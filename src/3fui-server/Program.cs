using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FFmpegFreeUI;
using linux3fui.Server;

var config = ServerConfig.FromEnvironment();

// 数据目录注入核心库（预设、设置、队列缓存、证书都存于此）
路径助手.数据目录 = config.DataDir;
Directory.CreateDirectory(config.DataDir);
设置_v6.启动时加载设置();

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
    options.TimestampFormat = "HH:mm:ss ";
    options.SingleLine = true;
});
builder.WebHost.ConfigureKestrel(options =>
{
    options.AddServerHeader = false;
    options.ListenAnyIP(config.HttpPort);
    options.ListenAnyIP(config.HttpsPort, listen =>
        options.ApplicationServices.GetRequiredService<TlsManager>().ConfigureHttpsPort(listen));
});
builder.Services.AddSingleton(config);
builder.Services.AddSingleton<QueueRealtime>();
builder.Services.AddSingleton<TlsManager>();
builder.Services.AddSingleton<MediaProbe>();
builder.Services.AddSingleton<AgentProxy>();
builder.Services.AddSingleton<ModelsDevCatalog>();
builder.Services.AddSingleton<PerfMonitor>();
builder.Services.AddSingleton(new AuthService(config.DataDir));
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = null;
    options.SerializerOptions.Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
    options.SerializerOptions.Converters.Add(new linux3fui.Server.LenientEnumJsonConverter());
});

var app = builder.Build();
var tls = app.Services.GetRequiredService<TlsManager>();
var realtime = app.Services.GetRequiredService<QueueRealtime>();
var mediaProbe = app.Services.GetRequiredService<MediaProbe>();
var agentProxy = app.Services.GetRequiredService<AgentProxy>();
var agentCatalog = app.Services.GetRequiredService<ModelsDevCatalog>();
var perf = app.Services.GetRequiredService<PerfMonitor>();
var auth = app.Services.GetRequiredService<AuthService>();
var logger = app.Logger;

// 登录认证中间件：启用后，除 /api/auth/* 外的所有 API 与 WebSocket 都要求有效会话 Cookie。
// 静态页面照常返回（SPA 自己显示登录界面），API 401 由前端拦截跳登录。
app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value ?? "";
    var isAuthApi = path.StartsWith("/api/auth", StringComparison.OrdinalIgnoreCase);
    var isApi = path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)
                || path.Equals("/ws", StringComparison.OrdinalIgnoreCase);
    if (isApi && !isAuthApi && auth.Enabled && !auth.Validate(context.Request.Cookies["fui_token"]))
    {
        context.Response.StatusCode = 401;
        await context.Response.WriteAsJsonAsync(new { error = "未登录或会话已过期" });
        return;
    }
    await next();
});

// HTTP → HTTPS 重定向（TLS 激活时）
app.Use(async (context, next) =>
{
    if (!context.Request.IsHttps && tls.Active && !context.WebSockets.IsWebSocketRequest)
    {
        var host = context.Request.Host.Host;
        var port = config.HttpsPort == 443 ? "" : ":" + config.HttpsPort;
        context.Response.Redirect($"https://{host}{port}{context.Request.Path}{context.Request.QueryString}");
        return;
    }
    await next();
});

app.UseWebSockets();

#region 登录认证
app.MapGet("/api/auth/status", (HttpContext context) =>
    Results.Json(auth.Status(auth.Validate(context.Request.Cookies["fui_token"])), JsonOptions.Compact));

app.MapPost("/api/auth/login", async (HttpContext context) =>
{
    var payload = await context.Request.ReadFromJsonAsync<LoginRequest>(JsonOptions.Compact);
    if (payload is null) return Results.BadRequest(new { error = "请求 JSON 无效" });
    if (!auth.Enabled) return Results.Ok(new { ok = true });
    var (ok, lockedSeconds) = auth.TryLogin(payload.username, payload.password);
    if (!ok)
    {
        return Results.Json(
            new { error = lockedSeconds > 0 ? $"错误次数过多，请 {lockedSeconds} 秒后再试" : "用户名或密码错误" },
            JsonOptions.Compact, statusCode: lockedSeconds > 0 ? 429 : 401);
    }
    var token = auth.CreateSession();
    context.Response.Cookies.Append("fui_token", token, new CookieOptions
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Strict,
        Secure = context.Request.IsHttps,
        Expires = DateTimeOffset.UtcNow.AddDays(7),
        Path = "/",
    });
    return Results.Ok(new { ok = true });
});

app.MapPost("/api/auth/logout", (HttpContext context) =>
{
    auth.Revoke(context.Request.Cookies["fui_token"]);
    context.Response.Cookies.Delete("fui_token");
    return Results.Ok(new { ok = true });
});

// 配置认证（启用/停用/改密码）。已启用时要求当前会话有效才能改。
app.MapPost("/api/auth/config", async (HttpContext context) =>
{
    if (auth.Enabled && !auth.Validate(context.Request.Cookies["fui_token"]))
        return Results.Unauthorized();
    var payload = await context.Request.ReadFromJsonAsync<AuthConfigRequest>(JsonOptions.Compact);
    if (payload is null) return Results.BadRequest(new { error = "请求 JSON 无效" });
    var error = auth.Configure(payload.enable, payload.username, payload.password);
    return error is null
        ? Results.Ok(new { ok = true })
        : Results.BadRequest(new { error });
});
#endregion

#region 状态与能力
app.MapGet("/api/status", () => new
{
    name = "linux-3fui",
    version = typeof(FFmpegFreeUI.预设数据_v6).Assembly.GetName().Version?.ToString(),
    ffmpeg = 设置_v6.获取FFmpeg进程文件名(),
    ffprobe = 设置_v6.获取FFprobe进程文件名(),
    tls = new
    {
        active = tls.Active,
        port = config.HttpsPort,
        subject = tls.Certificate?.Subject,
        notAfter = tls.Certificate?.NotAfter,
    },
    mediaRoot = config.MediaRoot,
    dataDir = config.DataDir,
});

app.MapGet("/api/ffmpeg/info", () => Results.Content(mediaProbe.GetFfmpegInfo(), "application/json; charset=utf-8"));

app.MapGet("/api/hw/encoders", () =>
{
    var results = 编码器可用性_v6.待探测编码器列表.Select(名称 =>
    {
        var r = 编码器可用性_v6.查询(名称);
        return r is null
            ? new { 编码器 = 名称, 已探测 = false, 可用 = false, 失败摘录 = "", 探测时间 = DateTime.MinValue }
            : new { 编码器 = r.编码器, 已探测 = r.已探测, 可用 = r.可用, 失败摘录 = r.失败摘录, 探测时间 = r.探测时间 };
    });
    return Results.Json(results, JsonOptions.Compact);
});

app.MapPost("/api/hw/encoders/reprobe", () =>
{
    _ = 编码器可用性_v6.强制重探测Async();
    return Results.Accepted();
});
#endregion

#region 预设
app.MapGet("/api/presets", () =>
{
    var user = Directory.Exists(预设管理_v6.获取预设目录("用户自定义"))
        ? Directory.GetFiles(预设管理_v6.获取预设目录("用户自定义"), "*.json").Select(name => Path.GetFileNameWithoutExtension(name) ?? "").OrderBy(x => x).ToList()
        : new List<string>();
    var community = Directory.Exists(预设管理_v6.获取预设目录("从社区下载"))
        ? Directory.GetFiles(预设管理_v6.获取预设目录("从社区下载"), "*.json").Select(name => Path.GetFileNameWithoutExtension(name) ?? "").OrderBy(x => x).ToList()
        : new List<string>();
    return Results.Json(new { user, community }, JsonOptions.Compact);
});

app.MapGet("/api/builtin-presets", () =>
    Results.Json(开发者内置预设_v6.获取全部().Select(item => new { 名称 = item.名称, 数据 = item.数据 }), JsonOptions.Compact));

static string ResolvePresetPath(string source, string name)
{
    var folder = source.Equals("community", StringComparison.OrdinalIgnoreCase)
        ? 预设管理_v6.获取预设目录("从社区下载")
        : 预设管理_v6.获取预设目录("用户自定义");
    return Path.Combine(folder, 预设管理_v6.安全文件名(name) + ".json");
}

app.MapGet("/api/presets/{source}/{name}", (string source, string name, HttpRequest request) =>
{
    var path = ResolvePresetPath(source, name);
    if (!File.Exists(path)) return Results.NotFound(new { error = "预设不存在" });
    var preset = 预设管理_v6.读取预设文件(path);
    if (request.Query.ContainsKey("download"))
        return Results.File(JsonSerializer.SerializeToUtf8Bytes(preset, JsonOptions.Compact), "application/json; charset=utf-8", name + ".3fui");
    return Results.Json(preset, JsonOptions.Compact);
});

app.MapPost("/api/presets/{source}/{name}", async (string source, string name, HttpRequest request) =>
{
    预设数据_v6 preset;
    try
    {
        preset = await request.ReadFromJsonAsync<预设数据_v6>(JsonOptions.Compact) ?? new 预设数据_v6();
    }
    catch (JsonException)
    {
        return Results.BadRequest(new { error = "预设 JSON 无效" });
    }
    var path = ResolvePresetPath(source, name);
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    预设管理_v6.写入预设文件(path, preset, false);
    return Results.Ok(new { path });
});

app.MapDelete("/api/presets/{source}/{name}", (string source, string name) =>
{
    var path = ResolvePresetPath(source, name);
    if (!File.Exists(path)) return Results.NotFound(new { error = "预设不存在" });
    File.Delete(path);
    return Results.Ok(new { deleted = path });
});

app.MapPost("/api/presets/import", async (HttpRequest request) =>
{
    if (!request.HasFormContentType) return Results.BadRequest(new { error = "需要 multipart/form-data" });
    var form = await request.ReadFormAsync();
    var file = form.Files.FirstOrDefault();
    if (file is null || file.Length == 0 || file.Length > 10 * 1024 * 1024)
        return Results.BadRequest(new { error = "请上传一个不超过 10MB 的预设文件" });
    using var stream = file.OpenReadStream();
    using var document = await JsonDocument.ParseAsync(stream);
    var folder = 预设管理_v6.获取预设目录("用户自定义");
    Directory.CreateDirectory(folder);
    var name = 预设管理_v6.安全文件名(Path.GetFileNameWithoutExtension(file.FileName));
    var target = Path.Combine(folder, name + ".json");
    var index = 1;
    while (File.Exists(target)) target = Path.Combine(folder, $"{name}_{index++}.json");
    await File.WriteAllTextAsync(target, document.RootElement.GetRawText());
    return Results.Ok(new { name = Path.GetFileNameWithoutExtension(target), path = target });
});

app.MapGet("/api/encoder-db", () => Results.Json(new
{
    videoCategories = 视频编码器数据库_v6.全部分类,
    videoEncoders = 视频编码器数据库_v6.全部编码器,
    audioEncoders = 音频编码器数据库_v6.全部编码器,
}, JsonOptions.Compact));

// 参数总览页：按当前参数生成 ffmpeg 命令行模板（与队列执行走同一套核心逻辑）
app.MapPost("/api/preset/preview", async (HttpRequest request) =>
{
    PresetPreviewRequest? payload;
    try
    {
        payload = await request.ReadFromJsonAsync<PresetPreviewRequest>(JsonOptions.Compact);
    }
    catch (JsonException)
    {
        return Results.BadRequest(new { error = "预设 JSON 无效" });
    }
    if (payload?.preset is null) return Results.BadRequest(new { error = "请求 JSON 无效" });
    var input = string.IsNullOrWhiteSpace(payload.input) ? "<输入文件>" : payload.input;
    var output = string.IsNullOrWhiteSpace(payload.output) ? "<输出文件>" : payload.output;
    try
    {
        预设管理_v6.初始化空集合(payload.preset);
        var 阶段列表 = 预设管理_v6.生成阶段化命令行(payload.preset, input, output);
        return Results.Json(new
        {
            命令行 = 预设管理_v6.生成命令行展示文本(payload.preset, input, output),
            阶段 = 阶段列表.Select(item => new { 阶段 = item.阶段.ToString(), item.命令行, item.说明 }),
        }, JsonOptions.Compact);
    }
    catch (Exception ex)
    {
        return Results.Json(new { 命令行 = "", 错误 = ex.Message }, JsonOptions.Compact);
    }
});
#endregion

#region 队列
app.MapGet("/api/queue", () => Results.Json(编码队列_v6.获取队列快照().Select(QueueRealtime.TaskDto), JsonOptions.Compact));

app.MapGet("/api/queue/tasks/{id}", (string id) =>
{
    var task = 编码队列_v6.根据ID获取任务(id);
    if (task is null) return Results.NotFound(new { error = "任务不存在" });
    // 任务对象的日志缓存是私有字段，直接序列化拿不到——显式调 获取日志快照数据()；
    // 预设任务的 命令行 属性恒为空（只有纯命令行任务会填），这里用预设数据现场生成
    var 命令行 = task.命令行;
    if (string.IsNullOrWhiteSpace(命令行) && task.预设数据 is not null)
    {
        try { 命令行 = 预设管理_v6.生成命令行展示文本(task.预设数据, task.输入文件, task.输出文件); }
        catch { /* 生成失败就留空 */ }
    }
    var snapshot = task.获取日志快照数据();
    return Results.Json(new
    {
        task.ID,
        task.任务名称,
        task.输入文件,
        task.输出文件,
        状态 = task.状态.ToString(),
        命令行,
        task.实时输出,
        task.最新底部日志文本,
        task.最新底部日志是否错误,
        task.预设编码器,
        task.实际编码器,
        task.编码器切换记录,
        日志快照 = snapshot,
        错误列表 = task.错误列表,
        步骤 = task.步骤.Select(step => new { step.显示名称, 状态 = step.状态.ToString(), step.实际执行参数 }),
    }, JsonOptions.Compact);
});

static IEnumerable<string> ExpandInputs(IEnumerable<string> files)
{
    foreach (var file in files)
    {
        if (string.IsNullOrWhiteSpace(file)) continue;
        if (Directory.Exists(file))
        {
            foreach (var child in Directory.GetFiles(file, "*", SearchOption.AllDirectories))
                yield return child;
        }
        else
        {
            yield return file;
        }
    }
}

app.MapPost("/api/queue/tasks", async (HttpRequest request) =>
{
    var payload = await request.ReadFromJsonAsync<AddTasksRequest>(JsonOptions.Compact);
    if (payload is null) return Results.BadRequest(new { error = "请求 JSON 无效" });
    var files = ExpandInputs(payload.files).ToList();
    if (files.Count == 0) return Results.BadRequest(new { error = "没有可添加的文件" });
    预设管理_v6.初始化空集合(payload.preset);
    var tasks = 编码队列_v6.批量添加预设任务(files, payload.preset, payload.预设名称 ?? "");
    return Results.Json(tasks.Select(t => new { t.ID, t.任务名称, t.输入文件, t.输出文件 }), JsonOptions.Compact);
});

app.MapPost("/api/queue/commandline", async (HttpRequest request) =>
{
    var payload = await request.ReadFromJsonAsync<CommandLineRequest>(JsonOptions.Compact);
    if (payload is null || string.IsNullOrWhiteSpace(payload.args))
        return Results.BadRequest(new { error = "命令行不能为空" });
    var task = 编码队列_v6.添加命令行任务(payload.args, payload.name ?? "命令行任务", payload.output ?? "", payload.input ?? "");
    return Results.Json(new { task.ID, task.任务名称 }, JsonOptions.Compact);
});

app.MapPost("/api/queue/action", async (HttpRequest request) =>
{
    var payload = await request.ReadFromJsonAsync<QueueActionRequest>(JsonOptions.Compact);
    if (payload is null || payload.ids is null || payload.ids.Count == 0)
        return Results.BadRequest(new { error = "缺少任务 ID" });
    switch (payload.action)
    {
        case "start": 编码队列_v6.开始任务(payload.ids); break;
        case "pause": 编码队列_v6.暂停任务(payload.ids); break;
        case "resume": 编码队列_v6.恢复任务(payload.ids); break;
        case "stop": 编码队列_v6.停止任务(payload.ids); break;
        case "reset": 编码队列_v6.重置任务(payload.ids); break;
        case "remove": 编码队列_v6.移除任务(payload.ids); break;
        default: return Results.BadRequest(new { error = "未知操作：" + payload.action });
    }
    return Results.Ok(new { ok = true });
});

app.MapPost("/api/queue/reorder", async (HttpRequest request) =>
{
    var payload = await request.ReadFromJsonAsync<ReorderRequest>(JsonOptions.Compact);
    if (payload is null) return Results.BadRequest(new { error = "请求 JSON 无效" });
    var ok = 编码队列_v6.重新排序(payload.ids);
    return Results.Json(new { ok });
});

app.MapPost("/api/queue/sync-preset", async (HttpRequest request) =>
{
    var payload = await request.ReadFromJsonAsync<SyncPresetRequest>(JsonOptions.Compact);
    if (payload is null || payload.preset is null) return Results.BadRequest(new { error = "请求 JSON 无效" });
    预设管理_v6.初始化空集合(payload.preset);
    var result = payload.ids is { Count: > 0 }
        ? 编码队列_v6.同步指定未处理预设任务(payload.ids, payload.preset)
        : 编码队列_v6.同步未处理预设任务(payload.preset);
    return Results.Json(result, JsonOptions.Compact);
});

app.MapPost("/api/encoder-switch/respond", async (HttpRequest request) =>
{
    var payload = await request.ReadFromJsonAsync<EncoderSwitchRespondRequest>(JsonOptions.Compact);
    if (payload is null || string.IsNullOrWhiteSpace(payload.任务ID) || string.IsNullOrWhiteSpace(payload.选择))
        return Results.BadRequest(new { error = "请求 JSON 无效" });
    var task = 编码队列_v6.根据ID获取任务(payload.任务ID);
    if (task is null) return Results.BadRequest(new { error = "任务不存在" });
    switch (payload.选择)
    {
        case "once":
            return Results.Json(new { ok = true, 已写回预设 = false, 消息 = "已按本次任务处理" }, JsonOptions.Compact);
        case "preset":
        {
            // 任务名可能已被 应用任务名称混淆 改写（设置开启时），此处直接用内部任务对象的真实字段匹配用户预设文件名
            var 记录 = task.编码器切换记录.Count > 0 ? task.编码器切换记录[^1] : null;
            // 预设定位：优先任务入队时记录的预设名称（任务名可能被混淆改写，不一定是预设名）
            var 预设名 = !string.IsNullOrWhiteSpace(task.预设名称) ? task.预设名称 : task.任务名称;
            var 目录 = 预设管理_v6.获取预设目录("用户自定义");
            var 路径 = Path.Combine(目录, 预设管理_v6.安全文件名(预设名) + ".json");
            if (!File.Exists(路径)) return Results.BadRequest(new { error = $"未找到预设「{预设名}」，仅本次生效" });
            if (task.预设数据 is null) return Results.BadRequest(new { error = "任务不含预设数据，无法写回" });
            预设管理_v6.写入预设文件(路径, task.预设数据, false);
            var 消息 = 记录 is null
                ? $"已将切换后的参数写回预设「{预设名}」"
                : $"已将编码器切换（{记录.原编码器} → {记录.新编码器}）写回预设「{预设名}」";
            return Results.Json(new { ok = true, 已写回预设 = true, 消息 }, JsonOptions.Compact);
        }
        default:
            return Results.BadRequest(new { error = "未知选择：" + payload.选择 });
    }
});
#endregion

#region 媒体探测与浏览
app.MapGet("/api/probe", (string path) =>
{
    if (string.IsNullOrWhiteSpace(path)) return Results.BadRequest(new { error = "缺少 path 参数" });
    var (ok, result) = mediaProbe.Probe(path);
    if (!ok)
    {
        return Results.Json(new { error = result }, JsonOptions.Compact, statusCode: StatusCodes.Status422UnprocessableEntity);
    }
    // 直接用 ffprobe 原始 JSON 响应：既避免 JsonDocument 提前释放（原 500 的根因），
    // 也省掉一次解析/再序列化，字段完整不丢。
    return Results.Content(result, "application/json; charset=utf-8");
});

app.MapGet("/api/browse", (string? path) => Results.Json(mediaProbe.Browse(path ?? ""), JsonOptions.Compact));
#endregion

#region 设置
// 密钥不在接口明文回显：GET 打码为哨兵值；PUT 收到哨兵值视为「未修改」保留原值，空字符串视为「清除密钥」。
// 前端设置页整对象回读回写，哨兵透传不会覆盖真密钥；改密钥走 /api/agent/config 专用端点。
const string MaskedSecret = "***";

static JsonObject SanitizedSettings()
{
    var settings = 设置_v6.实例对象;
    var node = JsonSerializer.SerializeToNode(settings, JsonOptions.Compact)!.AsObject();
    if (!string.IsNullOrEmpty(settings.AgentApiKey))
        node[nameof(设置_v6.AgentApiKey)] = MaskedSecret;
    if (!string.IsNullOrEmpty(settings.MirrorChyanCDK))
        node[nameof(设置_v6.MirrorChyanCDK)] = MaskedSecret;
    return node;
}

app.MapGet("/api/settings", () => Results.Json(SanitizedSettings(), JsonOptions.Compact));

app.MapPut("/api/settings", async (HttpRequest request) =>
{
    设置_v6? updated;
    try
    {
        updated = await request.ReadFromJsonAsync<设置_v6>(JsonOptions.Compact);
    }
    catch (JsonException)
    {
        return Results.BadRequest(new { error = "设置 JSON 无效" });
    }
    if (updated is null) return Results.BadRequest(new { error = "请求体为空" });
    if (updated.AgentApiKey == MaskedSecret)
        updated.AgentApiKey = 设置_v6.实例对象.AgentApiKey;
    if (updated.MirrorChyanCDK == MaskedSecret)
        updated.MirrorChyanCDK = 设置_v6.实例对象.MirrorChyanCDK;
    var wasListening = 端口监听_v6.是否正在运行;
    var oldAutoStart = 设置_v6.实例对象.自动开始任务选项;
    设置_v6.实例对象 = updated;
    设置_v6.退出时保存设置();
    if (设置_v6.实例对象.是否监听端口 && !wasListening)
        端口监听_v6.启动客户端();
    else if (!设置_v6.实例对象.是否监听端口 && wasListening)
        端口监听_v6.停止客户端();
    // 原版行为：切换「自动开始任务」时同步改写所有未处理任务的 允许自动启动
    if (updated.自动开始任务选项 != oldAutoStart)
        编码队列_v6.应用自动开始任务设置(updated.自动开始任务选项 == 0);
    return Results.Json(SanitizedSettings(), JsonOptions.Compact);
});
#endregion

#region TLS（声明式 HTTPS）
app.MapGet("/api/tls", () => Results.Json(new
{
    active = tls.Active,
    port = config.HttpsPort,
    subject = tls.Certificate?.Subject,
    issuer = tls.Certificate?.Issuer,
    notBefore = tls.Certificate?.NotBefore,
    notAfter = tls.Certificate?.NotAfter,
    certPath = tls.CertPath,
    envConfigured = config.TlsCertPath is not null,
}));

app.MapPost("/api/tls", async (HttpRequest request) =>
{
    string certPem, keyPem;
    if (request.HasFormContentType)
    {
        var form = await request.ReadFormAsync();
        var cert = form.Files.GetFile("cert");
        var key = form.Files.GetFile("key");
        if (cert is null || key is null) return Results.BadRequest(new { error = "需要上传 cert 与 key 两个文件" });
        using var certReader = new StreamReader(cert.OpenReadStream(), Encoding.UTF8);
        using var keyReader = new StreamReader(key.OpenReadStream(), Encoding.UTF8);
        certPem = await certReader.ReadToEndAsync();
        keyPem = await keyReader.ReadToEndAsync();
    }
    else
    {
        var payload = await request.ReadFromJsonAsync<TlsUploadRequest>(JsonOptions.Compact);
        if (payload is null || string.IsNullOrWhiteSpace(payload.certPem) || string.IsNullOrWhiteSpace(payload.keyPem))
            return Results.BadRequest(new { error = "certPem 与 keyPem 不能为空" });
        certPem = payload.certPem;
        keyPem = payload.keyPem;
    }

    var error = tls.Install(certPem.Trim(), keyPem.Trim());
    if (error is not null) return Results.BadRequest(new { error });
    return Results.Json(new
    {
        ok = true,
        port = config.HttpsPort,
        subject = tls.Certificate?.Subject,
        notAfter = tls.Certificate?.NotAfter,
    });
});

app.MapDelete("/api/tls", () =>
{
    tls.Uninstall();
    return Results.Ok(new { ok = true });
});
#endregion

#region Agent
app.MapGet("/api/agent/config", () =>
{
    var settings = 设置_v6.实例对象;
    return Results.Json(new
    {
        endpoint = settings.AgentEndPoint,
        hasApiKey = !string.IsNullOrWhiteSpace(settings.AgentApiKey),
        model = settings.AgentModelId,
        reasoningEffort = settings.Agent推理级别,
        extraHeaders = settings.Agent附加请求头,
    });
});

app.MapPut("/api/agent/config", async (HttpRequest request) =>
{
    var payload = await request.ReadFromJsonAsync<AgentConfigRequest>(JsonOptions.Compact);
    if (payload is null) return Results.BadRequest(new { error = "请求 JSON 无效" });
    var settings = 设置_v6.实例对象;
    if (payload.endpoint is not null) settings.AgentEndPoint = payload.endpoint;
    if (payload.apiKey is not null) settings.AgentApiKey = payload.apiKey;
    if (payload.model is not null) settings.AgentModelId = payload.model;
    if (payload.reasoningEffort is not null) settings.Agent推理级别 = payload.reasoningEffort;
    if (payload.extraHeaders is not null) settings.Agent附加请求头 = payload.extraHeaders;
    设置_v6.退出时保存设置();
    return Results.Ok(new { ok = true });
});

app.MapPost("/api/agent/chat", (HttpContext context) =>
    agentProxy.ProxyChatAsync(context, app.Logger));

// ── models.dev 厂商/模型目录（公开替代上游赞助者专用的 sp-agent-endpoints.json）──
app.MapGet("/api/agent/catalog", async (HttpRequest request, CancellationToken cancellationToken) =>
{
    var (body, status) = await agentCatalog.GetProvidersAsync(request.Query.ContainsKey("refresh"), cancellationToken);
    return Results.Json(body, JsonOptions.Compact, statusCode: status);
});

app.MapGet("/api/agent/catalog/{providerId}", async (string providerId, CancellationToken cancellationToken) =>
{
    var (body, status) = await agentCatalog.GetModelsAsync(providerId, cancellationToken);
    return Results.Json(body, JsonOptions.Compact, statusCode: status);
});

// ── 扫描端点自身的模型列表（对齐上游 TryGetModelsAsync：无版本段时按 v1…v9、空前缀探测）──
app.MapPost("/api/agent/scan", async (HttpRequest request, CancellationToken cancellationToken) =>
{
    AgentScanRequest? payload = null;
    if (request.ContentLength is > 0)
    {
        try
        {
            payload = await request.ReadFromJsonAsync<AgentScanRequest>(JsonOptions.Compact);
        }
        catch (JsonException)
        {
            return Results.BadRequest(new { error = "请求 JSON 无效" });
        }
    }

    var outcome = await agentProxy.ScanModelsAsync(payload?.endpoint, payload?.apiKey, cancellationToken);
    if (!outcome.Ok)
        return Results.Json(new { error = outcome.Error }, JsonOptions.Compact, statusCode: outcome.StatusCode);

    return Results.Json(new
    {
        endpoint = outcome.Endpoint,
        prefix = outcome.Prefix,
        count = outcome.Models.Count,
        models = outcome.Models.Select(model => new { id = model.Id, ownedBy = model.OwnedBy }),
    }, JsonOptions.Compact);
});
#endregion

#region 性能监控
// 后台采样器随应用启停（2s 采样 /proc + nvidia-smi，环形缓冲 1 小时）
app.Lifetime.ApplicationStarted.Register(perf.Start);
app.Lifetime.ApplicationStopping.Register(perf.Stop);
app.MapGet("/api/perf", () => Results.Json(perf.Snapshot()));
app.MapGet("/api/perf/history", (HttpRequest request) =>
{
    int QueryInt(string key, int fallback, int min, int max) =>
        int.TryParse(request.Query[key], out var value) ? Math.Clamp(value, min, max) : fallback;
    var minutes = QueryInt("minutes", 60, 1, 120);
    var maxPoints = QueryInt("maxPoints", 720, 60, 3600);
    var includeCores = request.Query["cores"] == "1";
    return Results.Json(perf.History(minutes, maxPoints, includeCores));
});
#endregion

#region WebSocket 实时推送
app.Map("/ws", async (HttpContext context) =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }
    using var socket = await context.WebSockets.AcceptWebSocketAsync();
    realtime.AddSocket(socket);
    try
    {
        var buffer = new byte[4096];
        while (socket.State == WebSocketState.Open)
        {
            var result = await socket.ReceiveAsync(buffer, CancellationToken.None);
            if (result.MessageType == WebSocketMessageType.Close) break;
        }
    }
    catch
    {
    }
    finally
    {
        realtime.RemoveSocket(socket);
    }
});
#endregion

#region 静态前端（SPA）
var webRoot = app.Environment.WebRootPath;
if (Directory.Exists(webRoot))
{
    app.UseDefaultFiles();
    app.UseStaticFiles();
    app.MapFallbackToFile("index.html");
}
else
{
    app.MapGet("/", () => Results.Text("linux-3fui 服务运行中。前端未构建：请先构建 3fui-web 并将产物复制到 wwwroot。", "text/plain; charset=utf-8"));
}
#endregion

// 启动：恢复待处理任务缓存与 UDP 远程调用
if (编码队列_v6.存在未处理任务缓存())
{
    var restored = 编码队列_v6.加载未处理任务缓存();
    if (restored > 0) logger.LogInformation("已恢复 {Count} 个待处理任务", restored);
}
// 硬件编码器可用性探测：fire-and-forget，不阻塞启动（结果供任务启动前预检与 /api/hw/encoders 查询）
_ = 编码器可用性_v6.探测全部Async();
if (设置_v6.实例对象.是否监听端口)
{
    端口监听_v6.启动客户端();
    logger.LogInformation("UDP 远程调用监听：端口 {Port}", 设置_v6.实例对象.监听的端口);
}

// TLS 声明式启动（环境变量或已上传证书）
tls.LoadExisting();

app.Lifetime.ApplicationStopping.Register(() =>
{
    logger.LogInformation("正在停止：保存设置与待处理任务缓存");
    try { 编码队列_v6.停止所有进行中任务(); } catch { }
    try { 编码队列_v6.保存未处理任务缓存(); } catch { }
    try { 设置_v6.退出时保存设置(); } catch { }
});

logger.LogInformation("linux-3fui 启动：HTTP {HttpPort}，HTTPS {HttpsPort}（{Tls}），数据目录 {DataDir}，ffmpeg={Ffmpeg}",
    config.HttpPort, config.HttpsPort, tls.Active ? "已启用" : "未启用", config.DataDir, 设置_v6.获取FFmpeg进程文件名());

app.Run();

internal sealed record AddTasksRequest(List<string> files, 预设数据_v6 preset, string? 预设名称);
internal sealed record LoginRequest(string? username, string? password);
internal sealed record AuthConfigRequest(bool enable, string? username, string? password);
internal sealed record CommandLineRequest(string args, string? name, string? output, string? input);
internal sealed record QueueActionRequest(string action, List<string> ids);
internal sealed record ReorderRequest(List<string> ids);
internal sealed record SyncPresetRequest(List<string>? ids, 预设数据_v6 preset);
internal sealed record PresetPreviewRequest(预设数据_v6 preset, string? input, string? output);
internal sealed record TlsUploadRequest(string certPem, string keyPem);
internal sealed record AgentConfigRequest(string? endpoint, string? apiKey, string? model, string? reasoningEffort, string? extraHeaders);
// 字段可省：省略即用服务端已保存的端点与密钥
internal sealed record AgentScanRequest(string? endpoint, string? apiKey);
internal sealed record EncoderSwitchRespondRequest(string 任务ID, string 选择);
