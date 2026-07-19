using System.Text.Json;
using System.Text.Json.Serialization;

namespace MorphDB.Client;

/// <summary>
/// The serializer settings every request and response of this client uses.
/// </summary>
internal static class MorphDBJson
{
    /// <summary>
    /// Web defaults (camelCase, case-insensitive) plus <see cref="ObjectValueConverter"/>, so record
    /// values arrive as .NET values rather than as parser artifacts.
    /// </summary>
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new ObjectValueConverter() },
    };
}

/// <summary>
/// Materializes JSON into .NET values wherever a payload is typed as <see cref="object"/> — the record
/// dictionaries MorphDB returns, whose column types are only known at runtime.
/// <para>
/// Without this, <c>System.Text.Json</c> leaves every such value as a <see cref="JsonElement"/>: a row's
/// text column would not equal the string it holds, and a numeric one could not be cast or converted.
/// Every consumer would have to unwrap the parser's representation itself, which is the client's job.
/// </para>
/// </summary>
internal sealed class ObjectValueConverter : JsonConverter<object>
{
    public override object? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.TokenType switch
        {
            JsonTokenType.Null => null,
            JsonTokenType.True => true,
            JsonTokenType.False => false,
            // Strings stay strings. Sniffing them into Guid or DateTimeOffset would make a value's type
            // depend on its content — a text column holding a date would stop equalling its own string.
            JsonTokenType.String => reader.GetString()!,
            JsonTokenType.Number => ReadNumber(ref reader),
            JsonTokenType.StartObject => ReadObject(ref reader, options),
            JsonTokenType.StartArray => ReadArray(ref reader, options),
            _ => throw new JsonException($"Unexpected token '{reader.TokenType}' while reading a value."),
        };

    // Integers stay integral — a count or an id must not become a double.
    private static object ReadNumber(ref Utf8JsonReader reader)
    {
        if (reader.TryGetInt64(out var integer))
        {
            return integer;
        }

        if (reader.TryGetDecimal(out var exact))
        {
            return exact;
        }

        return reader.GetDouble();
    }

    private Dictionary<string, object?> ReadObject(ref Utf8JsonReader reader, JsonSerializerOptions options)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                return result;
            }

            var name = reader.GetString()!;
            reader.Read();
            result[name] = Read(ref reader, typeof(object), options);
        }

        throw new JsonException("Unexpected end of input while reading an object.");
    }

    private List<object?> ReadArray(ref Utf8JsonReader reader, JsonSerializerOptions options)
    {
        var result = new List<object?>();

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray)
            {
                return result;
            }

            result.Add(Read(ref reader, typeof(object), options));
        }

        throw new JsonException("Unexpected end of input while reading an array.");
    }

    public override void Write(Utf8JsonWriter writer, object value, JsonSerializerOptions options)
    {
        var type = value.GetType();

        // Serializing as `object` again would re-enter this converter forever; nothing carries that
        // static type in practice, so it is a bug rather than a case to handle.
        if (type == typeof(object))
        {
            writer.WriteStartObject();
            writer.WriteEndObject();
            return;
        }

        JsonSerializer.Serialize(writer, value, type, options);
    }
}
