namespace linux3fui.Server;

/// <summary>
/// Agent 技能资料库：与上游 `功能/Agent 智能体/Agent技能资料库_v6.vb` 对应的一组内嵌 markdown，
/// 由 `list_agent_skills` / `read_agent_skill_reference` 两个工具读取。
///
/// ⚠ 内容只写**本项目（Linux 网页版）的真实行为**，不是上游 Windows 版的结论：
/// 例如 HEVC 硬编用 hevc_vaapi 而不是 hevc_qsv、滤镜里没有 fruc_vulkan、参数面板字段是扁平中文属性名。
/// 新增参考文档只需往 References 里加一条（文件名用 kebab-case，与工具入参归一化规则一致）。
/// </summary>
public static class AgentSkills
{
    public sealed record Reference(string Name, string Title, string Content);

    public const string SkillName = "ffmpegfreeui";

    public const string SkillDescription =
        "linux-3fui（FFmpegFreeUI 的 Linux/网页版）的使用与调参资料：参数面板字段语义、命令行生成与自检、" +
        "滤镜与流控制、编码队列与任务生命周期、硬件编码器选型与已知坑。不确定 ffmpeg 用法或本项目行为时先查这里。";

    public static readonly IReadOnlyList<Reference> References =
    [
        new("parameter-panel", "参数面板与预设（怎么改才生效）",
            """
            # 参数面板与预设

            ## 数据结构
            - 预设就是一份**扁平的中文属性名 → 值**的 JSON（核心 VB 类 `预设数据_v6`，约 150 个属性）。
              例：`输出容器`＝`mkv`、`视频参数_编码器_具体编码`＝`hevc_vaapi`、`视频参数_质量控制_值`＝`24`。
            - 嵌套的少数例外用点号路径，例如 `视频参数_超分_直接面板.目标宽度`。
            - `滤镜排序系统` 是**列表**：每项形如 `{ 启用: bool, 滤镜: string, 自定义滤镜内容: string }`，改它要传完整列表。

            ## 改参数的正确姿势
            1. 先用 `get_parameter_field_info`（或 `get_parameter_panel_state` 的概览）确认**字段名与当前值**，不要猜字段名。
            2. 用 `apply_parameter_panel_patch` 的 `changes` 传 `{ 属性名: 新值 }`；返回值里的「生效字段」才是真正落地的
               （部分字段受门控/联动影响，可能与预期不同）。
            3. 改完**必须自检**：`get_parameter_panel_state(include_command_preview=true)` 看生成出来的 ffmpeg 命令行。
            4. 要留存就用 `save_parameter_preset`（source=user/community；内置预设只读）。

            ## 常见坑
            - **枚举字段写错值不会报错**：本项目对工具调用做了枚举名校验（会列合法取值），但从预设文件/命令行进来的错值会被
              核心的容错枚举**静默回退成默认值**。所以改完要看命令行的实际效果，不要只看"写成功了"。
            - `输出容器`（后缀）为**空**时生成不出输出文件名，入队会失败——先设容器。
            - 图片类编码器（AVIF/JXL 等）会**强制跳过音频与字幕**映射，这时不要指望 `-map 0:a`。
            - 「全保留」是默认策略：`-map 0:v:0? -map 0:a? -c:a copy -map 0:s? -c:s copy -map_metadata 0 -map_chapters 0`
              ——所以默认会带上全部音轨/字幕/元数据/章节/附件。要只留一条音轨，改
              `流控制_将音频参数应用于指定流`＝`["0:a:0"]` 并关掉 `流控制_启用保留其他音频流`。
            - MP4/WebM 等受限容器装不下"全保留"的项（附件/字幕/章节），工具入队前会拦下来并给原因；改用 MKV 或逐项关掉开关。
            """),
        new("command-generation", "命令行生成与自检",
            """
            # 命令行生成与自检

            ## 怎么拿命令行
            - `get_parameter_panel_state(include_command_preview=true)`：按**当前面板**生成（输入/输出用占位符 `<输入文件>`/`<输出文件>`）。
            - `read_parameter_preset`：读某个预设文件并附带它的命令行预览。
            - 队列里的任务：`get_queue_summary(include_commands=true)`（预设任务会现场生成，命令行为空的都是预设任务）。

            ## 生成的规则要点
            - 命令行由**核心 VB**（`预设管理_v6.生成命令行展示文本`）统一生成，与真正执行时用的是同一套逻辑；
              前端/工具都只是调用它，不要去手工拼 ffmpeg 参数。
            - 需要 ffprobe 前置分析的参数（掐头去尾、HDR 转换的某些分支）在预览里只能看到主命令，实际执行会多一步探测。
            - 二次编码/阶段化命令：`生成阶段化命令行` 会拆成多步（分析 → 编码），日志里能看到每一步的「实际执行参数」。

            ## 自检清单（改完参数照这个走）
            1. 命令行里出现你改的那个参数了吗？值对吗？
            2. `-map` 是否还带着你不想保留的音轨/字幕？
            3. 硬编参数是否完整（VAAPI 需要 `-vaapi_device` + `format=nv12,hwupload`；QSV 的 h264 需要 `-low_power 1`）？
            4. 输出容器与保留项是否兼容（见「参数面板与预设」的坑）？
            """),
        new("filters-and-streams", "滤镜链与流控制",
            """
            # 滤镜链与流控制

            ## 滤镜排序系统
            - 滤镜是**有序列表**，按顺序串成 `-vf`（视频）／`-af`（音频）链；每项有「启用」开关，只有启用的才进链。
            - 顺序很重要：`yadif` 反交错应在 `scale` 之前，`deband` 通常在 `scale` 之后，`subtitles`/`ass` 放最后。
            - 改整条链要用 `apply_parameter_panel_patch` 传完整的 `滤镜排序系统` 列表（只传一项会覆盖整条链，别这么干）。
            - 滑杆类滤镜（简易调色的亮度/对比度/饱和度/伽马、响度标准化）走的是**独立字段 + 门控开关**：
              必须把对应的启用开关设为 true，核心才会生成对应的 `eq`/`loudnorm`。

            ## 流映射（流控制页）
            - `流控制_将视频参数应用于指定流` / `..._音频参数...` / `..._字幕...` 三个字段决定参数作用在哪些流上，
              取值形如 `0:v:0`、`0:a:1`（`0` 是输入文件序号，冒号后是 `v/a/s` 与类型内序号）。
            - 留空＝作用于全部该类型流。
            - `流控制_启用保留其他音频流`、`..._启用保留其他字幕流` 是"除选中的以外，其余原样 copy"的开关；
              「全保留」默认开着它们。
            - 附件/章节/元数据分别在 `流控制_附件选项`、`流控制_章节选项`、`流控制_元数据选项` 里（默认全保留）。

            ## 本项目滤镜的已知缺口
            - Debian 的 ffmpeg 没有 `fruc_vulkan`（NV FRUC 插帧）——这个滤镜在本项目不可用，别推荐。
            - VMAF 需要 ffmpeg 构建带 libvmaf，属于可选开关，缺了就报 `No such filter`。
            """),
        new("queue-and-tasks", "编码队列与任务生命周期",
            """
            # 编码队列与任务生命周期

            ## 状态
            `未处理(0)` → `正在处理(1)` ↔ `已暂停(2)`；终态是 `已完成(10)` / `已停止(20)` / `错误(99)`。

            ## 能用工具做什么
            - 看：`get_queue_summary`（`target="all"` 或 `id/ids`、`index/indexes`；`detail` 带步骤与错误、`include_commands` 带命令行、
              `include_preset_json` 带预设快照）、`get_queue_task_logs`（`mode=all/latest_non_progress/errors/current_stage`，`log_limit` 从最新往前取）。
            - 控：`control_queue_tasks` 的 `action`：`start`（**只有未处理可开始**）/`pause`/`resume`/`stop`/`reset`/`remove`（**只从队列移除，不删磁盘文件**）。
              不可用的动作会返回具体原因（例如"不是未处理状态"），照着原因处理，不要硬试。
            - 改任务参数：`patch_queue_task_presets`（只改**未处理**任务的预设快照，不影响参数面板）。

            ## 重要行为
            - **默认「入队即自动开始」**：设置里的 `自动开始任务选项 = 0` 表示允许自动启动。给队列入队要意识到可能立刻开始跑真正的编码。
            - **任务失败会留下 0 字节输出文件**，它被当成输入会连锁报错（ffmpeg 退出码 183 / `EBML header parsing failed`）——先删残骸。
            - 任务详情里的**日志是显式投影出来的**（核心里的日志缓存是私有字段），工具拿到的就是同一份。
            - 编码器故障自动切换：启动探测 → 任务启动前检查 → 运行时 stderr 特征兜底；切换后会推送通知，
              质量参数按标度换算（`-global_quality` ↔ `-qp`），并可能丢弃不兼容参数（例如 x265 的 `slower` 不能给 VAAPI）。

            ## 常见 ffmpeg 退出码（实测）
            | 退出码 | 含义 |
            |---|---|
            | 234 | 输出容器为空（`Unable to choose an output format for ''`）|
            | 8 | 缺滤镜，例如 `No such filter: 'fruc_vulkan'` |
            | 171 | hevc_qsv 编码失败（本机平台级故障）|
            | 183 | 把失败留下的 0 字节文件当成了输入 |
            """),
        new("hardware-encoders", "硬件编码器选型与已知坑",
            """
            # 硬件编码器选型与已知坑

            ## 本机（引用项目实测结论，换机器需重新探测）
            - Intel UHD 730：`renderD128`。**HEVC 硬编用 `hevc_vaapi`**，不要用 `hevc_qsv`。
            - AMD RX 6400：`renderD129`，VAAPI 报 `-22`（amdgpu headless 常见），别用。
            - 可用性以 `get_system_hardware` 的「硬件编码器」清单为准（它是真实探测结果，不是 `-encoders` 文本匹配）。

            ## 各家的参数要求
            - **VAAPI**：必须注入 `-vaapi_device /dev/dri/renderD128` 并在滤镜链里 `format=nv12,hwupload`
              （10 位源自动用 `p010le`）——核心已自动注入，但如果你手写 `解码参数_指定硬件的参数名/参数` 覆盖了它，就会失效。
            - **QSV**：Debian 的 ffmpeg 链的是 `libvpl`，需要 `libvpl2 + libmfx-gen1.2`（装 `libmfx1` 没用）。
              `h264_qsv` 必须 `-low_power 1`（核心自动注入）；ICQ（`-global_quality`）不支持，只有 CQP/比特率。
            - **hevc_qsv 在本机不可用**：`Invalid FrameType:0`（MFX 返回空比特流，FFmpeg 源码 qsvenc.c 对应位置可查），
              宿主直装 ffmpeg 同样报错，**属平台级故障，调参无解**，别在这上面花时间。
            - **NVENC**：需要宿主装 NVIDIA Container Toolkit 并在 compose 里开 `runtime: nvidia`；本机无 NVIDIA 卡。
            - **AV1**：`av1_nvenc`（N 卡 40 系）/ `av1_qsv`（Intel 新核显）/ `libsvtav1`（软编）。本机 Intel UHD 730 的 AV1 QSV 未验证。

            ## 质量参数怎么选
            - CPU（x264/x265）：CRF 或 2-pass 比特率；x265 的 `slower` 等预设档**不能**给硬件编码器（会报
              `Error setting option compression_level`）——切换编码器时核心会剔除这类残留值。
            - 硬件：VAAPI/QSV 用 `-qp`（固定 QP）或比特率；N 卡用 `-cq`。质量标度与 CRF 不通用，别直接换算数字。
            - 判断硬编是否真成功：**看日志和流信息，不要只看输出文件存在或大小**——曾出现过只产出音轨的"成功"文件。
            """),
    ];

    /// <summary>reference 名归一化：去 references/ 前缀、去 .md 后缀、下划线转连字符。</summary>
    public static string Normalize(string? name)
    {
        var value = (name ?? "").Trim().ToLowerInvariant();
        if (value.Contains('/')) value = value[(value.LastIndexOf('/') + 1)..];
        if (value.EndsWith(".md")) value = value[..^3];
        return value.Replace('_', '-');
    }

    public static string ListJson() => System.Text.Json.JsonSerializer.Serialize(new
    {
        skills = new[]
        {
            new
            {
                name = SkillName,
                description = SkillDescription,
                references = References.Select(reference => new { name = reference.Name, title = reference.Title }).ToArray(),
            },
        },
        usage = "用 read_agent_skill_reference 读某一篇：它接受 name（可省略 references/ 前缀与 .md 后缀）。",
    }, JsonOptions.Compact);

    public static string? Read(string? name)
    {
        var wanted = Normalize(name);
        if (wanted.Length == 0) return null;
        return References.FirstOrDefault(reference => reference.Name == wanted)?.Content;
    }
}
