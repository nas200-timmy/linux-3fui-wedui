Imports System.IO

' 数据目录：替代 Windows 版的 Application.StartupPath。
' 服务端在启动时注入实际数据目录（Docker 中为 /data）。
Public Module 路径助手
    Private _数据目录 As String = AppContext.BaseDirectory

    Public Property 数据目录 As String
        Get
            Return _数据目录
        End Get
        Set(value As String)
            If Not String.IsNullOrWhiteSpace(value) Then
                Try
                    Directory.CreateDirectory(value)
                    _数据目录 = Path.GetFullPath(value)
                Catch
                End Try
            End If
        End Set
    End Property
End Module
