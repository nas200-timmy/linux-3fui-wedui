Imports System.Threading
Imports System.Threading.Tasks

' linux-3fui 适配：上游 用户使用统计_v6 的统计核心（原样移植），
' 移除赞助提示等 WinForms UI 部分。linux-3fui 不上报任何数据。
Public NotInheritable Class 用户使用统计_v6

    Private Sub New()
    End Sub

    Private Shared ReadOnly 后台调度器 As TaskScheduler =
        New ConcurrentExclusiveSchedulerPair(TaskScheduler.Default, 1).ExclusiveScheduler

    Public Shared Sub 记录编码任务执行结果(成功完成 As Boolean, 执行时长 As TimeSpan)
        Task.Factory.StartNew(
            Sub()
                Try
                    Dim 设置 = 设置_v6.实例对象
                    设置.用户统计_任务执行总时长Ticks = 安全累加(设置.用户统计_任务执行总时长Ticks, Math.Max(0, 执行时长.Ticks))
                    If 成功完成 Then
                        设置.用户统计_成功编码任务数 = 安全累加(设置.用户统计_成功编码任务数, 1)
                        设置.用户统计_首次成功提示已显示 = True
                    End If
                    设置_v6.后台保存设置()
                Catch
                End Try
            End Sub,
            CancellationToken.None,
            TaskCreationOptions.DenyChildAttach,
            后台调度器)
    End Sub

    Private Shared Function 安全累加(current As Long, delta As Long) As Long
        Dim maxValue As Long = Long.MaxValue
        If delta <= 0 Then Return current
        If current > maxValue - delta Then Return maxValue
        Return current + delta
    End Function

End Class
