using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace linux3fui.Server;

/// <summary>Linux 性能监控：后台 2s 采样 /proc + 可选 nvidia-smi，内存环形缓冲保留 1 小时（替代上游的 LibreHardwareMonitor）。</summary>
public sealed class PerfMonitor : IDisposable
{
    private static readonly long ClockTicks = 100; // USER_HZ
    private const int IntervalMs = 2000;
    private const int Capacity = 1800;             // 2s × 1800 = 1 小时
    private const double SectorBytes = 512;        // /proc/diskstats 扇区字节数

    private sealed record CpuCounters(long User, long Nice, long System, long Idle, long Iowait, long Irq, long Softirq, long Steal)
    {
        public long Total => User + Nice + System + Idle + Iowait + Irq + Softirq + Steal;
        public long Busy => Total - Idle - Iowait;
    }

    private sealed record CpuSample(double Usage, double User, double System, double Iowait, double[] PerCore);
    private sealed record MemSample(double TotalMb, double UsedMb, double AvailableMb, double BuffersCacheMb,
                                    double SwapTotalMb, double SwapUsedMb, double UsagePercent);
    // 设备速率表：name -> [读 KB/s, 写 KB/s]（磁盘）/ [收 KB/s, 发 KB/s]（网络）
    private sealed record DiskSample(double ReadKbps, double WriteKbps, Dictionary<string, double[]> Devices);
    private sealed record NetSample(double RxKbps, double TxKbps, Dictionary<string, double[]> Interfaces);
    private sealed record GpuSample(string Name, double Utilization, double MemoryUsedMb, double MemoryTotalMb, double Temperature);
    private sealed record PerfSample(long T, CpuSample Cpu, MemSample Memory, DiskSample Disk, NetSample Network, GpuSample[] Gpu);

    private readonly object _gate = new();
    private readonly Queue<PerfSample> _samples = new();
    private Timer? _timer;
    private CpuCounters? _prevCpuTotal;
    private Dictionary<string, CpuCounters>? _prevCores;
    private Dictionary<string, double[]>? _prevDisks; // name -> [读字节, 写字节]
    private Dictionary<string, double[]>? _prevNets;  // name -> [收字节, 发字节]
    private DateTime _prevStamp;
    private PerfSample? _latest;
    private bool _nvidiaMissing;
    private string? _cpuModel;

    public void Start()
    {
        lock (_gate)
        {
            _timer ??= new Timer(Tick, null, 0, IntervalMs);
        }
    }

    public void Stop() => Dispose();

    public void Dispose()
    {
        lock (_gate)
        {
            _timer?.Dispose();
            _timer = null;
        }
    }

    private void Tick(object? state)
    {
        // 上一次采样未结束（如 nvidia-smi 超时）则跳过本轮，避免重叠
        if (!Monitor.TryEnter(_gate)) return;
        try
        {
            SampleLocked();
        }
        catch (Exception ex)
        {
            // 定时器回调未捕获异常会杀进程，采样失败仅记日志
            Console.Error.WriteLine($"[PerfMonitor] 采样失败: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            Monitor.Exit(_gate);
        }
    }

    private void SampleLocked()
    {
        var now = DateTime.UtcNow;
        var elapsed = Math.Max(0.5, (now - _prevStamp).TotalSeconds);
        _prevStamp = now;

        var sample = new PerfSample(
            T: new DateTimeOffset(now).ToUnixTimeMilliseconds(),
            Cpu: ReadCpu(elapsed),
            Memory: ReadMemory(),
            Disk: ReadDisk(elapsed),
            Network: ReadNetwork(elapsed),
            Gpu: ReadGpus());

        _latest = sample;
        _samples.Enqueue(sample);
        while (_samples.Count > Capacity) _samples.Dequeue();
    }

    /// <summary>当前快照。兼容旧前端字段（cpu/memory/loadavg/processes/gpu），新增 perCore/disk/network。</summary>
    public object Snapshot()
    {
        PerfSample latest;
        lock (_gate)
        {
            if (_latest is null) SampleLocked();
            latest = _latest!;
        }
        var result = new Dictionary<string, object?>
        {
            ["cpu"] = new
            {
                cores = Environment.ProcessorCount,
                usagePercent = latest.Cpu.Usage,
                user = latest.Cpu.User,
                system = latest.Cpu.System,
                iowait = latest.Cpu.Iowait,
                perCore = latest.Cpu.PerCore,
                model = CpuModel(),
            },
            ["memory"] = new
            {
                totalMb = R1(latest.Memory.TotalMb),
                usedMb = R1(latest.Memory.UsedMb),
                availableMb = R1(latest.Memory.AvailableMb),
                usagePercent = latest.Memory.UsagePercent,
                buffersCacheMb = R1(latest.Memory.BuffersCacheMb),
                swapTotalMb = R1(latest.Memory.SwapTotalMb),
                swapUsedMb = R1(latest.Memory.SwapUsedMb),
            },
            ["loadavg"] = ReadLoadAvg(),
            ["disk"] = new
            {
                readKbps = R1(latest.Disk.ReadKbps),
                writeKbps = R1(latest.Disk.WriteKbps),
                devices = latest.Disk.Devices
                    .OrderByDescending(kv => kv.Value[0] + kv.Value[1])
                    .Select(kv => new { name = kv.Key, readKbps = R1(kv.Value[0]), writeKbps = R1(kv.Value[1]) })
                    .ToArray(),
            },
            ["network"] = new
            {
                rxKbps = R1(latest.Network.RxKbps),
                txKbps = R1(latest.Network.TxKbps),
                interfaces = latest.Network.Interfaces
                    .OrderByDescending(kv => kv.Value[0] + kv.Value[1])
                    .Select(kv => new { name = kv.Key, rxKbps = R1(kv.Value[0]), txKbps = R1(kv.Value[1]) })
                    .ToArray(),
            },
            ["processes"] = ReadTopProcesses(),
        };
        if (latest.Gpu.Length > 0)
        {
            result["gpu"] = latest.Gpu.Select(g => new
            {
                name = g.Name,
                utilization = R1(g.Utilization),
                memoryUsedMb = R1(g.MemoryUsedMb),
                memoryTotalMb = R1(g.MemoryTotalMb),
                temperature = R1(g.Temperature),
            }).ToArray();
        }
        return result;
    }

    /// <summary>历史序列：从环形缓冲按步长抽稀（保留最新点），cores=1 时附带每核序列。</summary>
    public object History(int minutes, int maxPoints, bool includeCores)
    {
        List<PerfSample> samples;
        lock (_gate)
        {
            var cutoff = new DateTimeOffset(DateTime.UtcNow).ToUnixTimeMilliseconds() - minutes * 60000L;
            samples = _samples.Where(s => s.T >= cutoff).ToList();
        }
        if (samples.Count == 0)
        {
            return new
            {
                intervalMs = IntervalMs,
                points = 0,
                from = 0,
                to = 0,
                cpu = Array.Empty<double>(),
                cpuUser = Array.Empty<double>(),
                cpuSystem = Array.Empty<double>(),
                memory = Array.Empty<double>(),
                diskRead = Array.Empty<double>(),
                diskWrite = Array.Empty<double>(),
                netRx = Array.Empty<double>(),
                netTx = Array.Empty<double>(),
                gpuUtil = Array.Empty<double[]>(),
                gpuMem = Array.Empty<double[]>(),
                cores = includeCores ? Array.Empty<double[]>() : null,
            };
        }

        var step = Math.Max(1, (int)Math.Ceiling(samples.Count / (double)Math.Max(1, maxPoints)));
        var picked = samples.Where((_, i) => (samples.Count - 1 - i) % step == 0).ToList();
        var gpuCount = picked.Max(s => s.Gpu.Length);

        return new
        {
            intervalMs = IntervalMs,
            points = picked.Count,
            from = picked[0].T,
            to = picked[^1].T,
            cpu = picked.Select(s => s.Cpu.Usage).ToArray(),
            cpuUser = picked.Select(s => s.Cpu.User).ToArray(),
            cpuSystem = picked.Select(s => s.Cpu.System).ToArray(),
            memory = picked.Select(s => s.Memory.UsagePercent).ToArray(),
            diskRead = picked.Select(s => s.Disk.ReadKbps).ToArray(),
            diskWrite = picked.Select(s => s.Disk.WriteKbps).ToArray(),
            netRx = picked.Select(s => s.Network.RxKbps).ToArray(),
            netTx = picked.Select(s => s.Network.TxKbps).ToArray(),
            gpuUtil = Enumerable.Range(0, gpuCount)
                .Select(g => picked.Select(s => s.Gpu.Length > g ? s.Gpu[g].Utilization : 0).ToArray())
                .ToArray(),
            gpuMem = Enumerable.Range(0, gpuCount)
                .Select(g => picked.Select(s => s.Gpu.Length > g ? s.Gpu[g].MemoryUsedMb : 0).ToArray())
                .ToArray(),
            cores = includeCores ? picked.Select(s => s.Cpu.PerCore).ToArray() : null,
        };
    }

    // ── CPU：/proc/stat 差分（总量 + 每核）──
    private CpuSample ReadCpu(double elapsed)
    {
        try
        {
            var cores = new Dictionary<string, CpuCounters>(StringComparer.Ordinal);
            CpuCounters? total = null;
            foreach (var line in File.ReadLines("/proc/stat"))
            {
                if (!line.StartsWith("cpu", StringComparison.Ordinal)) break;
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var counters = ParseCpu(parts);
                if (parts[0].Length == 3) total = counters;
                else cores[parts[0]] = counters;
            }
            if (total is null) return new CpuSample(0, 0, 0, 0, Array.Empty<double>());

            double usage = 0, user = 0, system = 0, iowait = 0;
            double[] perCore;
            if (_prevCpuTotal is not null)
            {
                var delta = Math.Max(1, total.Total - _prevCpuTotal.Total);
                usage = ClampPct((total.Busy - _prevCpuTotal.Busy) * 100.0 / delta);
                user = ClampPct((total.User + total.Nice - _prevCpuTotal.User - _prevCpuTotal.Nice) * 100.0 / delta);
                system = ClampPct((total.System + total.Irq + total.Softirq - _prevCpuTotal.System - _prevCpuTotal.Irq - _prevCpuTotal.Softirq) * 100.0 / delta);
                iowait = ClampPct((total.Iowait - _prevCpuTotal.Iowait) * 100.0 / delta);
            }
            if (_prevCores is not null)
            {
                // 每核值必须 R1：前端按原样显示（CPU i  当前值%），不取整会出现 2.03020202020…
                perCore = cores.Select(kv =>
                {
                    var prev = _prevCores!.GetValueOrDefault(kv.Key);
                    if (prev is null) return 0.0;
                    var delta = Math.Max(1, kv.Value.Total - prev.Total);
                    return R1(ClampPct((kv.Value.Busy - prev.Busy) * 100.0 / delta));
                }).ToArray();
            }
            else
            {
                perCore = cores.Select(_ => 0.0).ToArray();
            }
            _prevCpuTotal = total;
            _prevCores = cores;
            return new CpuSample(R1(usage), R1(user), R1(system), R1(iowait), perCore);
        }
        catch
        {
            return new CpuSample(0, 0, 0, 0, Array.Empty<double>());
        }
    }

    private static CpuCounters ParseCpu(string[] parts)
    {
        long V(int i) => i < parts.Length ? ParseLong(parts[i]) : 0;
        return new CpuCounters(V(1), V(2), V(3), V(4), V(5), V(6), V(7), V(8));
    }

    // ── 内存：/proc/meminfo ──
    private static MemSample ReadMemory()
    {
        try
        {
            var values = File.ReadLines("/proc/meminfo")
                .Select(line => line.Split(':', 2, StringSplitOptions.TrimEntries))
                .Where(pair => pair.Length == 2)
                .ToDictionary(pair => pair[0], pair => ParseKb(pair[1]));
            var total = values.GetValueOrDefault("MemTotal");
            var available = values.GetValueOrDefault("MemAvailable");
            var used = Math.Max(0, total - available);
            var buffersCache = values.GetValueOrDefault("Buffers") + values.GetValueOrDefault("Cached") + values.GetValueOrDefault("SReclaimable");
            return new MemSample(
                TotalMb: total / 1024.0,
                UsedMb: used / 1024.0,
                AvailableMb: available / 1024.0,
                BuffersCacheMb: buffersCache / 1024.0,
                SwapTotalMb: values.GetValueOrDefault("SwapTotal") / 1024.0,
                SwapUsedMb: (values.GetValueOrDefault("SwapTotal") - values.GetValueOrDefault("SwapFree")) / 1024.0,
                UsagePercent: total == 0 ? 0 : R1(used * 100.0 / total));
        }
        catch
        {
            return new MemSample(0, 0, 0, 0, 0, 0, 0);
        }
    }

    // ── 磁盘：/proc/diskstats 计数器差分（整盘，排除 loop/ram 与分区）──
    private DiskSample ReadDisk(double elapsed)
    {
        try
        {
            var whole = WholeDisks();
            var devices = new Dictionary<string, double[]>(StringComparer.Ordinal);
            foreach (var line in File.ReadLines("/proc/diskstats"))
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 10) continue;
                var name = parts[2];
                if (name.StartsWith("loop", StringComparison.Ordinal) || name.StartsWith("ram", StringComparison.Ordinal)) continue;
                if (whole.Count > 0 && !whole.Contains(name)) continue; // 无 /sys 时退化为仅排除 loop/ram
                var readBytes = ParseLong(parts[5]) * SectorBytes;
                var writeBytes = ParseLong(parts[9]) * SectorBytes;
                devices[name] = [readBytes, writeBytes];
            }

            var prev = _prevDisks;
            _prevDisks = devices;
            var rates = new Dictionary<string, double[]>(StringComparer.Ordinal);
            foreach (var kv in devices)
            {
                var old = prev?.GetValueOrDefault(kv.Key);
                rates[kv.Key] = old is null
                    ? [0.0, 0.0]
                    : [Math.Max(0, kv.Value[0] - old[0]) / 1024.0 / elapsed, Math.Max(0, kv.Value[1] - old[1]) / 1024.0 / elapsed];
            }
            return new DiskSample(rates.Values.Sum(v => v[0]), rates.Values.Sum(v => v[1]), rates);
        }
        catch
        {
            return new DiskSample(0, 0, new Dictionary<string, double[]>());
        }
    }

    private static HashSet<string> WholeDisks()
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            foreach (var dir in Directory.EnumerateDirectories("/sys/block"))
            {
                set.Add(Path.GetFileName(dir));
            }
        }
        catch
        {
            /* /sys 不可用时仅排除 loop/ram */
        }
        return set;
    }

    // ── 网络：/proc/net/dev 计数器差分（容器视角，排除 lo）──
    private NetSample ReadNetwork(double elapsed)
    {
        try
        {
            var ifaces = new Dictionary<string, double[]>(StringComparer.Ordinal);
            foreach (var line in File.ReadLines("/proc/net/dev").Skip(2))
            {
                var colon = line.IndexOf(':');
                if (colon < 0) continue;
                var name = line[..colon].Trim();
                if (name == "lo") continue;
                var fields = line[(colon + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (fields.Length < 9) continue;
                ifaces[name] = [ParseLong(fields[0]), ParseLong(fields[8])]; // rx_bytes, tx_bytes
            }

            var prev = _prevNets;
            _prevNets = ifaces;
            var rates = new Dictionary<string, double[]>(StringComparer.Ordinal);
            foreach (var kv in ifaces)
            {
                var old = prev?.GetValueOrDefault(kv.Key);
                rates[kv.Key] = old is null
                    ? [0.0, 0.0]
                    : [Math.Max(0, kv.Value[0] - old[0]) / 1024.0 / elapsed, Math.Max(0, kv.Value[1] - old[1]) / 1024.0 / elapsed];
            }
            return new NetSample(rates.Values.Sum(v => v[0]), rates.Values.Sum(v => v[1]), rates);
        }
        catch
        {
            return new NetSample(0, 0, new Dictionary<string, double[]>());
        }
    }

    // ── GPU：nvidia-smi（仅 NVIDIA；探测失败后不再调用）──
    private GpuSample[] ReadGpus()
    {
        if (_nvidiaMissing) return [];
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "nvidia-smi",
                    Arguments = "--query-gpu=name,utilization.gpu,memory.used,memory.total,temperature.gpu --format=csv,noheader,nounits",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
            };
            process.Start();
            // 同步 ReadToEnd 无超时：nvidia-smi 挂死（驱动卡住）会把采样线程卡死，进而顶住 Snapshot 的锁
            var readTask = process.StandardOutput.ReadToEndAsync();
            if (!process.WaitForExit(3000))
            {
                // 超时视为瞬时故障：杀掉本次采样，但不永久禁用（重启驱动后可自行恢复）
                try { process.Kill(true); } catch { }
                return [];
            }
            var output = readTask.GetAwaiter().GetResult().Trim();
            if (output.Length == 0) return [];
            return output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line =>
            {
                var parts = line.Split(',', StringSplitOptions.TrimEntries);
                return new GpuSample(
                    Name: parts.ElementAtOrDefault(0) ?? "",
                    Utilization: ParseDouble(parts.ElementAtOrDefault(1)),
                    MemoryUsedMb: ParseDouble(parts.ElementAtOrDefault(2)),
                    MemoryTotalMb: ParseDouble(parts.ElementAtOrDefault(3)),
                    Temperature: ParseDouble(parts.ElementAtOrDefault(4)));
            }).ToArray();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            // 二进制不存在/无法启动：永久禁用，避免每 2 秒空转
            _nvidiaMissing = true;
            return [];
        }
        catch
        {
            // 其他异常按瞬时故障处理，不永久禁用
            return [];
        }
    }

    private static object ReadLoadAvg()
    {
        try
        {
            var parts = File.ReadAllText("/proc/loadavg").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return new { one = parts[0], five = parts[1], fifteen = parts[2] };
        }
        catch
        {
            return new { error = "无法读取 /proc/loadavg" };
        }
    }

    private static object ReadTopProcesses()
    {
        try
        {
            var processes = new List<(string pid, string name, double cpuTicks, double rssMb)>();
            foreach (var directory in Directory.EnumerateDirectories("/proc"))
            {
                if (!int.TryParse(Path.GetFileName(directory), out _)) continue;
                try
                {
                    var stat = File.ReadAllText(Path.Combine(directory, "stat"));
                    var commStart = stat.IndexOf('(');
                    var commEnd = stat.LastIndexOf(')');
                    var name = commStart >= 0 && commEnd > commStart ? stat[(commStart + 1)..commEnd] : "?";
                    if (name is not ("ffmpeg" or "ffprobe")) continue;
                    var statParts = stat[(commEnd + 2)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    // utime(12) stime(13) 位于括号后字段 11/12（下标 11/12）
                    var utime = statParts.Length > 12 ? ParseLong(statParts[11]) : 0;
                    var stime = statParts.Length > 13 ? ParseLong(statParts[12]) : 0;
                    var rssPages = statParts.Length > 22 ? ParseLong(statParts[21]) : 0;
                    processes.Add((
                        pid: Path.GetFileName(directory),
                        name,
                        cpuTicks: (utime + stime) / (double)ClockTicks,
                        rssMb: rssPages * 4096 / 1024.0 / 1024.0));
                }
                catch
                {
                }
            }
            // 排序后投影回匿名类型（JSON 字段名保持 pid/name/cpuTicks/rssMb 不变）；
            // 旧实现把匿名类型存进 List<object> 再强转 JsonElement，运行期必炸（进程列表恒失效）
            return processes.OrderByDescending(process => process.cpuTicks).Take(8)
                .Select(process => new { process.pid, process.name, process.cpuTicks, process.rssMb }).ToList();
        }
        catch
        {
            return new { error = "无法读取 /proc" };
        }
    }

    private string CpuModel()
    {
        if (_cpuModel is not null) return _cpuModel;
        try
        {
            _cpuModel = File.ReadLines("/proc/cpuinfo").FirstOrDefault(line => line.StartsWith("model name", StringComparison.Ordinal))?["model name".Length..]?.Trim().TrimStart(':').Trim() ?? "";
        }
        catch
        {
            _cpuModel = "";
        }
        return _cpuModel;
    }

    private static double ClampPct(double value) => Math.Clamp(value, 0, 100);
    private static double R1(double value) => Math.Round(value, 1);
    private static long ParseLong(string value) => long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : 0;
    private static double ParseDouble(string? value) => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) ? result : 0;
    private static long ParseKb(string value) => long.TryParse(value.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : 0;
}
