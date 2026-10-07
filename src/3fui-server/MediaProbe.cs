using System.Diagnostics;
using System.Text;
using System.Text.Json;
using FFmpegFreeUI;

namespace linux3fui.Server;

/// <summary>媒体探测（ffprobe）、ffmpeg 能力信息与目录浏览。</summary>
public sealed class MediaProbe
{
    private readonly ServerConfig _config;
    private readonly ILogger<MediaProbe> _logger;

    public MediaProbe(ServerConfig config, ILogger<MediaProbe> logger)
    {
        _config = config;
        _logger = logger;
    }

    /// <summary>运行 ffprobe 输出 JSON（-show_streams -show_format -of json）。</summary>
    public (bool ok, string result) Probe(string path)
    {
        var probePath = 设置_v6.获取FFprobe进程文件名();
        if (string.IsNullOrWhiteSpace(probePath)) return (false, "未找到 ffprobe，请安装 ffmpeg 或配置 替代进程文件名");
        return Run(probePath, $"-v error -show_streams -show_format -of json \"{path}\"");
    }

    /// <summary>ffmpeg 能力信息（版本/编码器/滤镜），带缓存。</summary>
    public string GetFfmpegInfo()
    {
        var ffmpeg = 设置_v6.获取FFmpeg进程文件名();
        if (string.IsNullOrWhiteSpace(ffmpeg)) return "{\"error\":\"未找到 ffmpeg\"}";
        var (okEncoders, encoders) = Run(ffmpeg, "-hide_banner -encoders");
        var (okFilters, filters) = Run(ffmpeg, "-hide_banner -filters");
        var (okVersion, version) = Run(ffmpeg, "-hide_banner -version");
        return JsonSerializer.Serialize(new
        {
            version = okVersion ? version : "",
            encoders = okEncoders ? encoders : "",
            filters = okFilters ? filters : "",
        }, JsonOptions.Compact);
    }

    /// <summary>浏览目录（受限：MEDIA_ROOT 未设置时允许任意绝对路径）。</summary>
    public object Browse(string path)
    {
        var root = _config.MediaRoot;
        var requested = string.IsNullOrWhiteSpace(path) ? (root ?? "/") : path;
        string full;
        try
        {
            full = Path.GetFullPath(requested);
        }
        catch
        {
            return new { error = "路径无效" };
        }
        if (root is not null)
        {
            var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
            var requestedFull = full.TrimEnd(Path.DirectorySeparatorChar);
            if (!(requestedFull + Path.DirectorySeparatorChar).StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                return new { error = "路径超出媒体根目录" };
        }
        if (!Directory.Exists(full)) return new { error = "目录不存在" };

        var mediaExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".ts", ".m2ts", ".mts", ".m4v",
            ".webm", ".mpg", ".mpeg", ".3gp", ".rm", ".rmvb", ".vob", ".ogv", ".asf",
            ".mp3", ".flac", ".wav", ".aac", ".m4a", ".ogg", ".opus", ".wma", ".ac3", ".ape",
            ".ass", ".srt", ".ssa", ".3fui", ".json",
        };
        var parent = Directory.GetParent(full)?.FullName;
        var entries = Directory.EnumerateDirectories(full)
            .Select(dir => new
            {
                name = Path.GetFileName(dir),
                path = dir,
                isDirectory = true,
                isMedia = false,
                size = (long?)null,
            })
            .Concat(Directory.EnumerateFiles(full)
                .Select(file => new
                {
                    name = Path.GetFileName(file),
                    path = file,
                    isDirectory = false,
                    isMedia = mediaExtensions.Contains(Path.GetExtension(file)),
                    size = (long?)new FileInfo(file).Length,
                }))
            .OrderByDescending(entry => entry.isDirectory)
            .ThenBy(entry => entry.name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new { path = full, parent, root, entries };
    }

    private (bool ok, string output) Run(string fileName, string arguments)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                },
            };
            var output = new StringBuilder();
            process.OutputDataReceived += (_, e) => { if (e.Data is not null) output.AppendLine(e.Data); };
            process.Start();
            process.BeginOutputReadLine();
            var error = process.StandardError.ReadToEnd();
            if (!process.WaitForExit(60_000))
            {
                try { process.Kill(true); } catch { }
                return (false, "执行超时");
            }
            // 无参 WaitForExit：等异步输出事件全部投递完毕，否则 BeginOutputReadLine 的数据可能未收全（截断 JSON）
            process.WaitForExit();
            var stdout = output.ToString();
            return (process.ExitCode == 0, stdout.Length > 0 ? stdout : error);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "运行 {FileName} 失败", fileName);
            return (false, "执行失败：" + ex.Message);
        }
    }
}
