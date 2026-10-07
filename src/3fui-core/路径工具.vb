Imports System.IO

' linux-3fui 适配：上游 Module1 中与路径转换相关的纯逻辑函数（原样移植，模块级以便无前缀调用）。
Module 路径工具
    ''' <summary>上游 Module1.转译模式处理路径（转译模式下把 Windows 路径转成 Unix 风格）。</summary>
    Public Function 转译模式处理路径(p As String) As String
        Dim a = p
        Dim root As String = Path.GetPathRoot(a)
        If Not String.IsNullOrEmpty(root) Then
            a = a.Substring(root.Length)
        End If
        a = a.Replace("\", "/").Replace("//", "/")
        If Not a.StartsWith("/"c) Then a = "/" & a
        Return a
    End Function

    ''' <summary>上游 Module1.将路径转换为FFmpeg滤镜接受的格式（原样移植）。</summary>
    Public Function 将路径转换为FFmpeg滤镜接受的格式(path As String) As String
        If String.IsNullOrEmpty(path) Then
            Return path
        End If
        If path.StartsWith("\\") Then
            Dim pathAfterPrefix As String = path.Substring(2)
            pathAfterPrefix = pathAfterPrefix.Replace("\", "\\")
            Return "\\" & pathAfterPrefix
        End If
        If path.Length >= 2 AndAlso path(1) = ":"c Then
            Dim driveLetter As String = path.Substring(0, 1)
            Dim pathAfterDrive As String = path.Substring(2)
            pathAfterDrive = pathAfterDrive.Replace("\", "\\")
            Return driveLetter & "\:" & pathAfterDrive
        End If
        Return path.Replace("\", "\\")
    End Function
End Module
