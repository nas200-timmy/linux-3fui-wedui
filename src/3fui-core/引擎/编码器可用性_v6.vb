Imports System.Collections.Concurrent
Imports System.Diagnostics
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks

''' <summary>
''' 编码器可用性探测：以 1 帧试编码探针判定各硬件编码器在当前环境是否可用。
''' 探针命令形态与 3fui 实际生成的命令一致（QSV 带 -low_power 1，VAAPI 带 -vaapi_device+hwupload）。
''' 结果供「任务启动前检查」使用；探测缺失时由运行时兜底分支接管。
''' </summary>
Public Module 编码器可用性_v6

    Public Class 编码器探测结果_v6
        Public Property 编码器 As String = ""
        Public Property 已探测 As Boolean = False
        Public Property 可用 As Boolean = False
        Public Property 失败摘录 As String = ""
        Public Property 探测时间 As DateTime = DateTime.MinValue
    End Class

    Private ReadOnly 结果表 As New ConcurrentDictionary(Of String, 编码器探测结果_v6)(StringComparer.OrdinalIgnoreCase)
    Private ReadOnly 单探测锁表 As New ConcurrentDictionary(Of String, SemaphoreSlim)(StringComparer.OrdinalIgnoreCase)

    ''' <summary>等价组内全部编码器（即探测全集）。</summary>
    Public ReadOnly Property 待探测编码器列表 As List(Of String)
        Get
            Dim 全部 As New List(Of String)
            For Each 名称 In {"h264_qsv", "h264_vaapi", "hevc_qsv", "hevc_vaapi", "av1_qsv", "av1_vaapi"}
                If Not 全部.Contains(名称) Then 全部.Add(名称)
            Next
            Return 全部
        End Get
    End Property

    ''' <summary>查询已缓存的探测结果；未探测过返回 Nothing。</summary>
    Public Function 查询(编码器 As String) As 编码器探测结果_v6
        If String.IsNullOrWhiteSpace(编码器) Then Return Nothing
        Dim 结果 As 编码器探测结果_v6 = Nothing
        结果表.TryGetValue(编码器, 结果)
        Return 结果
    End Function

    ''' <summary>并发安全地确保某编码器已被探测（每编码器一把锁，重复调用零开销）。</summary>
    Public Async Function 确保已探测Async(编码器 As String) As Task(Of 编码器探测结果_v6)
        Dim 已有 = 查询(编码器)
        If 已有 IsNot Nothing AndAlso 已有.已探测 Then Return 已有
        Dim 锁 = 单探测锁表.GetOrAdd(编码器, Function(k) New SemaphoreSlim(1, 1))
        Await 锁.WaitAsync()
        Try
            已有 = 查询(编码器)
            If 已有 IsNot Nothing AndAlso 已有.已探测 Then Return 已有
            Return Await 探测一个Async(编码器)
        Finally
            锁.Release()
        End Try
    End Function

    ''' <summary>启动探测：并行探测全集，不阻塞服务启动。</summary>
    Public Async Function 探测全部Async() As Task
        Dim 任务列表 = 待探测编码器列表.Select(Function(名称) 确保已探测Async(名称)).ToList()
        Await Task.WhenAll(任务列表)
    End Function

    ''' <summary>强制重新探测（忽略缓存），供 /api/hw/encoders/reprobe 使用。</summary>
    Public Async Function 强制重探测Async() As Task
        Dim 任务列表 = 待探测编码器列表.Select(Function(名称) 强制探测一个Async(名称)).ToList()
        Await Task.WhenAll(任务列表)
    End Function

    Private Async Function 强制探测一个Async(编码器 As String) As Task(Of 编码器探测结果_v6)
        Dim 锁 = 单探测锁表.GetOrAdd(编码器, Function(k) New SemaphoreSlim(1, 1))
        Await 锁.WaitAsync()
        Try
            Return Await 探测一个Async(编码器)
        Finally
            锁.Release()
        End Try
    End Function

    Private Async Function 探测一个Async(编码器 As String) As Task(Of 编码器探测结果_v6)
        Dim 结果 As New 编码器探测结果_v6 With {.编码器 = 编码器}
        Try
            Dim 探针参数 As String
            If 编码器.EndsWith("_qsv", StringComparison.OrdinalIgnoreCase) Then
                ' 与预设命令行编码_v6 的 QSV 分支一致：自动补 -low_power 1
                探针参数 = $"-c:v {编码器} -low_power 1"
            ElseIf 编码器.EndsWith("_vaapi", StringComparison.OrdinalIgnoreCase) Then
                ' 与预设命令行输入输出_v6 的 VAAPI 分支一致：-vaapi_device + hwupload
                探针参数 = $"-vaapi_device /dev/dri/renderD128 -vf format=nv12,hwupload -c:v {编码器}"
            Else
                探针参数 = $"-c:v {编码器}"
            End If
            Dim 参数 = $"-v error -f lavfi -i testsrc2=duration=1:size=320x240 {探针参数} -f null -"
            Dim 错误输出 As New StringBuilder()
            Using 取消源 As New CancellationTokenSource(TimeSpan.FromSeconds(20))
                Using 进程 As New Process()
                    进程.StartInfo.FileName = 设置_v6.获取FFmpeg进程文件名()
                    进程.StartInfo.Arguments = 参数
                    进程.StartInfo.UseShellExecute = False
                    进程.StartInfo.RedirectStandardError = True
                    进程.StartInfo.RedirectStandardOutput = True
                    进程.StartInfo.StandardOutputEncoding = Encoding.UTF8
                    进程.StartInfo.StandardErrorEncoding = Encoding.UTF8
                    进程.StartInfo.CreateNoWindow = True
                    AddHandler 进程.ErrorDataReceived, Sub(_s, e)
                                                             If e.Data IsNot Nothing AndAlso 错误输出.Length < 8192 Then 错误输出.AppendLine(e.Data)
                                                         End Sub
                    进程.Start()
                    进程.BeginErrorReadLine()
                    进程.BeginOutputReadLine()
                    Try
                        Await 进程.WaitForExitAsync(取消源.Token)
                        结果.可用 = (进程.ExitCode = 0)
                    Catch ex As OperationCanceledException
                        ' 超时必须杀掉探针：Linux 上 Process.Dispose 不杀子进程，不杀会一直挂着
                        Try
                            进程.Kill(True)
                        Catch
                        End Try
                        Throw
                    End Try
                End Using
            End Using
            If Not 结果.可用 Then
                结果.失败摘录 = 提取失败摘录(错误输出.ToString())
                If 结果.失败摘录 = "" Then 结果.失败摘录 = "试编码退出码非 0（无 stderr 输出）"
            End If
        Catch ex As OperationCanceledException
            结果.可用 = False
            结果.失败摘录 = "探测超时（20 秒）"
        Catch ex As Exception
            结果.可用 = False
            结果.失败摘录 = "探测异常：" & ex.Message
        End Try
        结果.已探测 = True
        结果.探测时间 = DateTime.Now
        结果表(编码器) = 结果
        Return 结果
    End Function

    Private Function 提取失败摘录(原始 As String) As String
        If String.IsNullOrWhiteSpace(原始) Then Return ""
        Dim 行 = 原始.Split({vbCr, vbLf}, StringSplitOptions.RemoveEmptyEntries)
        Dim 取 = Math.Min(行.Length, 3)
        Return String.Join(" / ", 行.Skip(行.Length - 取).Select(Function(x) x.Trim()))
    End Function

    ' 编码器初始化类故障特征（stderr 文本判定）。刻意不含 "Invalid argument"（过于宽泛）。
    Private ReadOnly 编码器故障特征 As String() = {
        "Error creating a MFX session",
        "MFXVideoENCODE",
        "Failed to set value 'qsv",
        "Failed to initialise VAAPI",
        "va_openDriver() returns -1",
        "Device creation failed",
        "No NVENC capable devices",
        "Cannot init CUDA",
        "Cannot load nvEncodeAPI",
        "Unknown encoder",
        "some encoding parameters are not supported"
    }

    ''' <summary>判断失败输出是否为「编码器本身不可用」类故障（区别于片源错误、滤镜缺失等）。</summary>
    Public Function Is编码器故障(输出文本 As String) As Boolean
        If String.IsNullOrWhiteSpace(输出文本) Then Return False
        For Each 特征 In 编码器故障特征
            If 输出文本.Contains(特征, StringComparison.OrdinalIgnoreCase) Then Return True
        Next
        Return False
    End Function

End Module
