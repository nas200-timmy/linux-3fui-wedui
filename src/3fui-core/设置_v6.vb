Imports System.IO
Imports System.Text
Imports System.Text.Json

' linux-3fui 的 设置_v6 桥接实现。
' 与上游 FFmpegFreeUI 的 设置_v6 保持字段名兼容（Settings.json 可互通，仅 UI 专属字段被省略）。
' 进程文件默认名改为 Linux 的 "ffmpeg"/"ffprobe"（上游为 ffmpeg.exe/ffprobe.exe）。
Public Class 设置_v6

    Public Shared Property 实例对象 As New 设置_v6

    ' 性能调度
    Public Property 指定处理器核心 As String = ""
    Public Property 自动同时运行任务数量选项 As Integer = 0
    Public Property 编码队列刷新速度 As Integer = 2

    ' 功能设定
    Public Property 工作目录 As String = ""
    Public Property 有任务时系统保持状态选项 As Integer = 0
    Public Property 提示音选项 As Integer = 0
    Public Property 自动开始任务选项 As Integer = 0
    Public Property 自动重置参数面板的页面选择 As Integer = 0
    Public Property 混淆任务名称 As Integer = 0
    Public Property 任务失败自动删除输出文件 As Integer = 0
    Public Property 编码队列显示最新日志行 As Integer = 0
    Public Property 任务日志保留行数选项 As Integer = 1
    Public Property 任务日志性能计数器 As Integer = 0

    ' 转译辅助（Linux 下可用于指向自定义 ffmpeg 路径或包装脚本）
    Public Property 替代进程文件名 As String = ""
    Public Property 覆盖参数传递 As String = ""
    Public Property 转译模式 As Boolean = False

    ' 远程调用
    Public Property 是否监听端口 As Boolean = False
    Public Property 监听的端口 As String = "10591"

    ' Agent（与上游字段名一致）
    Public Property AgentEndPoint As String = ""
    Public Property AgentApiKey As String = ""
    Public Property Agent附加请求头 As String = ""
    Public Property Agent附加请求Body As String = ""
    Public Property AgentModelId As String = ""
    Public Property Agent推理级别 As String = ""
    ''' <summary>0=本地联网；1=端点联网；2=禁用联网</summary>
    Public Property Agent联网设置 As Integer = 0
    Public Property Agent权限级别 As Integer = 0
    Public Property Agent权限_编辑参数面板 As Boolean = True
    Public Property Agent权限_添加和编辑任务 As Boolean = False
    Public Property Agent权限_访问编码队列 As Boolean = False

    ' 用户统计（保留字段，linux-3fui 不上报）
    Public Property 用户统计_成功编码任务数 As Long = 0
    Public Property 用户统计_任务执行总时长Ticks As Long = 0
    Public Property 用户统计_首次成功提示已显示 As Boolean = False
    Public Property 用户统计_已提示编码任务百次档位 As Long = 0
    Public Property 用户统计_已提示任务时长240小时档位 As Long = 0

    Public Property 更新服务器选择 As Integer = 0
    Public Property MirrorChyanCDK As String = ""
    Public Property 是否询问标记_下载服务器选择 As Boolean = False
    Public Property 自定义视频编码器列表 As New List(Of String)
    Public Property 质量评测页面状态 As String = ""

    Public Shared Function 获取FFmpeg进程文件名() As String
        Dim custom = If(实例对象?.替代进程文件名, "").Trim()
        Return 解析工作目录进程文件(If(custom <> "", custom, "ffmpeg"))
    End Function

    Public Shared Function 获取FFprobe进程文件名() As String
        Return 获取同目录配套进程文件名("ffprobe")
    End Function

    Public Shared Function 获取FFplay进程文件名() As String
        Return 获取同目录配套进程文件名("ffplay")
    End Function

    Public Shared Function 获取有效工作目录() As String
        Dim directory = If(实例对象?.工作目录, "").Trim()
        Return If(System.IO.Directory.Exists(directory), directory, "")
    End Function

    Private Shared Function 获取同目录配套进程文件名(defaultFileName As String) As String
        Dim custom = If(实例对象?.替代进程文件名, "").Trim()
        If custom <> "" Then
            Dim customDirectory = Path.GetDirectoryName(custom)
            If Not String.IsNullOrWhiteSpace(customDirectory) Then
                Dim candidate = Path.Combine(customDirectory, defaultFileName)
                If File.Exists(candidate) Then Return Path.GetFullPath(candidate)
            End If
        End If
        Return 解析工作目录进程文件(defaultFileName)
    End Function

    Private Shared Function 解析工作目录进程文件(processFileName As String) As String
        Dim value = If(processFileName, "").Trim().Trim(""""c)
        If value = "" Then Return value
        If Path.IsPathRooted(value) Then Return value

        Dim workingDirectory = 获取有效工作目录()
        If workingDirectory <> "" Then
            Dim candidate = Path.Combine(workingDirectory, value)
            If File.Exists(candidate) Then Return Path.GetFullPath(candidate)
        End If
        Return value
    End Function

    ' 设置持久化（与上游相同的字段名与 JSON 格式；路径改为数据目录）
    Private Shared ReadOnly Property 设置文件路径 As String
        Get
            Return Path.Combine(路径助手.数据目录, "Settings.json")
        End Get
    End Property
    Private Shared ReadOnly 设置文件写入锁 As New Object()

    Public Shared Sub 退出时保存设置()
        Try
            保存设置到文件()
        Catch
        End Try
    End Sub

    Friend Shared Sub 后台保存设置()
        保存设置到文件()
    End Sub

    Private Shared Sub 保存设置到文件()
        SyncLock 设置文件写入锁
            Dim 临时文件路径 = 设置文件路径 & ".tmp"
            Try
                File.WriteAllText(临时文件路径, JsonSerializer.Serialize(实例对象, JsonSO), New UTF8Encoding(False))
                File.Move(临时文件路径, 设置文件路径, True)
                ' 含 AgentApiKey 等密钥，仅属主可读（Windows 上该 API 无意义故跳过）
                If Not OperatingSystem.IsWindows() Then File.SetUnixFileMode(设置文件路径, UnixFileMode.UserRead Or UnixFileMode.UserWrite)
            Finally
                If File.Exists(临时文件路径) Then File.Delete(临时文件路径)
            End Try
        End SyncLock
    End Sub

    Public Shared Sub 启动时加载设置()
        If Not File.Exists(设置文件路径) Then
            退出时保存设置()
        Else
            Try
                Dim 设置文本 = File.ReadAllText(设置文件路径)
                实例对象 = JsonSerializer.Deserialize(Of 设置_v6)(设置文本, JsonSO)
                迁移旧设置字段(设置文本)
            Catch
            End Try
        End If
    End Sub

    Private Shared Sub 迁移旧设置字段(设置文本 As String)
        If String.IsNullOrWhiteSpace(设置文本) Then Exit Sub
        Try
            Using doc = JsonDocument.Parse(设置文本)
                Dim root = doc.RootElement

                If 实例对象.Agent权限级别 = 0 Then
                    Dim boolValue As Boolean
                    If 读取布尔(root, "Agent权限_访问编码队列", boolValue) AndAlso boolValue Then
                        实例对象.Agent权限级别 = 1
                    End If
                    If 读取布尔(root, "Agent权限_添加和编辑任务", boolValue) AndAlso boolValue Then
                        实例对象.Agent权限级别 = 1
                    End If
                End If

            End Using
        Catch
        End Try
    End Sub

    Private Shared Function 读取布尔(root As JsonElement, name As String, ByRef value As Boolean) As Boolean
        Dim element As JsonElement
        If Not root.TryGetProperty(name, element) OrElse element.ValueKind <> JsonValueKind.True AndAlso element.ValueKind <> JsonValueKind.False Then Return False
        value = element.GetBoolean()
        Return True
    End Function

End Class
