namespace MorphDB.Client.Models;

/// <summary>
/// View information response.
/// </summary>
public sealed class ViewInfo
{
    public Guid Id { get; init; }
    public required string Name { get; init; }
    public required string BaseTable { get; init; }
    public IReadOnlyList<ViewColumnInfo> Columns { get; init; } = [];
    public bool IsMaterialized { get; init; }
    public DateTimeOffset? LastRefreshedAt { get; init; }
    public bool IsStale { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>
/// View column information.
/// </summary>
public sealed class ViewColumnInfo
{
    public required string Name { get; init; }
    public required string DataType { get; init; }
    public bool IsComputed { get; init; }
    public string? Expression { get; init; }
}

/// <summary>
/// Request to create a view.
/// </summary>
public sealed class CreateViewRequest
{
    public required string Name { get; init; }
    public required string BaseTable { get; init; }
    public IReadOnlyList<string>? Columns { get; init; }
    public string? Filter { get; init; }
    public bool IsMaterialized { get; init; }
}
