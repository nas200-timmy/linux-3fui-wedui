using System.Text;
using System.Text.Json;
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
        if (value.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) ||
            value.EndsWith("/v1beta", StringComparison.OrdinalIgnoreCase))
            return value;
        return value + "/v1";
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
            if (!string.IsNullOrWhiteSpace(settings.AgentApiKey))
                forward.Headers.TryAddWithoutValidation("Authorization", "Bearer " + settings.AgentApiKey);
            foreach (var line in (settings.Agent附加请求头 ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var separator = line.IndexOf(':');
                if (separator > 0)
                    forward.Headers.TryAddWithoutValidation(line[..separator].Trim(), line[(separator + 1)..].Trim());
            }

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
}
