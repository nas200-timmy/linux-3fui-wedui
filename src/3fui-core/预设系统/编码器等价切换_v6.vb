Imports System.Collections.Generic

''' <summary>
''' 编码器等价切换：编码器故障时在同一硬件家族内切换等价编码器，并做质量参数等价换算。
''' 映射表为 Linux 范围：Intel 核显 QSV ↔ VAAPI（同一块硅片/同一媒体引擎）。
''' NVIDIA NVENC / AMD AMF 在 Linux 下无同硬件替代品——不可用则如实报错，不偷换硬件。
''' </summary>
Public Module 编码器等价切换_v6

    ''' <summary>一次切换的完整告知数据：任务日志、WS 弹窗、写回预设三处共用。</summary>
    Public Class 编码器切换信息_v6
        Public Property 任务ID As String = ""
        Public Property 任务名称 As String = ""
        Public Property 原编码器 As String = ""
        Public Property 新编码器 As String = ""
        Public Property 原因 As String = ""
        Public Property 触发方式 As String = ""
        Public Property 换算明细 As New List(Of String)
        Public Property 丢弃参数 As New List(Of String)
        Public Property 时间 As DateTime = DateTime.Now
    End Class

    Public Class 编码器切换结果_v6
        Public Property 成功 As Boolean = False
        Public Property 新编码器 As String = ""
        Public Property 换算明细 As New List(Of String)
        Public Property 丢弃参数 As New List(Of String)
        Public Property 错误信息 As String = ""
    End Class

    ' 等价组：组内顺序即回退顺序（从前缀相同的格式家族推断）
    Private ReadOnly 等价组表 As List(Of List(Of String)) = New List(Of List(Of String)) From {
        New List(Of String) From {"h264_qsv", "h264_vaapi"},
        New List(Of String) From {"hevc_qsv", "hevc_vaapi"},
        New List(Of String) From {"av1_qsv", "av1_vaapi"}
    }

    ' QSV -preset 档位 ↔ VAAPI -compression_level（0 最快 ~ 7 最慢）
    Private ReadOnly QSV到VAAPI预设映射 As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase) From {
        {"veryfast", "1"}, {"faster", "2"}, {"fast", "3"}, {"medium", "4"},
        {"slow", "5"}, {"slower", "6"}, {"veryslow", "7"}}
    Private ReadOnly VAAPI到QSV预设映射 As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase) From {
        {"0", "veryfast"}, {"1", "veryfast"}, {"2", "faster"}, {"3", "fast"},
        {"4", "medium"}, {"5", "slow"}, {"6", "slower"}, {"7", "veryslow"}}

    ' 进阶参数集里所有硬件编码器都接受的通用开关白名单
    Private ReadOnly 通用参数白名单 As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {
        "-g", "-bf", "-refs", "-threads", "-aspect"}

    ''' <summary>硬件编码器判定（与预设命令行编码_v6 的 EndsWith 风格一致）。</summary>
    Public Function 是硬件编码器(编码器名 As String) As Boolean
        If String.IsNullOrWhiteSpace(编码器名) Then Return False
        Return 编码器名.EndsWith("_qsv", StringComparison.OrdinalIgnoreCase) OrElse
               编码器名.EndsWith("_vaapi", StringComparison.OrdinalIgnoreCase) OrElse
               编码器名.EndsWith("_nvenc", StringComparison.OrdinalIgnoreCase) OrElse
               编码器名.EndsWith("_amf", StringComparison.OrdinalIgnoreCase)
    End Function

    ''' <summary>目标编码器所属等价组（无组则返回只含自身的组）。</summary>
    Public Function 获取等价编码器组(编码器名 As String) As List(Of String)
        For Each 组 In 等价组表
            If 组.Contains(编码器名) Then Return 组
        Next
        Return New List(Of String) From {编码器名}
    End Function

    ''' <summary>组内从原编码器的下一个开始，找第一个未尝试过且通过可用性判断的候选。</summary>
    Public Function 选择回退编码器(原编码器 As String, 已尝试 As IEnumerable(Of String), 可用性判断 As Func(Of String, Boolean)) As String
        If String.IsNullOrWhiteSpace(原编码器) OrElse 可用性判断 Is Nothing Then Return ""
        Dim 组 = 获取等价编码器组(原编码器)
        If 组.Count <= 1 Then Return ""
        Dim 已尝试集 = If(已尝试, Enumerable.Empty(Of String)()).ToList()
        Dim 起点 = 组.IndexOf(原编码器)
        If 起点 < 0 Then 起点 = 0
        For 偏移 = 1 To 组.Count - 1
            Dim 候选 = 组((起点 + 偏移) Mod 组.Count)
            If 已尝试集.Contains(候选) Then Continue For
            Try
                If 可用性判断(候选) Then Return 候选
            Catch
            End Try
        Next
        Return ""
    End Function

    ''' <summary>
    ''' 把预设就地换算到目标编码器：编码器名、质量参数名、编码预设档位、像素格式、进阶参数集。
    ''' 换算明细与丢弃参数写入结果，供日志/弹窗告知用户。
    ''' </summary>
    Public Function 换算预设到新编码器(预设 As 预设数据_v6, 目标编码器 As String) As 编码器切换结果_v6
        Dim 结果 As New 编码器切换结果_v6 With {.新编码器 = 目标编码器}
        If 预设 Is Nothing Then
            结果.错误信息 = "预设数据为空"
            Return 结果
        End If
        Dim 源编码器 = 预设.视频参数_编码器_具体编码
        Dim 目标数据 = 视频编码器数据库_v6.获取编码器数据(目标编码器)
        If 目标数据 Is Nothing Then
            结果.错误信息 = $"编码器数据库中不存在 {目标编码器}"
            Return 结果
        End If

        预设.视频参数_编码器_具体编码 = 目标编码器
        If Not String.IsNullOrWhiteSpace(目标数据.分类名称) Then 预设.视频参数_编码器_分类名称 = 目标数据.分类名称
        结果.换算明细.Add($"编码器 {源编码器} → {目标编码器}（同硬件等价）")

        换算质量参数名(预设, 目标编码器, 结果.换算明细)
        换算编码预设档位(预设, 源编码器, 目标编码器, 结果.换算明细)
        换算像素格式(预设, 结果.换算明细)
        过滤进阶参数集(预设, 目标数据, 结果)

        结果.成功 = True
        Return 结果
    End Function

    Private Sub 换算质量参数名(预设 As 预设数据_v6, 目标编码器 As String, 明细 As List(Of String))
        Dim 质量名 = If(预设.视频参数_质量控制_参数名, "").Trim()
        If 质量名 = "" Then Return
        ' 五候选质量参数（-crf/-cq/-global_quality/-qp）标度一致（越低越清晰），数值原样保留
        Dim 目标名 As String = 质量名
        If 质量名.Equals("-crf", StringComparison.OrdinalIgnoreCase) OrElse
           质量名.Equals("-cq", StringComparison.OrdinalIgnoreCase) OrElse
           质量名.Equals("-global_quality", StringComparison.OrdinalIgnoreCase) OrElse
           质量名.Equals("-qp", StringComparison.OrdinalIgnoreCase) Then
            If 目标编码器.EndsWith("_vaapi", StringComparison.OrdinalIgnoreCase) Then
                目标名 = "-qp"
            ElseIf 目标编码器.EndsWith("_qsv", StringComparison.OrdinalIgnoreCase) Then
                目标名 = "-global_quality"
            End If
        End If
        If 目标名 <> 质量名 Then
            明细.Add($"质量参数 {质量名} → {目标名}（值 {预设.视频参数_质量控制_值} 保留，标度一致：越低越清晰）")
            预设.视频参数_质量控制_参数名 = 目标名
        End If
    End Sub

    Private Sub 换算编码预设档位(预设 As 预设数据_v6, 源编码器 As String, 目标编码器 As String, 明细 As List(Of String))
        Dim 档位 = If(预设.视频参数_编码器_编码预设, "").Trim()
        Dim 换算后 As String = Nothing
        If 源编码器.EndsWith("_qsv", StringComparison.OrdinalIgnoreCase) AndAlso
           目标编码器.EndsWith("_vaapi", StringComparison.OrdinalIgnoreCase) Then
            If 档位 <> "" AndAlso QSV到VAAPI预设映射.TryGetValue(档位, 换算后) Then
                明细.Add($"速度档位 -preset {档位} → -compression_level {换算后}（0 最快 ~ 7 最慢）")
                预设.视频参数_编码器_编码预设 = 换算后
            End If
        ElseIf 源编码器.EndsWith("_vaapi", StringComparison.OrdinalIgnoreCase) AndAlso
               目标编码器.EndsWith("_qsv", StringComparison.OrdinalIgnoreCase) Then
            If 档位 <> "" AndAlso VAAPI到QSV预设映射.TryGetValue(档位, 换算后) Then
                明细.Add($"速度档位 -compression_level {档位} → -preset {换算后}")
                预设.视频参数_编码器_编码预设 = 换算后
            End If
        End If
        ' 未映射或为空：不改动——命令行生成时受目标编码器值列表白名单约束，非法值自动跳过
    End Sub

    Private Sub 换算像素格式(预设 As 预设数据_v6, 明细 As List(Of String))
        Dim 像素 = If(预设.视频参数_色彩管理_像素格式, "").Trim()
        If 像素.ToLowerInvariant().Contains("qsv") Then
            预设.视频参数_色彩管理_像素格式 = "nv12"
            明细.Add("像素格式 ""nv12 qsv"" → ""nv12""（VAAPI 无 qsv 表面）")
        End If
    End Sub

    ''' <summary>进阶参数集是自由文本，逐对（开关+值）过滤：只保留目标编码器认识的开关。</summary>
    Private Sub 过滤进阶参数集(预设 As 预设数据_v6, 目标数据 As 视频编码器数据库_v6.视频编码器数据, 结果 As 编码器切换结果_v6)
        Dim 原始 = If(预设.视频参数_质量控制_进阶参数集, "").Trim()
        If 原始 = "" Then Return
        Dim 白名单 As New HashSet(Of String)(目标数据.特殊参数名列表, StringComparer.OrdinalIgnoreCase)
        白名单.UnionWith(通用参数白名单)

        Dim 记号 = 原始.Split({" "c}, StringSplitOptions.RemoveEmptyEntries)
        Dim 保留 As New List(Of String)
        Dim 丢弃 As New List(Of String)
        Dim i = 0
        While i < 记号.Length
            Dim 开关 = 记号(i)
            ' 收集该开关及其值（值是下一个不以 - 开头的记号；连续开关各自无值）
            Dim 消费 = 1
            If i + 1 < 记号.Length AndAlso Not 记号(i + 1).StartsWith("-"c) Then 消费 = 2
            If 开关.StartsWith("-"c) AndAlso 白名单.Contains(开关) Then
                For j = i To i + 消费 - 1
                    保留.Add(记号(j))
                Next
            ElseIf 开关.StartsWith("-"c) Then
                丢弃.Add(String.Join(" ", 记号.Skip(i).Take(消费)))
            Else
                保留.Add(开关)
            End If
            i += 消费
        End While

        预设.视频参数_质量控制_进阶参数集 = String.Join(" ", 保留)
        If 丢弃.Count > 0 Then
            结果.丢弃参数.AddRange(丢弃)
            结果.换算明细.Add($"进阶参数集中 {丢弃.Count} 项不被 {目标数据.名称} 支持，已移除（见丢弃列表）")
        End If
    End Sub

End Module
