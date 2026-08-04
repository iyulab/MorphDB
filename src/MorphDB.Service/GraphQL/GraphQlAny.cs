using System.Text.Json;

namespace MorphDB.Service.GraphQL;

/// <summary>
/// The boundary between GraphQL's <c>Any</c> scalar and the runtime values the rest of the service
/// works in. A row's shape is not known to the schema — that is the product — so <c>Any</c> is
/// where an arbitrary JSON value crosses, and it crosses as a parsed JSON value in both directions.
/// <para>
/// It lives in one place because the alternative is each resolver deciding for itself how a number
/// or a date coerces, and two doors that coerce differently are two contracts. The REST door reads
/// the same JSON with the same rules; keeping the mapping here is what lets them agree.
/// </para>
/// </summary>
internal static class GraphQlAny
{
    /// <summary>
    /// Column names are the caller's own logical names, so no naming policy is applied — a policy
    /// would rewrite the caller's schema on the way out.
    /// </summary>
    private static readonly JsonSerializerOptions RowOptions = new()
    {
        PropertyNamingPolicy = null,
        DictionaryKeyPolicy = null
    };

    /// <summary>
    /// Reads a row argument into the dictionary the write pipeline takes. A value that is not an
    /// object has no fields, and answering with an empty row lets the pipeline refuse it with a
    /// code the caller can branch on rather than throwing out of the resolver.
    /// </summary>
    public static IDictionary<string, object?> ToRow(JsonElement value)
    {
        var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        if (value.ValueKind is not JsonValueKind.Object)
        {
            return row;
        }

        foreach (var property in value.EnumerateObject())
        {
            row[property.Name] = ToRuntimeValue(property.Value);
        }

        return row;
    }

    /// <summary>
    /// Narrows a parsed JSON value to the CLR type the storage layer expects. Objects and arrays
    /// stay JSON text: a jsonb column holds what the caller sent, and re-materialising it as nested
    /// dictionaries would only have to be re-serialized on the way to the database.
    /// </summary>
    public static object? ToRuntimeValue(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Number when element.TryGetInt32(out var intVal) => intVal,
        JsonValueKind.Number when element.TryGetInt64(out var longVal) => longVal,
        JsonValueKind.Number when element.TryGetDecimal(out var decVal) => decVal,
        JsonValueKind.String when element.TryGetGuid(out var guidVal) => guidVal,
        JsonValueKind.String when element.TryGetDateTimeOffset(out var dateVal) => dateVal,
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Object => JsonSerializer.Serialize(element, RowOptions),
        JsonValueKind.Array => JsonSerializer.Serialize(element, RowOptions),
        _ => element.ToString()
    };

    /// <summary>
    /// Writes a row back out as the JSON value the <c>Any</c> scalar carries.
    /// </summary>
    public static JsonElement FromRow(IDictionary<string, object?> row) =>
        JsonSerializer.SerializeToElement(row, RowOptions);

    public static IReadOnlyList<JsonElement> FromRows(IEnumerable<IDictionary<string, object?>> rows) =>
        rows.Select(FromRow).ToList();
}
