using System.Text.Json;
using System.Text.Json.Nodes;

namespace linux3fui.Server;

/// <summary>
/// models.dev 模型目录：厂商 → OpenAI 兼容 base URL / 文档 / 环境变量，以及厂商 → 模型列表。
/// 上游（Lake1059/FFmpegFreeUI）的厂商清单来自赞助者专用的远端 sp-agent-endpoints.json，
/// 公开版拿不到，这里改用 models.dev 作为公开替代。
/// 原始 api.json（约 5MB）缓存进数据目录（TTL 24h，容器重建不丢）；断网时降级用过期缓存并标记 stale。
/// </summary>
public sealed class ModelsDevCatalog
{
    private const string SourceUrl = "https://models.dev/api.json";
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(24);

    private readonly string _cachePath;
    // 出网抖动实测存在：同一台机器上 5MB 的 api.json 可能 2s 拉完，也可能 60s 都拉不完。
    // 所以压到 25s 并在失败后重试一次，别让用户对着「目录加载中…」等一分钟。
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(25) };
    private readonly SemaphoreSlim _fetchGate = new(1, 1);
    private readonly object _stateLock = new();
    private readonly Dictionary<string, JsonArray> _modelCache = new(StringComparer.OrdinalIgnoreCase);

    private JsonDocument? _document;
    private Dictionary<string, JsonElement> _providers = new(StringComparer.OrdinalIgnoreCase);
    private JsonArray? _providerList;
    private DateTimeOffset _fetchedAt;
    private bool _stale;
    private string _lastError = "";

    public ModelsDevCatalog(ServerConfig config)
    {
        _cachePath = Path.Combine(config.DataDir, "models.dev.json");
    }

    /// <summary>厂商列表（瘦身约 40KB）+ 抓取时间与是否在用过期缓存；StatusCode 用于直接回给前端。</summary>
    public async Task<(JsonObject Body, int StatusCode)> GetProvidersAsync(bool refresh, CancellationToken cancellationToken)
    {
        var error = await EnsureLoadedAsync(refresh, cancellationToken);
        if (error is not null)
            return (new JsonObject { ["error"] = error }, StatusCodes.Status502BadGateway);

        lock (_stateLock)
        {
            return (new JsonObject
            {
                ["fetchedAt"] = _fetchedAt.ToString("o"),
                ["stale"] = _stale,
                ["count"] = _providerList?.Count ?? 0,
                ["providers"] = _providerList?.DeepClone(),
            }, StatusCodes.Status200OK);
        }
    }

    /// <summary>单个厂商的模型列表（中位约 1.3KB，最大的 openrouter 约 52KB）。</summary>
    public async Task<(JsonObject Body, int StatusCode)> GetModelsAsync(string providerId, CancellationToken cancellationToken)
    {
        var error = await EnsureLoadedAsync(false, cancellationToken);
        if (error is not null)
            return (new JsonObject { ["error"] = error }, StatusCodes.Status502BadGateway);

        JsonArray models;
        JsonElement provider;
        lock (_stateLock)
        {
            if (!_providers.TryGetValue(providerId, out provider))
                return (new JsonObject { ["error"] = $"models.dev 目录中没有厂商 {providerId}" }, StatusCodes.Status404NotFound);
            if (!_modelCache.TryGetValue(providerId, out var cached))
            {
                cached = SlimModels(provider);
                _modelCache[providerId] = cached;
            }
            models = cached;
        }

        return (new JsonObject
        {
            ["id"] = providerId,
            ["stale"] = _stale,
            ["count"] = models.Count,
            ["models"] = models.DeepClone(),
        }, StatusCodes.Status200OK);
    }

    private async Task<string?> EnsureLoadedAsync(bool refresh, CancellationToken cancellationToken)
    {
        // 1) 内存里已有目录：新鲜就直接用；过期也先给出去并后台刷新
        //    —— 出网抖动时页面不该对着「目录加载中…」干等一分钟
        if (!refresh)
        {
            bool hasData;
            bool fresh;
            lock (_stateLock)
            {
                hasData = _providerList is not null;
                fresh = hasData && DateTimeOffset.UtcNow - _fetchedAt < Ttl;
                if (hasData && !fresh) _stale = true;
            }
            if (fresh) return null;
            if (hasData)
            {
                _ = RefreshInBackgroundAsync();
                return null;
            }

            // 2) 内存空但磁盘有缓存（容器重启后）：立刻用磁盘那份；过期才后台刷新
            if (await TryLoadDiskCacheAsync(cancellationToken))
            {
                if (_stale) _ = RefreshInBackgroundAsync();
                return null;
            }
        }

        // 3) 彻底没有缓存：只能等网络（这里才会出现最长 ~50s 的等待）
        return await FetchAsync(refresh, cancellationToken);
    }

    private async Task RefreshInBackgroundAsync()
    {
        try
        {
            await FetchAsync(true, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
        }
    }

    private async Task<bool> TryLoadDiskCacheAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_cachePath)) return false;
        try
        {
            var cached = await File.ReadAllTextAsync(_cachePath, cancellationToken);
            var fetchedAt = new DateTimeOffset(File.GetLastWriteTimeUtc(_cachePath), TimeSpan.Zero);
            Apply(cached, fetchedAt, stale: DateTimeOffset.UtcNow - fetchedAt >= Ttl);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            return false;
        }
    }

    private async Task<string?> FetchAsync(bool force, CancellationToken cancellationToken)
    {
        await _fetchGate.WaitAsync(cancellationToken);
        try
        {
            lock (_stateLock)
            {
                // 等锁期间可能已被别的请求填好；强制刷新（?refresh=1）不走这条捷径
                if (!force && _document is not null && DateTimeOffset.UtcNow - _fetchedAt < Ttl) return null;
            }

            for (var attempt = 1; attempt <= 2; attempt++)
            {
                try
                {
                    var json = await _http.GetStringAsync(SourceUrl, cancellationToken);
                    SaveCache(json);
                    Apply(json, DateTimeOffset.UtcNow, stale: false);
                    return null;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _lastError = ex.Message;
                }
                if (attempt == 1) await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }

            if (await TryLoadDiskCacheAsync(cancellationToken)) return null;

            return $"无法获取 models.dev 目录：{_lastError}";
        }
        finally
        {
            _fetchGate.Release();
        }
    }

    private void SaveCache(string json)
    {
        try
        {
            // 原子写：进程被杀不会留半个 JSON（与预设/设置一致）
            var temp = _cachePath + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, _cachePath, true);
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
        }
    }

    private void Apply(string json, DateTimeOffset fetchedAt, bool stale)
    {
        var document = JsonDocument.Parse(json);
        var providers = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        var list = new JsonArray();
        foreach (var item in document.RootElement.EnumerateObject())
        {
            providers[item.Name] = item.Value;
            list.Add(SlimProvider(item.Name, item.Value));
        }

        lock (_stateLock)
        {
            _document?.Dispose();
            _document = document;
            _providers = providers;
            _providerList = list;
            _modelCache.Clear();
            _fetchedAt = fetchedAt;
            _stale = stale;
        }
    }

    private static JsonObject SlimProvider(string fallbackId, JsonElement provider)
    {
        var env = new JsonArray();
        if (provider.TryGetProperty("env", out var envElement) && envElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in envElement.EnumerateArray())
                if (item.ValueKind == JsonValueKind.String) env.Add(item.GetString());
        }

        return new JsonObject
        {
            ["id"] = Text(provider, "id") is { Length: > 0 } id ? id : fallbackId,
            ["name"] = Text(provider, "name") is { Length: > 0 } name ? name : fallbackId,
            ["api"] = Text(provider, "api"),
            ["doc"] = Text(provider, "doc"),
            ["env"] = env.Count > 0 ? env : null,
            ["models"] = provider.TryGetProperty("models", out var models) && models.ValueKind == JsonValueKind.Object
                ? models.GetPropertyCount()
                : 0,
        };
    }

    private static JsonArray SlimModels(JsonElement provider)
    {
        var result = new JsonArray();
        if (!provider.TryGetProperty("models", out var models) || models.ValueKind != JsonValueKind.Object) return result;

        foreach (var entry in models.EnumerateObject())
        {
            var model = entry.Value;
            var id = Text(model, "id");
            if (string.IsNullOrWhiteSpace(id)) id = entry.Name;
            if (string.IsNullOrWhiteSpace(id)) continue;

            JsonElement limit = default;
            var hasLimit = model.TryGetProperty("limit", out limit) && limit.ValueKind == JsonValueKind.Object;
            JsonElement cost = default;
            var hasCost = model.TryGetProperty("cost", out cost) && cost.ValueKind == JsonValueKind.Object;

            result.Add(new JsonObject
            {
                ["id"] = id,
                ["name"] = Text(model, "name"),
                ["reasoning"] = Flag(model, "reasoning"),
                ["toolCall"] = Flag(model, "tool_call"),
                ["context"] = hasLimit ? Number(limit, "context") : null,
                ["costIn"] = hasCost ? Number(cost, "input") : null,
                ["costOut"] = hasCost ? Number(cost, "output") : null,
                ["releaseDate"] = Text(model, "release_date"),
            });
        }
        return result;
    }

    private static string? Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool Flag(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.True;

    private static double? Number(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : null;
}
