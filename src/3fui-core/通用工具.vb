Imports System
Imports System.IO
Imports System.Text

' linux-3fui 适配：上游 Module1 中的纯逻辑通用函数（原样移植，模块级以便无前缀调用）。
Module 通用工具

    ''' <summary>上游 Module1.随机字符串生成。</summary>
    Public Function 随机字符串生成(长度 As Integer, Optional 包含数字 As Boolean = True, Optional 包含大写字母 As Boolean = True, Optional 包含小写字母 As Boolean = True) As String
        If 长度 <= 0 Then
            Return ""
            Exit Function
        End If
        If Not 包含数字 AndAlso Not 包含大写字母 AndAlso Not 包含小写字母 Then
            Return ""
            Exit Function
        End If
        Dim numbers As String = "0123456789"
        Dim upperCase As String = "ABCDEFGHIJKLMNOPQRSTUVWXYZ"
        Dim lowerCase As String = "abcdefghijklmnopqrstuvwxyz"
        Dim validChars As New StringBuilder()
        If 包含数字 Then validChars.Append(numbers)
        If 包含大写字母 Then validChars.Append(upperCase)
        If 包含小写字母 Then validChars.Append(lowerCase)
        If validChars.Length = 0 Then
            Return "无效的字符集"
        End If
        Dim rnd As New Random()
        Dim result As New StringBuilder(长度)
        For i As Integer = 1 To 长度
            Dim randomIndex As Integer = rnd.Next(0, validChars.Length)
            result.Append(validChars(randomIndex))
        Next
        Return result.ToString()
    End Function

    ''' <summary>上游 Module1.规范化文件夹路径。</summary>
    Public Function 规范化文件夹路径(路径 As String) As String
        Dim result = If(路径, "").Trim()
        If result = "" Then Return ""
        Try
            Dim root = Path.GetPathRoot(result)
            If root <> "" AndAlso String.Equals(result, root, StringComparison.OrdinalIgnoreCase) AndAlso
               root.Length = 3 AndAlso root(1) = ":"c AndAlso (root(2) = "\"c OrElse root(2) = "/"c) Then
                Return root
            End If
        Catch
        End Try
        Return result.TrimEnd("\"c, "/"c)
    End Function

    ''' <summary>上游 Module1.混淆字符_喵（任务名称混淆）。</summary>
    Public Function 混淆字符_喵(input As String) As String
        Return New String("喵", input.Length)
    End Function

End Module
