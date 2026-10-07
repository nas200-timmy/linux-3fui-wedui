Imports System
Imports System.Diagnostics
Imports System.IO
Imports System.Runtime.InteropServices

' Windows 专属互操作的跨平台声明（与上游 Module1 一致）。
' Windows 路径调用前必须先判断 OperatingSystem.IsWindows()；Linux 上调用会抛出 DllNotFoundException。
Module Windows互操作
    <DllImport("kernel32.dll", SetLastError:=True)>
    Public Function SetThreadExecutionState(esFlags As EXECUTION_STATE) As EXECUTION_STATE
    End Function

    <Flags>
    Public Enum EXECUTION_STATE As UInteger
        ES_SYSTEM_REQUIRED = &H1
        ES_DISPLAY_REQUIRED = &H2
        ES_CONTINUOUS = &H80000000UI
    End Enum

    <DllImport("ntdll.dll")>
    Public Function NtSuspendProcess(processHandle As IntPtr) As Integer
    End Function

    <DllImport("ntdll.dll")>
    Public Function NtResumeProcess(processHandle As IntPtr) As Integer
    End Function

    ' Linux 信号（libc kill）：19 = SIGSTOP，18 = SIGCONT
    <DllImport("libc", EntryPoint:="kill", SetLastError:=True)>
    Private Function Kill(pid As Integer, sig As Integer) As Integer
    End Function

    ''' <summary>跨平台挂起进程：Windows 用 NtSuspendProcess，Linux 用 SIGSTOP。</summary>
    Public Function 挂起进程(p As Process) As Boolean
        If p Is Nothing OrElse p.HasExited Then Return False
        If OperatingSystem.IsWindows() Then Return NtSuspendProcess(p.Handle) = 0
        Return Kill(p.Id, 19) = 0
    End Function

    ''' <summary>跨平台恢复进程：Windows 用 NtResumeProcess，Linux 用 SIGCONT。</summary>
    Public Function 恢复进程(p As Process) As Boolean
        If p Is Nothing OrElse p.HasExited Then Return False
        If OperatingSystem.IsWindows() Then Return NtResumeProcess(p.Handle) = 0
        Return Kill(p.Id, 18) = 0
    End Function

    ''' <summary>根据核心编号列表生成 ProcessorAffinity 掩码（与上游 Module1 相同实现）。</summary>
    Public Function GetAffinityMask(cores As Integer()) As IntPtr
        Dim mask As Long = 0
        For Each core In cores
            If core >= 0 AndAlso core < Environment.ProcessorCount Then
                mask = mask Or CLng(1) << core
            Else
                Throw New ArgumentOutOfRangeException($"核心编号 {core} 无效。系统共有 {Environment.ProcessorCount} 个核心（从 0 开始）。")
            End If
        Next
        Return New IntPtr(mask)
    End Function

    ' 任务完成/失败提示音（上游来自 My.Resources；linux-3fui 服务端无声音输出，保留字段仅为兼容调用点）。
    Public Sound_Finish As Stream = Nothing
    Public Sound_Error As Stream = Nothing
End Module
