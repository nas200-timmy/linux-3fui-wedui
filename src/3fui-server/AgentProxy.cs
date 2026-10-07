using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FFmpegFreeUI;

namespace linux3fui.Server;

/// <summary>
/// Agent 代理：把浏览器请求转发到任何 OpenAI SDK 兼容端点（与上游 3FUI Agent 相同的接入方式），
/// 支持流式 SSE 透传、附加请求头/Body、推理级别参数。API Key 只保存在服务端，不落浏览器。
/// </summary>
public sealed class AgentProxy
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromMinutes(10) };

    private static readonly JsonSerializerOptions Compact = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public string NormalizeEndpoint(string? endpoint)
    {
        var value = (endpoint ?? "").Trim().TrimEnd('/');
        if (value == "") return "";
        // 已带版本段（/v1、/v4、/v1beta…）就原样用——上游 GetEndpointVersion 同款判定。
        // 旧实现只认 /v1 与 /v1beta，会把 models.dev 给的 .../paas/v4 拼成 .../paas/v4/v1（错）。
        return VersionSuffix(value) == "" ? value + "/v1" : value;
    }

    /// <summary>取端点路径末尾的版本段（v1 / v4 / v1beta…），没有则返回空串。</summary>
    private static string VersionSuffix(string endpoint)
    {
        var path = Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ? uri.AbsolutePath : endpoint;
        var match = Regex.Match(path.TrimEnd('/'), @"(?:^|/)(v\d+[a-z]*)$", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.ToLowerInvariant() : "";
    }

    public async Task ProxyChatAsync(HttpContext context, ILogger logger)
    {
        var settings = 设置_v6.实例对象;
        var endpoint = NormalizeEndpoint(settings.AgentEndPoint);
        if (endpoint == "")
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { error = "尚未配置 Agent 端点" });
            return;
        }
        using var reader = new StreamReader(context.Request.Body, Encoding.UTF8);
        var requestText = await reader.ReadToEndAsync();
        JsonDocument request;
        try
        {
            request = JsonDocument.Parse(requestText);
        }
        catch
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { error = "请求 JSON 无效" });
            return;
        }
        using (request)
        {
            var root = request.RootElement;
            var messages = root.GetProperty("messages");
            var model = root.TryGetProperty("model", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : settings.AgentModelId;
            var stream = !root.TryGetProperty("stream", out var s) || s.ValueKind != JsonValueKind.False;

            using var outgoing = new MemoryStream();
            using (var writer = new Utf8JsonWriter(outgoing))
            {
                writer.WriteStartObject();
                writer.WritePropertyName("model");
                writer.WriteStringValue(string.IsNullOrWhiteSpace(model) ? throw new InvalidOperationException("未指定模型") : model);
                writer.WritePropertyName("stream");
                writer.WriteBooleanValue(stream);
                if (root.TryGetProperty("reasoning_effort", out var effort) && effort.ValueKind == JsonValueKind.String)
                {
                    writer.WritePropertyName("reasoning_effort");
                    effort.WriteTo(writer);
                }
                else if (!string.IsNullOrWhiteSpace(settings.Agent推理级别))
                {
                    writer.WritePropertyName("reasoning_effort");
                    writer.WriteStringValue(settings.Agent推理级别);
                }
                if (root.TryGetProperty("max_tokens", out var maxTokens))
                {
                    writer.WritePropertyName("max_tokens");
                    maxTokens.WriteTo(writer);
                }
                writer.WritePropertyName("messages");
                messages.WriteTo(writer);
                writer.WriteEndObject();
            }

            using var forward = new HttpRequestMessage(HttpMethod.Post, endpoint + "/chat/completions");
            forward.Content = new ByteArrayContent(outgoing.ToArray());
            forward.Content.Headers.ContentType = new("application/json") { CharSet = "utf-8" };
            ApplyCredentials(forward, settings, settings.AgentApiKey);

            try
            {
                using var response = await Client.SendAsync(forward, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted);
                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync(context.RequestAborted);
                    context.Response.StatusCode = (int)response.StatusCode;
                    await context.Response.WriteAsync(body, context.RequestAborted);
                    return;
                }
                context.Response.StatusCode = 200;
                if (stream)
                {
                    context.Response.ContentType = "text/event-stream; charset=utf-8";
                    await context.Response.StartAsync(context.RequestAborted);
                    await response.Content.CopyToAsync(context.Response.Body, context.RequestAborted);
                }
                else
                {
                    context.Response.ContentType = "application/json; charset=utf-8";
                    await response.Content.CopyToAsync(context.Response.Body, context.RequestAborted);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Agent 请求失败");
                if (!context.Response.HasStarted)
                {
                    context.Response.StatusCode = StatusCodes.Status502BadGateway;
                    await context.Response.WriteAsJsonAsync(new { error = "Agent 端点请求失败：" + ex.Message });
                }
            }
        }
    }

    /// <summary>
    /// 扫描端点自身的模型列表，对齐上游 AgentEndpointClient.TryGetModelsAsync + GetApiPrefixes：
    /// 端点已带版本段就只试它，否则依次试 v1…v9 与无前缀，取第一个能解析出模型列表的结果。
    /// 与 /api/agent/chat 一样只用服务端保存的密钥，密钥不出浏览器。
    /// </summary>
    public async Task<AgentScanOutcome> ScanModelsAsync(string? endpointOverride, string? apiKeyOverride, CancellationToken cancellationToken)
    {
        var settings = 设置_v6.实例对象;
        var endpoint = (endpointOverride ?? settings.AgentEndPoint ?? "").Trim().TrimEnd('/');
        if (endpoint == "")
            return AgentScanOutcome.Failed(StatusCodes.Status400BadRequest, "尚未配置 Agent 端点");

        var apiKey = apiKeyOverride ?? settings.AgentApiKey;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));

        var prefixes = VersionSuffix(endpoint) == ""
            ? new[] { "v1", "v2", "v3", "v4", "v5", "v6", "v7", "v8", "v9", "" }
            : new[] { "" };

        var lastError = "端点没有返回模型列表。";
        var lastStatus = StatusCodes.Status502BadGateway;
        foreach (var prefix in prefixes)
        {
            var url = prefix == "" ? endpoint + "/models" : endpoint + "/" + prefix + "/models";
            try
            {
                using var forward = new HttpRequestMessage(HttpMethod.Get, url);
                ApplyCredentials(forward, settings, apiKey);
                using var response = await Client.SendAsync(forward, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                var body = await response.Content.ReadAsStringAsync(timeout.Token);
                if (!response.IsSuccessStatusCode)
                {
                    lastStatus = (int)response.StatusCode;
                    lastError = DescribeFailure(lastStatus, body);
                    // 鉴权问题换前缀也没用，直接收工
                    if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) break;
                    continue;
                }

                var models = ParseModels(body);
                if (models is null)
                {
                    lastStatus = StatusCodes.Status502BadGateway;
                    lastError = "端点未返回 OpenAI 兼容的模型列表（响应里没有 data 数组）。";
                    continue;
                }

                return new AgentScanOutcome
                {
                    Ok = true,
                    Endpoint = url[..^"/models".Length],
                    Prefix = prefix,
                    StatusCode = StatusCodes.Status200OK,
                    Models = models,
                };
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                lastStatus = StatusCodes.Status504GatewayTimeout;
                lastError = "连接端点超时（30 秒）";
            }
            catch (Exception ex)
            {
                lastStatus = StatusCodes.Status502BadGateway;
                lastError = "无法连接端点：" + ex.Message;
            }
        }

        return AgentScanOutcome.Failed(lastStatus, lastError);
    }

    private static void ApplyCredentials(HttpRequestMessage request, 设置_v6 settings, string? apiKey)
    {
        if (!string.IsNullOrWhiteSpace(apiKey))
            request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + apiKey);
        foreach (var line in (settings.Agent附加请求头 ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = line.IndexOf(':');
            if (separator > 0)
                request.Headers.TryAddWithoutValidation(line[..separator].Trim(), line[(separator + 1)..].Trim());
        }
    }

    /// <summary>解析 OpenAI 兼容的 /models 响应；不是该形状返回 null（调用方继续试下一个前缀）。</summary>
    private static List<AgentScannedModel>? ParseModels(string body)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            return null;
        }

        using (document)
        {
            var root = document.RootElement;
            // 标准形状 {"object":"list","data":[…]}；少数网关直接返回顶层数组，一并容忍
            if (root.ValueKind == JsonValueKind.Array) return CollectModels(root);
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
                return CollectModels(data);
            return null;
        }
    }

    private static List<AgentScannedModel> CollectModels(JsonElement array)
    {
        var result = new List<AgentScannedModel>();
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var id = item.TryGetProperty("id", out var idElement) && idElement.ValueKind == JsonValueKind.String ? idElement.GetString() : null;
            if (string.IsNullOrWhiteSpace(id)) continue;
            var ownedBy = item.TryGetProperty("owned_by", out var owner) && owner.ValueKind == JsonValueKind.String ? owner.GetString() ?? "" : "";
            result.Add(new AgentScannedModel(id, ownedBy));
        }
        return result;
    }

    private static string DescribeFailure(int status, string body)
    {
        var reason = status switch
        {
            StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden => "API Key 缺失或无效",
            StatusCodes.Status404NotFound => "该地址没有 /models 接口",
            _ => "",
        };
        var text = reason == "" ? $"端点返回 {status}" : $"端点返回 {status}：{reason}";
        var detail = ExtractErrorMessage(body);
        return detail == "" ? text : $"{text}（{detail}）";
    }

    /// <summary>从错误响应里取可读信息（OpenAI 风格 error.message / error，或纯文本），截断防 HTML 错误页刷屏。</summary>
    private static string ExtractErrorMessage(string body)
    {
        var text = (body ?? "").Trim();
        if (text == "") return "";
        try
        {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("error", out var error))
                {
                    if (error.ValueKind == JsonValueKind.String) return Limit(error.GetString() ?? "");
                    if (error.ValueKind == JsonValueKind.Object &&
                        error.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String)
                        return Limit(message.GetString() ?? "");
                }
                if (root.TryGetProperty("message", out var top) && top.ValueKind == JsonValueKind.String)
                    return Limit(top.GetString() ?? "");
            }
        }
        catch (JsonException)
        {
            if (!text.StartsWith('<')) return Limit(text);
        }
        return "";
    }

    private static string Limit(string value)
    {
        var clean = Regex.Replace(value, @"\s+", " ").Trim();
        return clean.Length <= 200 ? clean : clean[..200] + "…";
    }
}

/// <summary>端点模型扫描结果。</summary>
public sealed class AgentScanOutcome
{
    public bool Ok { get; init; }
    /// <summary>真正取到模型列表的 API 基地址（前缀被探明时会带上，如 .../v2）。</summary>
    public string Endpoint { get; init; } = "";
    public string Prefix { get; init; } = "";
    public int StatusCode { get; init; } = StatusCodes.Status200OK;
    public string Error { get; init; } = "";
    public List<AgentScannedModel> Models { get; init; } = new();

    public static AgentScanOutcome Failed(int statusCode, string error) =>
        new() { Ok = false, StatusCode = statusCode, Error = error };
}

/// <summary>扫描到的单个模型（Id 即请求里 model 字段要填的值）。</summary>
public sealed record AgentScannedModel(string Id, string OwnedBy);
