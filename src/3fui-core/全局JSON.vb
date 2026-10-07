Imports System.Text.Json

' 对应上游 Module1 中的全局 JsonSO（System.Text.Json 配置）。
Module 全局JSON
    Public JsonSO As New JsonSerializerOptions With {
        .WriteIndented = True,
        .PropertyNamingPolicy = Nothing,
        .DictionaryKeyPolicy = Nothing,
        .Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    }

    Sub New()
        JsonSO.Converters.Add(New System.Text.Json.Serialization.JsonStringEnumConverter())
    End Sub
End Module
