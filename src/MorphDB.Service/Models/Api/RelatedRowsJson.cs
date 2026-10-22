using System.Text.Json;
using MorphDB.Core.Abstractions;

namespace MorphDB.Service.Models.Api;

/// <summary>
/// Reads the value of an <c>exists</c> condition — <c>{"table": "lines", "column": "order_id", "filter":
/// ["qty:gt:4"]}</c> — into <see cref="RelatedRows"/>, the same from a query string and from a request body.
/// Each related condition is written as a filter parameter is (<c>column:operator:value</c>) or, in a body,
/// as <c>{"column", "operator", "value"}</c>.
/// </summary>
internal static class RelatedRowsJson
{
    private const string Shape =
        "The value of exists is {\"table\": <related table>, \"column\": <its column holding this row's value>, " +
        "\"filter\": [<conditions on the related rows>]}.";

    /// <summary>The related rows written as text in a filter parameter.</summary>
    public static RelatedRows Parse(string expression, string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            return From(document.RootElement);
        }
        catch (JsonException)
        {
            throw new ArgumentException($"Invalid filter format: '{expression}'. {Shape}");
        }
    }

    /// <summary>The related rows written as a JSON object.</summary>
    public static RelatedRows From(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object
            || !value.TryGetProperty("table", out var table) || table.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(table.GetString())
            || !value.TryGetProperty("column", out var column) || column.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(column.GetString()))
        {
            throw new ArgumentException(Shape);
        }

        var where = new List<FilterCondition>();
        if (value.TryGetProperty("filter", out var filter))
        {
            if (filter.ValueKind != JsonValueKind.Array)
            {
                throw new ArgumentException(Shape);
            }

            foreach (var condition in filter.EnumerateArray())
            {
                where.Add(Condition(condition));
            }
        }

        return new RelatedRows(table.GetString()!, column.GetString()!, where);
    }

    /// <summary>A body's value of an <c>exists</c> or <c>notexists</c> condition, or the value as it came for any other operator.</summary>
    public static object? ForOperator(FilterOperator op, object? value) =>
        op.TakesRelatedRows()
            ? value is JsonElement element ? From(element) : value is RelatedRows ? value : throw new ArgumentException(Shape)
            : value;

    private static FilterCondition Condition(JsonElement condition)
    {
        if (condition.ValueKind == JsonValueKind.String)
        {
            var (column, op, value) = FilterExpressions.Parse(condition.GetString()!);
            return new FilterCondition { Column = column, Operator = op, Value = value };
        }

        if (condition.ValueKind == JsonValueKind.Object
            && condition.TryGetProperty("column", out var column2) && column2.ValueKind == JsonValueKind.String
            && condition.TryGetProperty("operator", out var op2) && op2.ValueKind == JsonValueKind.String)
        {
            var op = ApiModelExtensions.ParseFilterOperator(op2.GetString()!);
            var value = condition.TryGetProperty("value", out var v) ? ApiModelExtensions.NormalizeJsonValue(v) : null;
            return new FilterCondition { Column = column2.GetString()!, Operator = op, Value = op.TakesRelatedRows() ? ForOperator(op, v) : value };
        }

        throw new ArgumentException(
            "A condition of exists is a filter text ('qty:gt:4') or an object {\"column\", \"operator\", \"value\"}.");
    }
}
