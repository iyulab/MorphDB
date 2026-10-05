using System.Collections;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MorphDB.Client.Models;

namespace MorphDB.Client;

/// <summary>
/// The serializer settings every request and response of this client uses.
/// </summary>
internal static class MorphDBJson
{
    /// <summary>
    /// Web defaults (camelCase, case-insensitive) plus <see cref="ObjectValueConverter"/>, so record
    /// values arrive as .NET values rather than as parser artifacts. These are the options of
    /// <see cref="MorphDBJsonContext"/>, so anything that serializes through options rather than
    /// through a type's generated metadata resolves its types from that context too — never by
    /// reflection.
    /// </summary>
    public static JsonSerializerOptions Options => MorphDBJsonContext.Default.Options;
}

/// <summary>
/// Serialization metadata for every type the client sends or receives, generated at compile time.
/// <para>
/// The client used to serialize by reflection, which a trimmed or ahead-of-time compiled host cannot
/// rely on: the members reflection looks for may have been trimmed away, and the code it would emit
/// at runtime cannot be emitted at all. Every request and response goes through this context
/// instead. A type missing here fails at the call that needs it in every host alike, rather than
/// only in the hosts that trim.
/// </para>
/// </summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, Converters = [typeof(ObjectValueConverter)])]
// Record values, and the bodies made of them.
[JsonSerializable(typeof(object))]
[JsonSerializable(typeof(IDictionary<string, object?>))]
[JsonSerializable(typeof(IReadOnlyList<IDictionary<string, object?>>))]
[JsonSerializable(typeof(PagedResponse<DataRecord>))]
[JsonSerializable(typeof(DataRecord))]
[JsonSerializable(typeof(AggregateWireRequest))]
[JsonSerializable(typeof(AggregationResponse))]
// Batch and transactions.
[JsonSerializable(typeof(BatchRequest))]
[JsonSerializable(typeof(BatchResponse))]
[JsonSerializable(typeof(TransactionRequest))]
[JsonSerializable(typeof(TransactionResponse))]
[JsonSerializable(typeof(FinalizeRequest))]
[JsonSerializable(typeof(FinalizeResponse))]
// Bulk import and export.
[JsonSerializable(typeof(ImportJobStatus))]
[JsonSerializable(typeof(ExportJobStatus))]
[JsonSerializable(typeof(CsvExportOptions))]
[JsonSerializable(typeof(JsonExportOptions))]
[JsonSerializable(typeof(XlsxExportOptions))]
// Projects.
[JsonSerializable(typeof(CreateProjectRequest))]
[JsonSerializable(typeof(UpdateProjectRequest))]
[JsonSerializable(typeof(ProjectInfo))]
[JsonSerializable(typeof(PagedResponse<ProjectInfo>))]
[JsonSerializable(typeof(ProjectStats))]
[JsonSerializable(typeof(SchemaHealthReport))]
// Schema.
[JsonSerializable(typeof(List<TableInfo>))]
[JsonSerializable(typeof(TableInfo))]
[JsonSerializable(typeof(CreateTableRequest))]
[JsonSerializable(typeof(AddColumnRequest))]
[JsonSerializable(typeof(AlterColumnRequest))]
[JsonSerializable(typeof(ColumnInfo))]
[JsonSerializable(typeof(CreateRelationRequest))]
[JsonSerializable(typeof(RelationInfo))]
// Views.
[JsonSerializable(typeof(IReadOnlyList<ViewInfo>))]
[JsonSerializable(typeof(ViewInfo))]
[JsonSerializable(typeof(CreateViewRequest))]
// Webhooks.
[JsonSerializable(typeof(List<WebhookInfo>))]
[JsonSerializable(typeof(WebhookInfo))]
[JsonSerializable(typeof(CreateWebhookRequest))]
[JsonSerializable(typeof(List<WebhookDelivery>))]
// The real-time hub: the payloads it pushes, and the table name the client sends to subscribe.
[JsonSerializable(typeof(RealtimeClient.RecordChangedMessage))]
[JsonSerializable(typeof(RealtimeClient.RecordDeletedMessage))]
[JsonSerializable(typeof(string))]
internal sealed partial class MorphDBJsonContext : JsonSerializerContext;

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

    /// <summary>
    /// Writes a value whose static type is <see cref="object"/> — a record's column value, a filter's
    /// operand — by what it is at runtime.
    /// <para>
    /// This used to hand the value back to the serializer under its runtime type, which works only by
    /// reflection. The kinds of value a record holds are few and known, so they are written here
    /// directly, each exactly as the serializer wrote it before: the same date and time formats, an
    /// enum as its underlying number, a byte array as base64. Anything else is refused rather than
    /// guessed at — an arbitrary object has no column to land in, and serializing its public
    /// properties by reflection is precisely what a trimmed host cannot do.
    /// </para>
    /// </summary>
    public override void Write(Utf8JsonWriter writer, object value, JsonSerializerOptions options)
    {
        switch (value)
        {
            case string text:
                writer.WriteStringValue(text);
                return;
            case char character:
                writer.WriteStringValue([character]);
                return;
            case bool flag:
                writer.WriteBooleanValue(flag);
                return;

            // A boxed enum matches none of the integral cases below. The serializer's default wrote its
            // underlying number, and so does this — unsigned when the underlying type is.
            case Enum:
                if (Type.GetTypeCode(Enum.GetUnderlyingType(value.GetType())) == TypeCode.UInt64)
                {
                    writer.WriteNumberValue(Convert.ToUInt64(value, CultureInfo.InvariantCulture));
                }
                else
                {
                    writer.WriteNumberValue(Convert.ToInt64(value, CultureInfo.InvariantCulture));
                }

                return;

            case sbyte number:
                writer.WriteNumberValue(number);
                return;
            case byte number:
                writer.WriteNumberValue(number);
                return;
            case short number:
                writer.WriteNumberValue(number);
                return;
            case ushort number:
                writer.WriteNumberValue(number);
                return;
            case int number:
                writer.WriteNumberValue(number);
                return;
            case uint number:
                writer.WriteNumberValue(number);
                return;
            case long number:
                writer.WriteNumberValue(number);
                return;
            case ulong number:
                writer.WriteNumberValue(number);
                return;
            case float number:
                writer.WriteNumberValue(number);
                return;
            case double number:
                writer.WriteNumberValue(number);
                return;
            case decimal number:
                writer.WriteNumberValue(number);
                return;

            // No writer overload takes these; their invariant text is the JSON number the serializer
            // wrote for them. A non-finite Half is not a JSON number, and the serializer refused it.
            case Int128 number:
                writer.WriteRawValue(number.ToString(CultureInfo.InvariantCulture));
                return;
            case UInt128 number:
                writer.WriteRawValue(number.ToString(CultureInfo.InvariantCulture));
                return;
            case Half number when Half.IsFinite(number):
                writer.WriteRawValue(number.ToString(CultureInfo.InvariantCulture));
                return;
            case Half number:
                // Lets the writer refuse it with the same error it gives a non-finite double.
                writer.WriteNumberValue((double)number);
                return;

            case DateTime instant:
                writer.WriteStringValue(instant);
                return;
            case DateTimeOffset instant:
                writer.WriteStringValue(instant);
                return;
            case DateOnly date:
                writer.WriteStringValue(date.ToString("O", CultureInfo.InvariantCulture));
                return;
            // A time of day is written as the span since midnight — "03:04:05", or with seven fraction
            // digits when it has a fraction — which is how the serializer formatted one.
            case TimeOnly time:
                writer.WriteStringValue(time.ToTimeSpan().ToString("c", CultureInfo.InvariantCulture));
                return;
            case TimeSpan span:
                writer.WriteStringValue(span.ToString("c", CultureInfo.InvariantCulture));
                return;
            case Guid id:
                writer.WriteStringValue(id);
                return;
            case byte[] bytes:
                writer.WriteBase64StringValue(bytes);
                return;

            // A value that is JSON already is written as it is.
            case JsonElement element:
                element.WriteTo(writer);
                return;
            case JsonDocument document:
                document.WriteTo(writer);
                return;
            case JsonNode node:
                node.WriteTo(writer, options);
                return;

            // Dictionaries before sequences: every dictionary is also a sequence of its entries.
            case IDictionary<string, object?> entries:
                WriteObject(writer, entries, options);
                return;
            case IReadOnlyDictionary<string, object?> entries:
                WriteObject(writer, entries, options);
                return;
            case IDictionary entries:
                WriteObject(writer, entries, options);
                return;
            case IEnumerable items:
                writer.WriteStartArray();
                foreach (var item in items)
                {
                    WriteValue(writer, item, options);
                }

                writer.WriteEndArray();
                return;

            // Serializing as `object` again would re-enter this converter forever; nothing carries that
            // static type in practice, so it is a bug rather than a case to handle.
            case var _ when value.GetType() == typeof(object):
                writer.WriteStartObject();
                writer.WriteEndObject();
                return;

            default:
                throw Unsupported(value.GetType(), reason: null);
        }
    }

    private void WriteObject(Utf8JsonWriter writer, IEnumerable<KeyValuePair<string, object?>> entries, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var (name, entry) in entries)
        {
            writer.WritePropertyName(name);
            WriteValue(writer, entry, options);
        }

        writer.WriteEndObject();
    }

    // A dictionary seen only through the non-generic interface — a Dictionary<string, int>, say, which
    // is not an IDictionary<string, object?>. Its keys are names only if they are strings.
    private void WriteObject(Utf8JsonWriter writer, IDictionary entries, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (DictionaryEntry entry in entries)
        {
            if (entry.Key is not string name)
            {
                throw Unsupported(entries.GetType(), "its keys are not strings");
            }

            writer.WritePropertyName(name);
            WriteValue(writer, entry.Value, options);
        }

        writer.WriteEndObject();
    }

    // The serializer writes a null itself and never hands one to a converter, but the nulls inside a
    // dictionary or a list reach this converter's own recursion, so it writes them.
    private void WriteValue(Utf8JsonWriter writer, object? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
        }
        else
        {
            Write(writer, value, options);
        }
    }

    private static NotSupportedException Unsupported(Type type, string? reason) => new(
        $"A value of type '{type.FullName}' cannot be written as a record value"
        + (reason is null ? string.Empty : $": {reason}")
        + ". Record values must be JSON primitives (strings, numbers, booleans, null), dates and times, "
        + "Guids, byte arrays, dictionaries with string keys, lists, or JsonElement/JsonNode values.");
}
