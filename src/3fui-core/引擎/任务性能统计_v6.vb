' 任务性能统计：linux-3fui 适配桩。
' 上游实现依赖 LakeUI 的 MainAppUsageCounter（GPL 且 Windows 专用），此处保留相同公共 API，
' 具体采样由服务端通过 Linux /proc 与 nvidia-smi 实现（见 3fui-server 的 PerformanceMonitor）。
Public NotInheritable Class 任务性能统计_v6
    Private Sub New()
    End Sub

    ''' <summary>释放指定进程的计数器（Linux 实现无缓存，直接返回）。</summary>
    Public Shared Sub 释放进程计数器(processId As Integer)
    End Sub

    ''' <summary>按需采样快照（保持与上游相同的返回结构；Linux 实现由服务端覆盖）。</summary>
    Public Shared Function 获取快照(task As 编码任务_v6) As Dictionary(Of String, Object)
        Dim result As New Dictionary(Of String, Object) From {
            {"process_id", If(task Is Nothing, 0, task.当前进程ID)},
            {"sampling", "linux_stub"}
        }
        result("available") = False
        result("reason") = "Linux 实现由服务端 PerformanceMonitor 提供"
        Return result
    End Function
End Class
