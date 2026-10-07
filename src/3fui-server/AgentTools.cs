using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using FFmpegFreeUI;

namespace linux3fui.Server;

/// <summary>
/// Agent 工具层：把"操作 linux-3fui 本体"的能力做成 OpenAI function calling 工具。
///
/// 权限沿用上游三档（设置_v6.Agent权限级别）：0 安全区域 / 1 环境控制 / 2 系统访问。
/// 本轮只移植 0+1 档；档位 2 的文件系统与 shell **没有实现**，因此目录里不会出现它们。
///
/// 执行范围分两类：
///   · server  —— 本文件实现，服务端直接调核心 VB 类（队列/预设/硬件/探测）
///   · browser —— 参数面板与准备文件的状态只活在浏览器里，由前端执行；服务端只负责"下发即授权"
/// 服务端工具**每次调用都会再校验一次权限**（对齐上游 Agent本地工具_v6.vb:235 的二次判定）。
/// </summary>
public sealed class AgentTools
{
    public const int LevelSafe = 0;
    public const int LevelEnvironment = 1;
    public const int LevelSystem = 2;

    /// <summary>工具返回给模型前的统一截断长度（上游在循环里截 16000）。</summary>
    private const int ResultLimit = 16000;

    private readonly MediaProbe _mediaProbe;
    private readonly PerfMonitor _perf;
    private readonly ServerConfig _config;

    public AgentTools(MediaProbe mediaProbe, PerfMonitor perf, ServerConfig config)
    {
        _mediaProbe = mediaProbe;
        _perf = perf;
        _config = config;
    }

    public static string LevelName(int level) => level switch
    {
        LevelEnvironment => "环境控制",
        LevelSystem => "系统访问",
        _ => "安全区域",
    };

    #region 目录

    private sealed record ToolDef(
        string Name,
        string Description,
        int Level,
        string Scope,
        bool Write,
        JsonObject Parameters);

    /// <summary>
    /// 17 个工具：0 档 3 个（参数面板，全在浏览器）、1 档 14 个（服务端 10 + 浏览器 4）。
    /// 工具名与上游 Agent本地工具_v6.vb 保持一致，便于模型把上游知识迁移过来。
    /// </summary>
    private static readonly ToolDef[] Definitions =
    [
        // ── 0 档：安全区域（常驻），参数面板类，全部由浏览器执行 ──
        new("get_parameter_panel_state",
            "读取当前参数面板（用户正在编辑的那份预设）的状态。默认只返回概览；可用 include_command_preview 让服务端按当前参数现场生成 ffmpeg 命令行；include_preset_json 返回完整预设 JSON（字段很多，非必要不要开）。",
            LevelSafe, "browser", false, Props(
                ("include_overview", Bool("是否返回参数概览（编码器/容器/质量控制等要点），默认 true", true), false),
                ("include_command_preview", Bool("是否返回按当前参数生成的 ffmpeg 命令行预览，默认 false", true), false),
                ("include_preset_json", Bool("是否返回完整预设 JSON，默认 false", true), false))),

        new("get_parameter_field_info",
            "查询参数面板字段的元信息：类型、当前值、候选值、取值规则。只读不改。可用 fields 指定精确字段名（预设数据_v6 的属性名），或用 query 按关键词模糊查。",
            LevelSafe, "browser", false, Props(
                ("fields", StrArr("要精确查询的字段名列表（预设数据_v6 属性名，如 输出容器、视频参数_编码器_具体编码）"), false),
                ("query", Str("按关键词模糊查，例如 字幕、色带、码率"), false),
                ("include_current_values", Bool("是否带上当前值，默认 true", true), false))),

        new("apply_parameter_panel_patch",
            "修改当前参数面板（用户正在编辑的预设）。changes 的键必须是预设数据_v6 的**属性名**，值为新值；也可以给 preset_json 整包替换。返回实际生效的字段（可能因门控/联动与你给的不同）。改完记得把结果告诉用户。",
            LevelSafe, "browser", true, Props(
                ("changes", Obj("要修改的字段：{预设数据_v6 属性名: 新值}，例如 {\"输出容器\":\"mkv\",\"视频参数_质量控制_值\":\"23\"}"), false),
                ("preset_json", Str("整包替换用的完整预设 JSON 字符串；与 changes 二选一，同时给时以 preset_json 为先"), false),
                ("note", Str("本次修改的说明（会记录在返回值里）"), false))),

        // ── 1 档：环境控制 —— 服务端执行 ──
        new("get_queue_summary",
            "读取编码队列：任务列表、状态、进度、可选命令行与预设快照。目标用 id/ids 或 index/indexes 指定，或 target=\"all\"；index 从 0 开始，顺序与返回值一致。",
            LevelEnvironment, "server", false, QueueTargetProps(
                ("detail", Bool("返回单任务时是否带完整字段，默认 false", true), false),
                ("include_commands", Bool("是否带 ffmpeg 命令行，默认 false", true), false),
                ("include_preset_json", Bool("是否带每个任务的预设 JSON（很大），默认 false", true), false),
                ("include_performance", Bool("是否附带当前性能快照，默认 false", true), false),
                ("offset", Int("从第几个任务开始，默认 0", 0, 100000), false),
                ("limit", Int("最多返回多少个任务，默认 20，最大 100", 1, 100), false))),

        new("get_queue_task_logs",
            "读取任务日志。目标用 id/ids 或 index/indexes 指定，或 target=\"all\"。mode=all/latest_non_progress/errors/current_stage，log_limit 限制每次返回的日志条数（从最新往前取）。",
            LevelEnvironment, "server", false, QueueTargetProps(
                ("mode", Str("日志筛选：all（全部）/latest_non_progress（最新输出不含进度）/errors（仅错误）/current_stage（当前阶段）；也接受 modes 数组"), false),
                ("modes", StrArr("多个筛选模式"), false),
                ("log_limit", Int("最多返回多少条日志，默认 20，最大 200", 1, 200), false))),

        new("control_queue_tasks",
            "控制编码队列任务。action=start（开始）/pause（暂停）/resume（恢复）/stop（停止）/reset（重置）/remove（从队列移除）。目标用 id/ids 或 index/indexes 指定，或 target=\"all\"。不可用的动作会返回原因。",
            LevelEnvironment, "server", true, QueueTargetProps(
                ("action", Str("start / pause / resume / stop / reset / remove"), true))),

        new("patch_queue_task_presets",
            "只改队列里**未处理**任务的预设快照，不影响参数面板。changes 的键是预设数据_v6 属性名；也可以用 preset_json 整包替换。",
            LevelEnvironment, "server", true, QueueTargetProps(
                ("changes", Obj("要覆盖的字段：{预设数据_v6 属性名: 新值}"), false),
                ("preset_json", Str("整包替换用的完整预设 JSON 字符串（会先套用 changes 再写回）"), false))),

        new("list_parameter_presets", "列出可用预设的名称。source=user（用户自定义）/community（从社区下载）/builtin（开发者内置）。默认三种都列。",
            LevelEnvironment, "server", false, Props(
                ("source", Str("user / community / builtin，省略则全部列出"), false))),

        new("read_parameter_preset",
            "读取一个预设：备注、关键参数、按该预设生成的命令行预览、以及完整 preset_json（供后续 save/apply 使用）。",
            LevelEnvironment, "server", false, Props(
                ("source", Str("user / community / builtin"), true),
                ("name", Str("预设名（不含 .json 后缀）"), true))),

        new("save_parameter_preset",
            "把一份预设 JSON 保存为预设文件。source 只接受 user / community（builtin 只读）。同名会覆盖。",
            LevelEnvironment, "server", true, Props(
                ("source", Str("user / community"), true),
                ("name", Str("预设名（不含 .json 后缀）"), true),
                ("preset_json", Str("要保存的完整预设 JSON 字符串"), true),
                ("note", Str("预设备注（写入 预设备注 字段）"), false))),

        new("get_system_hardware",
            "读取本机与软件环境：ffmpeg 版本与编码器清单、硬件编码器可用性探测结果、当前性能快照、数据目录与媒体根目录。",
            LevelEnvironment, "server", false, Props()),

        new("probe_media_file", "用 ffprobe 探测一个媒体文件，返回容器/时长/流清单（视频分辨率帧率、音频声道采样率、字幕语言）的摘要。",
            LevelEnvironment, "server", false, Props(
                ("path", Str("媒体文件路径（容器内视角，例如 /media/xxx.mkv）"), true))),

        new("browse_media_directory",
            "列出媒体库目录下的文件与子目录（限定在媒体根目录内，不能越界到系统目录）。用于找用户说的那个文件。",
            LevelEnvironment, "server", false, Props(
                ("path", Str("目录路径，省略则从媒体根目录开始"), false))),

        // ── 1 档：环境控制 —— 浏览器执行 ──
        new("get_prepare_files", "读取「准备文件」页当前待处理的文件路径列表。",
            LevelEnvironment, "browser", false, Props()),

        new("set_prepare_files", "修改「准备文件」页的待处理列表：mode=append（追加）/replace（替换）/clear（清空）。",
            LevelEnvironment, "browser", true, Props(
                ("paths", StrArr("要加入的文件路径列表（clear 时可省略）"), false),
                ("mode", Str("append / replace / clear，默认 append"), false))),

        new("submit_prepare_files_to_queue",
            "把「准备文件」页的待处理文件按**当前参数面板**加入编码队列（等于用户点「一键入队」）。入队前会做与界面一致的检查，不通过会返回原因。",
            LevelEnvironment, "browser", true, Props()),

        new("apply_parameter_preset",
            "把一个预设加载到参数面板（等于用户点「应用预设」），会覆盖当前编辑中的参数。",
            LevelEnvironment, "browser", true, Props(
                ("source", Str("user / community / builtin"), true),
                ("name", Str("预设名（不含 .json 后缀）"), true))),
    ];

    /// <summary>按权限级别过滤后的目录（给前端渲染与组装 tools 用）。</summary>
    public JsonArray Catalog(int permissionLevel)
    {
        var result = new JsonArray();
        foreach (var tool in Definitions.Where(tool => tool.Level <= permissionLevel))
        {
            result.Add(new JsonObject
            {
                ["name"] = tool.Name,
                ["description"] = tool.Description,
                ["parameters"] = tool.Parameters.DeepClone(),
                ["scope"] = tool.Scope,
                ["level"] = tool.Level,
                ["levelName"] = LevelName(tool.Level),
                ["write"] = tool.Write,
            });
        }
        return result;
    }

    /// <summary>取某工具在当前级别的定义；不在目录里（含未知工具、超出级别）返回 null。</summary>
    public JsonObject? Definition(string name, int permissionLevel)
    {
        var tool = Definitions.FirstOrDefault(item => item.Name == name);
        return tool is null || tool.Level > permissionLevel ? null : ToJson(tool);
    }

    private static JsonObject ToJson(ToolDef tool) => new()
    {
        ["name"] = tool.Name,
        ["description"] = tool.Description,
        ["parameters"] = tool.Parameters.DeepClone(),
        ["scope"] = tool.Scope,
        ["level"] = tool.Level,
        ["write"] = tool.Write,
    };

    #endregion

    #region 执行

    public sealed record ExecutionResult(bool Ok, string Result, int StatusCode = 200);

    /// <summary>执行一个 server 范围工具；权限不足返回 403，工具内部错误返回 {ok:false} 的可读文本（不打断循环）。</summary>
    public ExecutionResult Execute(string name, JsonElement args, int permissionLevel)
    {
        var tool = Definitions.FirstOrDefault(item => item.Name == name);
        if (tool is null) return new ExecutionResult(false, $"未知工具：{name}", 404);
        if (tool.Level > permissionLevel)
            return new ExecutionResult(false, $"权限不足：{name} 需要「{LevelName(tool.Level)}」级别，当前是「{LevelName(permissionLevel)}」", 403);
        if (tool.Scope != "server")
            return new ExecutionResult(false, $"{name} 需要浏览器端执行（它操作的是你界面上的状态），服务端无法代劳", 400);

        try
        {
            var text = name switch
            {
                "get_queue_summary" => GetQueueSummary(args),
                "get_queue_task_logs" => GetQueueTaskLogs(args),
                "control_queue_tasks" => ControlQueueTasks(args),
                "patch_queue_task_presets" => PatchQueueTaskPresets(args),
                "list_parameter_presets" => ListParameterPresets(args),
                "read_parameter_preset" => ReadParameterPreset(args),
                "save_parameter_preset" => SaveParameterPreset(args),
                "get_system_hardware" => GetSystemHardware(),
                "probe_media_file" => ProbeMediaFile(args),
                "browse_media_directory" => BrowseMediaDirectory(args),
                _ => throw new InvalidOperationException($"工具 {name} 未实现"),
            };
            return new ExecutionResult(true, Limit(text, ResultLimit));
        }
        catch (ToolFailure ex)
        {
            return new ExecutionResult(false, ErrorText(ex.Message));
        }
        catch (Exception ex)
        {
            // 工具异常变成可读文本回灌给模型，不打断循环（对齐上游 Agent本地工具_v6.vb:401）
            return new ExecutionResult(false, ErrorText("工具执行失败：" + ex.Message));
        }
    }

    #endregion

    #region 队列工具

    private string GetQueueSummary(JsonElement args)
    {
        var (hits, error) = ResolveTasks(args, out var queueCount);
        if (error is not null) return Error(error);

        var detail = Flag(args, "detail");
        var includeCommands = Flag(args, "include_commands");
        var includePreset = Flag(args, "include_preset_json");
        var limit = Math.Clamp(Number(args, "limit") ?? 20, 1, 100);
        var offset = Math.Max(Number(args, "offset") ?? 0, 0);
        var page = hits.Skip(offset).Take(limit).ToList();

        var tasks = new JsonArray();
        foreach (var task in page)
        {
            var item = new JsonObject
            {
                ["ID"] = task.ID,
                ["任务名称"] = task.任务名称,
                ["输入文件"] = task.输入文件,
                ["输出文件"] = task.输出文件,
                ["状态"] = task.状态.ToString(),
                ["预设名称"] = task.预设名称,
                ["百分比"] = task.进度.百分比,
                ["进度文本"] = task.进度.进度文本,
                ["效率文本"] = task.进度.效率文本,
                ["输出大小文本"] = task.进度.输出大小文本,
                ["时间文本"] = task.进度.时间文本,
                ["预设编码器"] = task.预设编码器,
                ["实际编码器"] = task.实际编码器,
                ["可移除"] = task.可移除,
                ["可重置"] = task.可重置,
                ["可开始"] = task.状态 == 编码任务状态_v6.未处理,
            };
            if (includeCommands)
            {
                var command = detail ? BuildCommandLine(task) : "";
                item["命令行"] = command;
            }
            if (detail)
            {
                item["实时输出"] = task.实时输出;
                item["最新底部日志文本"] = task.最新底部日志文本;
                item["错误列表"] = new JsonArray(task.错误列表.Select(x => (JsonNode)JsonValue.Create(x)).ToArray());
                item["步骤"] = new JsonArray(task.步骤.Select(step => (JsonNode)new JsonObject
                {
                    ["显示名称"] = step.显示名称,
                    ["状态"] = step.状态.ToString(),
                    ["实际执行参数"] = step.实际执行参数,
                }).ToArray());
            }
            if (includePreset && task.预设数据 is not null)
                item["预设数据"] = JsonNode.Parse(JsonSerializer.Serialize(task.预设数据, JsonOptions.Compact));
            tasks.Add(item);
        }

        var payload = new JsonObject
        {
            ["队列总数"] = queueCount,
            ["命中"] = hits.Count,
            ["offset"] = offset,
            ["返回"] = page.Count,
            ["任务"] = tasks,
        };
        if (Flag(args, "include_performance")) payload["性能"] = JsonNode.Parse(JsonSerializer.Serialize(_perfSnapshot, JsonOptions.Compact));
        return payload.ToJsonString(JsonOptions.Compact);
    }

    private static string GetQueueTaskLogs(JsonElement args)
    {
        var (hits, error) = ResolveTasks(args, out _);
        if (error is not null) return Error(error);

        var modes = ReadStringList(args, "modes");
        if (ReadString(args, "mode") is { Length: > 0 } single) modes.Insert(0, single);
        if (modes.Count == 0) modes.Add("all");
        var logLimit = Math.Clamp(Number(args, "log_limit") ?? 20, 1, 200);

        var tasks = new JsonArray();
        foreach (var task in hits)
        {
            var perMode = new JsonObject();
            foreach (var mode in modes)
            {
                var display = ParseLogMode(mode, out var modeError);
                if (modeError is not null) { perMode[mode] = modeError; continue; }
                var entries = task.获取日志快照(display);
                var tail = entries.Count > logLimit ? entries.Skip(entries.Count - logLimit).ToList() : entries;
                perMode[mode] = new JsonObject
                {
                    ["条目总数"] = entries.Count,
                    ["返回"] = tail.Count,
                    ["日志"] = new JsonArray(tail.Select(entry => (JsonNode)new JsonObject
                    {
                        ["序号"] = entry.序号,
                        ["时间"] = entry.时间.ToString("HH:mm:ss"),
                        ["阶段名"] = entry.阶段名,
                        ["类别"] = entry.类别.ToString(),
                        ["文本"] = Trim(entry.文本, 600),
                    }).ToArray()),
                };
            }
            tasks.Add(new JsonObject
            {
                ["ID"] = task.ID,
                ["任务名称"] = task.任务名称,
                ["状态"] = task.状态.ToString(),
                ["最新底部日志文本"] = task.最新底部日志文本,
                ["最新底部日志是否错误"] = task.最新底部日志是否错误,
                ["错误列表"] = new JsonArray(task.错误列表.Select(x => (JsonNode)JsonValue.Create(x)).ToArray()),
                ["日志"] = perMode,
            });
        }
        return new JsonObject { ["任务"] = tasks }.ToJsonString(JsonOptions.Compact);
    }

    private static string ControlQueueTasks(JsonElement args)
    {
        var action = (ReadString(args, "action") ?? "").Trim().ToLowerInvariant();
        if (action is not ("start" or "pause" or "resume" or "stop" or "reset" or "remove"))
            return Error("action 必须是 start / pause / resume / stop / reset / remove");

        var (hits, error) = ResolveTasks(args, out _);
        if (error is not null) return Error(error);
        if (hits.Count == 0) return Error("没有命中任何任务");

        // 先做可用性检查，避免静默失败（对齐上游 Agent本地工具_队列_v6.vb:446 的动作可用性判定）
        var blocked = new List<string>();
        foreach (var task in hits)
        {
            var reason = action switch
            {
                "start" when task.状态 != 编码任务状态_v6.未处理 => $"「{task.任务名称}」不是未处理状态（{task.状态}）",
                "remove" when !task.可移除 => $"「{task.任务名称}」当前不可移除（{task.状态}）",
                "reset" when !task.可重置 => $"「{task.任务名称}」当前不可重置（{task.状态}）",
                _ => "",
            };
            if (reason.Length > 0) blocked.Add(reason);
        }
        if (blocked.Count > 0) return Error($"这些任务不能执行 {action}：" + string.Join("；", blocked));

        var ids = hits.Select(task => task.ID).ToList();
        switch (action)
        {
            case "start": 编码队列_v6.开始任务(ids); break;
            case "pause": 编码队列_v6.暂停任务(ids); break;
            case "resume": 编码队列_v6.恢复任务(ids); break;
            case "stop": 编码队列_v6.停止任务(ids); break;
            case "reset": 编码队列_v6.重置任务(ids); break;
            case "remove": 编码队列_v6.移除任务(ids); break;
        }

        var after = ids
            .Select(id => 编码队列_v6.根据ID获取任务(id))
            .Where(task => task is not null)
            .Select(task => (JsonNode)new JsonObject { ["ID"] = task!.ID, ["任务名称"] = task.任务名称, ["状态"] = task.状态.ToString() })
            .ToArray();
        return new JsonObject
        {
            ["ok"] = true,
            ["action"] = action,
            ["影响任务数"] = ids.Count,
            ["任务"] = new JsonArray(after),
            ["说明"] = action == "remove" ? "已从队列移除（未删除磁盘文件）" : "已提交，队列状态可能稍后变化",
        }.ToJsonString(JsonOptions.Compact);
    }

    private static string PatchQueueTaskPresets(JsonElement args)
    {
        var (hits, error) = ResolveTasks(args, out _);
        if (error is not null) return Error(error);
        if (hits.Count == 0) return Error("没有命中任何任务");

        var notReady = hits.Where(task => task.状态 != 编码任务状态_v6.未处理).Select(task => $"「{task.任务名称}」（{task.状态}）").ToList();
        if (notReady.Count > 0) return Error("只能改未处理任务的预设：" + string.Join("、", notReady));

        JsonElement changes = default;
        var hasChanges = args.ValueKind == JsonValueKind.Object && args.TryGetProperty("changes", out changes) && changes.ValueKind == JsonValueKind.Object;
        var presetJson = ReadString(args, "preset_json");
        if (!hasChanges && string.IsNullOrWhiteSpace(presetJson))
            return Error("需要提供 changes 或 preset_json");

        预设数据_v6? replacement = null;
        if (!string.IsNullOrWhiteSpace(presetJson))
        {
            try
            {
                replacement = JsonSerializer.Deserialize<预设数据_v6>(presetJson, JsonOptions.Compact);
            }
            catch (JsonException ex)
            {
                return Error("preset_json 不是合法的预设 JSON：" + ex.Message);
            }
            if (replacement is null) return Error("preset_json 解析结果为空");
        }

        var applied = new JsonObject();
        foreach (var task in hits)
        {
            if (task.预设数据 is null) { applied[task.ID] = "该任务没有预设数据（命令行任务），已跳过"; continue; }
            预设数据_v6 preset = replacement is not null
                ? 预设管理_v6.克隆预设数据(replacement)
                : 预设管理_v6.克隆预设数据(task.预设数据);

            var changed = new JsonArray();
            var failed = new JsonArray();
            if (hasChanges)
            {
                foreach (var field in changes.EnumerateObject())
                {
                    if (TrySetPresetProperty(preset, field.Name, field.Value, out var fieldError)) changed.Add(field.Name);
                    else failed.Add($"{field.Name}：{fieldError}");
                }
            }
            预设管理_v6.初始化空集合(preset);
            var result = 编码队列_v6.同步指定未处理预设任务([task.ID], preset);
            applied[task.ID] = new JsonObject
            {
                ["任务名称"] = task.任务名称,
                ["生效字段"] = changed,
                ["失败字段"] = failed,
                ["同步结果"] = JsonNode.Parse(JsonSerializer.Serialize(result, JsonOptions.Compact)),
            };
        }

        return new JsonObject { ["ok"] = true, ["结果"] = applied }.ToJsonString(JsonOptions.Compact);
    }

    #endregion

    #region 预设工具

    private static string ListParameterPresets(JsonElement args)
    {
        var source = (ReadString(args, "source") ?? "").Trim();
        var result = new JsonObject();
        var want = source.Length == 0 ? "all" : source.ToLowerInvariant();

        if (want is "all" or "user" or "community")
        {
            result["用户自定义"] = Directory.Exists(预设管理_v6.获取预设目录("用户自定义"))
                ? new JsonArray(Directory.GetFiles(预设管理_v6.获取预设目录("用户自定义"), "*.json")
                    .Select(path => (JsonNode)JsonValue.Create(Path.GetFileNameWithoutExtension(path) ?? "")).OrderBy(x => x?.GetValue<string>()).ToArray())
                : new JsonArray();
            result["从社区下载"] = Directory.Exists(预设管理_v6.获取预设目录("从社区下载"))
                ? new JsonArray(Directory.GetFiles(预设管理_v6.获取预设目录("从社区下载"), "*.json")
                    .Select(path => (JsonNode)JsonValue.Create(Path.GetFileNameWithoutExtension(path) ?? "")).OrderBy(x => x?.GetValue<string>()).ToArray())
                : new JsonArray();
        }
        if (want is "all" or "builtin")
        {
            result["开发者内置"] = new JsonArray(开发者内置预设_v6.获取全部()
                .Select(item => (JsonNode)JsonValue.Create(item.名称)).ToArray());
        }
        if (want is not ("all" or "user" or "community" or "builtin"))
            return Error("source 只能是 user / community / builtin，或省略");

        return result.ToJsonString(JsonOptions.Compact);
    }

    private static string ReadParameterPreset(JsonElement args)
    {
        var source = ReadString(args, "source");
        var name = ReadString(args, "name");
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(name))
            return Error("需要 source 与 name");
        var (preset, presetError) = LoadPreset(source!, name!);
        if (presetError is not null) return Error(presetError);

        return new JsonObject
        {
            ["source"] = source,
            ["name"] = name,
            ["预设备注"] = preset!.预设备注,
            ["输出容器"] = preset.输出容器,
            ["视频编码器"] = preset.视频参数_编码器_具体编码,
            ["质量控制"] = preset.视频参数_质量控制_值,
            ["音频编码器"] = preset.音频参数_编码器_代号,
            ["命令行预览"] = TryCommandLine(preset),
            ["preset_json"] = JsonNode.Parse(JsonSerializer.Serialize(preset, JsonOptions.Compact)),
        }.ToJsonString(JsonOptions.Compact);
    }

    private static string SaveParameterPreset(JsonElement args)
    {
        var source = (ReadString(args, "source") ?? "").Trim().ToLowerInvariant();
        var name = (ReadString(args, "name") ?? "").Trim();
        var presetJson = ReadString(args, "preset_json");
        if (source is not ("user" or "community")) return Error("source 只能是 user / community（内置预设只读）");
        if (name.Length == 0) return Error("name 不能为空");
        if (string.IsNullOrWhiteSpace(presetJson)) return Error("preset_json 不能为空");

        预设数据_v6? preset;
        try
        {
            preset = JsonSerializer.Deserialize<预设数据_v6>(presetJson!, JsonOptions.Compact);
        }
        catch (JsonException ex)
        {
            return Error("preset_json 不是合法的预设 JSON：" + ex.Message);
        }
        if (preset is null) return Error("preset_json 解析结果为空");

        var note = ReadString(args, "note");
        if (!string.IsNullOrWhiteSpace(note)) preset.预设备注 = note;
        预设管理_v6.初始化空集合(preset);

        var folder = 预设管理_v6.获取预设目录(source == "user" ? "用户自定义" : "从社区下载");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, 预设管理_v6.安全文件名(name) + ".json");
        var overwritten = File.Exists(path);
        预设管理_v6.写入预设文件(path, preset, false);
        return new JsonObject
        {
            ["ok"] = true,
            ["source"] = source,
            ["name"] = name,
            ["path"] = path,
            ["已覆盖同名预设"] = overwritten,
        }.ToJsonString(JsonOptions.Compact);
    }

    #endregion

    #region 环境与媒体

    private string GetSystemHardware()
    {
        var encoders = new JsonArray(编码器可用性_v6.待探测编码器列表.Select(名称 =>
        {
            var probe = 编码器可用性_v6.查询(名称);
            return (JsonNode)new JsonObject
            {
                ["编码器"] = 名称,
                ["已探测"] = probe is not null && probe.已探测,
                ["可用"] = probe is not null && probe.可用,
                ["失败摘录"] = Trim(probe?.失败摘录 ?? "", 200),
            };
        }).ToArray());

        var ffmpegInfo = _mediaProbe.GetFfmpegInfo();
        return new JsonObject
        {
            ["数据目录"] = _config.DataDir,
            ["媒体根目录"] = _config.MediaRoot,
            ["ffmpeg"] = 设置_v6.获取FFmpeg进程文件名(),
            ["ffmpeg 信息摘要"] = Trim(ffmpegInfo, 3000),
            ["硬件编码器"] = encoders,
            ["性能快照"] = JsonNode.Parse(JsonSerializer.Serialize(_perfSnapshot, JsonOptions.Compact)),
            ["说明"] = "HEVC 硬编请优先用 hevc_vaapi（本机 hevc_qsv 为平台级故障）；h264_qsv 需要 low_power=1（核心会自动注入）",
        }.ToJsonString(JsonOptions.Compact);
    }

    private string ProbeMediaFile(JsonElement args)
    {
        var path = ReadString(args, "path");
        if (string.IsNullOrWhiteSpace(path)) return Error("path 不能为空");
        var (ok, result) = _mediaProbe.Probe(path!);
        if (!ok) return Error(result);

        using var document = JsonDocument.Parse(result);
        var root = document.RootElement;
        var streams = new JsonArray();
        if (root.TryGetProperty("streams", out var streamArray) && streamArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var stream in streamArray.EnumerateArray())
            {
                var item = new JsonObject
                {
                    ["index"] = Int(stream, "index"),
                    ["type"] = Text(stream, "codec_type"),
                    ["codec"] = Text(stream, "codec_name"),
                    ["profile"] = Text(stream, "profile"),
                    ["language"] = stream.TryGetProperty("tags", out var tags) ? Text(tags, "language") : null,
                    ["title"] = stream.TryGetProperty("tags", out var tags2) ? Text(tags2, "title") : null,
                };
                if (Int(stream, "width") is { } width && Int(stream, "height") is { } height) item["分辨率"] = $"{width}x{height}";
                if (Text(stream, "r_frame_rate") is { Length: > 0 } rate) item["帧率"] = rate;
                if (Int(stream, "channels") is { } channels) item["声道数"] = channels;
                if (Text(stream, "sample_rate") is { Length: > 0 } sampleRate) item["采样率"] = sampleRate;
                item["像素格式"] = Text(stream, "pix_fmt");
                item["色彩空间"] = Text(stream, "color_space");
                streams.Add(item);
            }
        }
        var format = new JsonObject();
        if (root.TryGetProperty("format", out var formatElement))
        {
            format["容器"] = Text(formatElement, "format_name");
            format["时长秒"] = Text(formatElement, "duration");
            format["总码率"] = Text(formatElement, "bit_rate");
            format["文件大小字节"] = Text(formatElement, "size");
        }
        return new JsonObject
        {
            ["path"] = path,
            ["format"] = format,
            ["流数量"] = streams.Count,
            ["streams"] = streams,
        }.ToJsonString(JsonOptions.Compact);
    }

    private string BrowseMediaDirectory(JsonElement args)
    {
        var path = ReadString(args, "path") ?? "";
        var browsed = _mediaProbe.Browse(path);
        var json = JsonSerializer.SerializeToNode(browsed, JsonOptions.Compact)!.AsObject();
        if (json["entries"] is JsonArray entries && entries.Count > 200)
        {
            json["已截断"] = entries.Count - 200;
            while (entries.Count > 200) entries.RemoveAt(entries.Count - 1);
        }
        return json.ToJsonString(JsonOptions.Compact);
    }

    #endregion

    #region 公共辅助

    private JsonObject _perfSnapshot => JsonSerializer.SerializeToNode(_perf.Snapshot(), JsonOptions.Compact)!.AsObject();

    private static (预设数据_v6? Preset, string? Error) LoadPreset(string source, string name)
    {
        var normalized = source.Trim().ToLowerInvariant();
        if (normalized == "builtin")
        {
            var builtin = 开发者内置预设_v6.获取全部().FirstOrDefault(item => item.名称 == name);
            return builtin is null ? (null, $"内置预设里没有「{name}」") : (builtin.数据, null);
        }
        if (normalized is not ("user" or "community")) return (null, "source 只能是 user / community / builtin");
        var folder = 预设管理_v6.获取预设目录(normalized == "user" ? "用户自定义" : "从社区下载");
        var path = Path.Combine(folder, 预设管理_v6.安全文件名(name) + ".json");
        if (!File.Exists(path)) return (null, $"预设不存在：{name}");
        return (预设管理_v6.读取预设文件(path), null);
    }

    private static string TryCommandLine(预设数据_v6 preset)
    {
        try { return 预设管理_v6.生成命令行展示文本(preset, "<输入文件>", "<输出文件>"); }
        catch (Exception ex) { return "（命令行生成失败：" + ex.Message + "）"; }
    }

    private static string BuildCommandLine(编码任务_v6 task)
    {
        if (!string.IsNullOrWhiteSpace(task.命令行)) return task.命令行;
        if (task.预设数据 is null) return "";
        try { return 预设管理_v6.生成命令行展示文本(task.预设数据, task.输入文件, task.输出文件); }
        catch { return ""; }
    }

    private static (List<编码任务_v6> Hits, string? Error) ResolveTasks(JsonElement args, out int queueCount)
    {
        var queue = 编码队列_v6.获取队列快照().ToList();
        queueCount = queue.Count;

        var target = (ReadString(args, "target") ?? "").Trim();
        if (target.Equals("all", StringComparison.OrdinalIgnoreCase)) return (queue, null);

        var ids = ReadStringList(args, "ids");
        if (ReadString(args, "id") is { Length: > 0 } single) ids.Insert(0, single);
        var indexes = ReadIntList(args, "indexes");
        if (Number(args, "index") is { } one) indexes.Insert(0, one);

        if (ids.Count == 0 && indexes.Count == 0)
            return (new List<编码任务_v6>(), "需要指定目标：id / ids / index / indexes，或 target=\"all\"");

        var hits = new List<编码任务_v6>();
        foreach (var id in ids)
        {
            var task = queue.FirstOrDefault(item => item.ID == id) ?? 编码队列_v6.根据ID获取任务(id);
            if (task is null) return (new List<编码任务_v6>(), $"找不到任务 ID：{id}");
            hits.Add(task);
        }
        foreach (var index in indexes)
        {
            if (index < 0 || index >= queue.Count)
                return (new List<编码任务_v6>(), $"index 越界：{index}（当前队列 {queue.Count} 项）");
            hits.Add(queue[index]);
        }
        return (hits.Distinct().ToList(), null);
    }

    private static 编码任务日志显示模式_v6 ParseLogMode(string mode, out string? error)
    {
        error = null;
        switch (mode.Trim().ToLowerInvariant())
        {
            case "all": return 编码任务日志显示模式_v6.全部输出;
            case "latest_non_progress": return 编码任务日志显示模式_v6.最新输出不含进度;
            case "errors": return 编码任务日志显示模式_v6.仅错误信息;
            case "current_stage": return 编码任务日志显示模式_v6.当前阶段输出;
            default:
                error = $"未知 mode：{mode}（可用 all/latest_non_progress/errors/current_stage）";
                return 编码任务日志显示模式_v6.全部输出;
        }
    }

    /// <summary>
    /// 按 预设数据_v6 的属性名写值。枚举**先校验名字再写**——容错枚举会把写错的枚举名静默回退成默认值（踩坑 27），
    /// 对模型写参数这种场景太危险；这里直接报错并列出合法取值。
    /// </summary>
    private static bool TrySetPresetProperty(预设数据_v6 preset, string name, JsonElement value, out string error)
    {
        var property = typeof(预设数据_v6).GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
        if (property is null || !property.CanWrite)
        {
            error = "没有这个字段（字段名必须是预设数据_v6 的属性名，可用 get_parameter_field_info 精确查询）";
            return false;
        }

        var targetType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        try
        {
            object? converted;
            if (value.ValueKind == JsonValueKind.Null)
            {
                if (property.PropertyType.IsValueType && Nullable.GetUnderlyingType(property.PropertyType) is null)
                {
                    error = "该字段不接受 null";
                    return false;
                }
                converted = null;
            }
            else if (targetType.IsEnum)
            {
                if (value.ValueKind != JsonValueKind.String)
                {
                    error = $"该字段是枚举，需要字符串；合法取值：{string.Join("/", Enum.GetNames(targetType))}";
                    return false;
                }
                var raw = value.GetString() ?? "";
                var hit = Enum.GetNames(targetType).FirstOrDefault(item => string.Equals(item, raw, StringComparison.OrdinalIgnoreCase));
                if (hit is null)
                {
                    error = $"未知枚举值「{raw}」；合法取值：{string.Join("/", Enum.GetNames(targetType))}";
                    return false;
                }
                converted = Enum.Parse(targetType, hit);
            }
            else if (targetType == typeof(string))
            {
                converted = value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText();
            }
            else if (targetType == typeof(bool))
            {
                converted = value.GetBoolean();
            }
            else if (targetType == typeof(int))
            {
                converted = value.GetInt32();
            }
            else if (targetType == typeof(long))
            {
                converted = value.GetInt64();
            }
            else if (targetType == typeof(double) || targetType == typeof(decimal))
            {
                converted = Convert.ChangeType(value.GetDouble(), targetType);
            }
            else
            {
                converted = value.Deserialize(property.PropertyType, JsonOptions.Compact);
            }

            property.SetValue(preset, converted);
            error = "";
            return true;
        }
        catch (Exception ex)
        {
            error = "值不合法：" + ex.Message;
            return false;
        }
    }

    private static string ErrorText(string message) => new JsonObject { ["ok"] = false, ["error"] = message }.ToJsonString(JsonOptions.Compact);

    /// <summary>
    /// 工具级的可读失败：在工具方法里直接 `return Error("…")` 就会抛出去，
    /// 由分发层包成 {ok:false,error} 回灌给模型（不打断循环）。
    /// </summary>
    private sealed class ToolFailure(string message) : Exception(message);

    private static string Error(string message) => throw new ToolFailure(message);

    private static string Limit(string text, int max) => text.Length <= max ? text : text[..max] + $"…（已截断，原文 {text.Length} 字符）";

    private static string Trim(string text, int max) => string.IsNullOrEmpty(text) || text.Length <= max ? text : text[..max] + "…";

    private static string? ReadString(JsonElement args, string name) =>
        args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool Flag(JsonElement args, string name) =>
        args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static int? Number(JsonElement args, string name) =>
        args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : null;

    private static List<string> ReadStringList(JsonElement args, string name)
    {
        var result = new List<string>();
        if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty(name, out var value)) return result;
        if (value.ValueKind == JsonValueKind.String)
        {
            var single = value.GetString();
            if (!string.IsNullOrWhiteSpace(single)) result.Add(single.Trim());
            return result;
        }
        if (value.ValueKind != JsonValueKind.Array) return result;
        foreach (var item in value.EnumerateArray())
            if (item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
                result.Add(item.GetString()!.Trim());
        return result;
    }

    private static List<int> ReadIntList(JsonElement args, string name)
    {
        var result = new List<int>();
        if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty(name, out var value)) return result;
        if (value.ValueKind == JsonValueKind.Number) { result.Add(value.GetInt32()); return result; }
        if (value.ValueKind != JsonValueKind.Array) return result;
        foreach (var item in value.EnumerateArray())
            if (item.ValueKind == JsonValueKind.Number) result.Add(item.GetInt32());
        return result;
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int? Int(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : null;

    #endregion

    #region 工具参数 schema 小工具

    private static JsonObject Props(params (string Name, JsonObject Schema, bool Required)[] items)
    {
        var properties = new JsonObject();
        var required = new JsonArray();
        foreach (var (name, schema, isRequired) in items)
        {
            properties[name] = schema;
            if (isRequired) required.Add(name);
        }
        return new JsonObject { ["type"] = "object", ["properties"] = properties, ["required"] = required };
    }

    /// <summary>队列类工具共用的目标选择参数。</summary>
    private static JsonObject QueueTargetProps(params (string Name, JsonObject Schema, bool Required)[] extra)
    {
        var common = new (string, JsonObject, bool)[]
        {
            ("id", Str("单个任务 ID"), false),
            ("ids", StrArr("多个任务 ID"), false),
            ("index", Int("队列位置，从 0 开始", 0, 100000), false),
            ("indexes", new JsonObject
            {
                ["type"] = "array",
                ["description"] = "多个队列位置（从 0 开始）",
                ["items"] = new JsonObject { ["type"] = "integer" },
            }, false),
            ("target", Str("填 all 表示整个队列"), false),
        };
        return Props([.. common, .. extra]);
    }

    private static JsonObject Str(string description) => new() { ["type"] = "string", ["description"] = description };

    private static JsonObject Bool(string description, bool _) => new() { ["type"] = "boolean", ["description"] = description };

    private static JsonObject Int(string description, int min, int max) => new()
    {
        ["type"] = "integer",
        ["description"] = description,
        ["minimum"] = min,
        ["maximum"] = max,
    };

    private static JsonObject StrArr(string description) => new()
    {
        ["type"] = "array",
        ["description"] = description,
        ["items"] = new JsonObject { ["type"] = "string" },
    };

    private static JsonObject Obj(string description) => new() { ["type"] = "object", ["description"] = description };

    #endregion
}
