using System.Text.Json;
using System.Text.Json.Serialization;

namespace linux3fui.Server;

/// <summary>
/// 宽容枚举转换器：把空字符串 / null / 未知名称回退为枚举默认值（0），数字按原值解析；
/// 写出时与 JsonStringEnumConverter 一致仍为枚举名称。
/// 前端下拉框未选中时提交空字符串，默认转换器会直接抛 JsonException，导致整份预设反序列化失败。
/// </summary>
public sealed class LenientEnumJsonConverter : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) => typeToConvert.IsEnum;

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(typeof(Inner<>).MakeGenericType(typeToConvert))!;

    private sealed class Inner<T> : JsonConverter<T> where T : struct, Enum
    {
        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.String:
                    var text = reader.GetString();
                    if (!string.IsNullOrWhiteSpace(text) && Enum.TryParse<T>(text.Trim(), ignoreCase: true, out var parsed))
                        return parsed;
                    return default;
                case JsonTokenType.Number:
                    return reader.TryGetInt64(out var number) ? (T)Enum.ToObject(typeof(T), number) : default;
                default:
                    return default;
            }
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString());
    }
}
