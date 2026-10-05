namespace MorphDB.Client;

// The aggregate request as the service reads it. AggregationRequest is shaped for the caller — an
// enum per function, a flag for descending — and the service reads strings, so the client maps one
// to the other before sending. These records are that mapped form. They were once an anonymous
// object, which only a reflection serializer can write; a named type is what lets the request be
// serialized by generated code. Each record's members are in the order the anonymous object
// declared them, and each camel-cases to the name it carried, so the body on the wire is unchanged.

/// <summary>The body of <c>POST /api/data/{table}/aggregate</c>.</summary>
internal sealed record AggregateWireRequest(
    List<AggregateWireColumn> Aggregations,
    IReadOnlyList<string> GroupBy,
    List<AggregateWireFilter>? Filter,
    List<AggregateWireHaving>? Having,
    List<AggregateWireOrder>? OrderBy,
    int? Limit,
    int? Offset);

/// <summary>One aggregate function, with the function named as the service spells it.</summary>
internal sealed record AggregateWireColumn(
    string Function,
    string? Column,
    string Alias,
    bool Distinct,
    int? Limit,
    string? OrderBy);

/// <summary>A row filter, with the operator named as the service spells it.</summary>
internal sealed record AggregateWireFilter(string Column, string Operator, object? Value);

/// <summary>A group filter on an aggregate's alias, with the operator named as the service spells it.</summary>
internal sealed record AggregateWireHaving(string Alias, string Operator, object Value);

/// <summary>An ordering, with its direction as the word the service reads.</summary>
internal sealed record AggregateWireOrder(string Column, string Direction);
