using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using FFmpegFreeUI;

namespace linux3fui.Server;

/// <summary>队列实时推送：订阅核心库事件，向 WebSocket 客户端广播队列快照与任务事件。</summary>
public sealed class QueueRealtime : IAsyncDisposable
{
    private readonly object _lock = new();
    private readonly List<WebSocket> _sockets = new();
    private readonly Timer _ticker;
    private int _version;

    public QueueRealtime()
    {
        编码队列_v6.队列已变化 += OnQueueChanged;
        编码队列_v6.任务已更新 += OnTaskUpdated;
        编码队列_v6.插件事件已触发 += OnPluginEvent;
        编码队列_v6.编码器切换通知 += OnEncoderSwitch;
        _ticker = new Timer(_ => BroadcastProgress(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    private void OnQueueChanged() => Broadcast(new { type = "queue", version = ++_version, tasks = Snapshot() });
    private void OnTaskUpdated(编码任务_v6 task) => Broadcast(new { type = "task", task = TaskDto(task) });
    private void OnPluginEvent(string name, 编码任务_v6 task, 编码任务日志条目_v6? log)
    {
        Broadcast(new
        {
            type = "event",
            name,
            taskId = task.ID,
            log = log is null ? null : new { log.序号, log.时间, log.阶段名, log.文本, 类别 = log.类别.ToString(), log.是否错误 },
        });
    }

    private void OnEncoderSwitch(编码器等价切换_v6.编码器切换信息_v6 信息)
    {
        Broadcast(new
        {
            type = "encoder-switch",
            信息.任务ID,
            信息.任务名称,
            信息.原编码器,
            信息.新编码器,
            信息.原因,
            信息.触发方式,
            信息.换算明细,
            信息.丢弃参数,
            信息.时间,
        });
    }

    private void BroadcastProgress()
    {
        var running = 编码队列_v6.获取队列快照().Where(t => t.正在执行 || t.状态 == 编码任务状态_v6.正在处理 || t.状态 == 编码任务状态_v6.已暂停).ToList();
        if (running.Count == 0) return;
        Broadcast(new
        {
            type = "progress",
            tasks = running.Select(t => new
            {
                t.ID,
                t.进度.当前阶段,
                进度文本 = t.进度.进度文本,
                t.进度.效率文本,
                t.进度.输出大小文本,
                t.进度.质量文本,
                t.进度.比特率文本,
                t.进度.时间文本,
                百分比 = t.进度.百分比,
                t.实时输出,
                t.最新底部日志文本,
                t.最新底部日志是否错误,
                状态 = t.状态.ToString(),
            }),
        });
    }

    public void AddSocket(WebSocket socket)
    {
        lock (_lock) _sockets.Add(socket);
        var payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
            new { type = "queue", version = _version, tasks = Snapshot() }, JsonOptions.Compact));
        _ = TrySend(socket, payload);
    }

    public void RemoveSocket(WebSocket socket)
    {
        lock (_lock) _sockets.Remove(socket);
    }

    private void Broadcast(object payload)
    {
        List<WebSocket> targets;
        lock (_lock) targets = [.. _sockets];
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload, JsonOptions.Compact));
        foreach (var socket in targets) _ = TrySend(socket, bytes);
    }

    private static async Task TrySend(WebSocket socket, byte[] payload)
    {
        try
        {
            if (socket.State == WebSocketState.Open)
                await socket.SendAsync(payload, WebSocketMessageType.Text, true, CancellationToken.None);
        }
        catch
        {
            // 客户端断开由接收循环负责清理。
        }
    }

    private static object Snapshot() => 编码队列_v6.获取队列快照().Select(TaskDto).ToList();

    public static object TaskDto(编码任务_v6 t) => new
    {
        t.ID,
        t.任务名称,
        t.输入文件,
        t.输出文件,
        状态 = t.状态.ToString(),
        状态码 = (int)t.状态,
        t.命令行,
        进度文本 = t.进度.进度文本,
        t.进度.效率文本,
        t.进度.输出大小文本,
        t.进度.质量文本,
        t.进度.比特率文本,
        t.进度.时间文本,
        百分比 = t.进度.百分比,
        t.实时输出,
        t.最新底部日志文本,
        t.最新底部日志是否错误,
        t.媒体总时长,
        t.当前进程ID,
        t.允许自动启动,
        t.可移除,
        t.可重置,
        t.可排序,
        t.预设编码器,
        t.实际编码器,
        切换记录 = t.编码器切换记录,
    };

    public async ValueTask DisposeAsync()
    {
        await _ticker.DisposeAsync();
        编码队列_v6.队列已变化 -= OnQueueChanged;
        编码队列_v6.任务已更新 -= OnTaskUpdated;
        编码队列_v6.插件事件已触发 -= OnPluginEvent;
        编码队列_v6.编码器切换通知 -= OnEncoderSwitch;
        List<WebSocket> targets;
        lock (_lock) targets = [.. _sockets];
        foreach (var socket in targets)
        {
            try { await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "server shutdown", CancellationToken.None); }
            catch { }
        }
    }
}

/// <summary>核心库 JSON 序列化配置（中文不转义，与上游 JsonSO 行为一致）。</summary>
public static class JsonOptions
{
    public static readonly JsonSerializerOptions Compact = new()
    {
        PropertyNamingPolicy = null,
        DictionaryKeyPolicy = null,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new linux3fui.Server.LenientEnumJsonConverter() },
    };
}
