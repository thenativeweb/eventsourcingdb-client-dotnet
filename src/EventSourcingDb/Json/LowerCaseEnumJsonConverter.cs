namespace EventSourcingDb.Json;

using System;
using System.Text.Json;
using System.Text.Json.Serialization;

public class LowerCaseEnumJsonConverter<T> : JsonConverter<T> where T : struct, Enum
{
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();
        if (Enum.TryParse<T>(value, ignoreCase: true, out var result))
        {
            return result;
        }
        throw new JsonException($"Cannot convert '{value}' to {typeof(T)}.");
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString().ToLowerInvariant());
    }
}
