using System.Text.Json.Serialization;

namespace MorphDB.Client.Models;

/// <summary>
/// What part of the service an audit entry is about. Travels as its name in lower case (<c>"data"</c>).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<AuditCategory>))]
public enum AuditCategory
{
    Auth,
    Data,
    Schema,
    Admin,
    Security,
    System,
}

/// <summary>
/// How serious an audit entry is, least first. Travels as its name in lower case (<c>"warning"</c>).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<AuditSeverity>))]
public enum AuditSeverity
{
    Debug,
    Info,
    Warning,
    Error,
    Critical,
}

/// <summary>
/// What to read from a project's audit log. Every filter left null is not applied.
/// </summary>
public sealed class AuditLogQuery
{
    public AuditCategory? Category { get; init; }

    /// <summary>This severity and the ones above it.</summary>
    public AuditSeverity? MinSeverity { get; init; }
    public string? ActorId { get; init; }
    public string? ResourceType { get; init; }
    public string? ResourceId { get; init; }
    public string? Action { get; init; }
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }

    /// <summary>Text to look for in the entry.</summary>
    public string? SearchText { get; init; }

    /// <summary>From 1. The service's default is 1.</summary>
    public int? Page { get; init; }

    /// <summary>1 to 100. The service's default is 50.</summary>
    public int? PageSize { get; init; }

    /// <summary>The field to order by. The service's default is <c>timestamp</c>.</summary>
    public string? OrderBy { get; init; }

    /// <summary>Newest first unless false. The service's default is true.</summary>
    public bool? Descending { get; init; }
}

/// <summary>
/// One page of a project's audit log.
/// </summary>
public sealed class AuditLogPage
{
    public IReadOnlyList<AuditLogEntry> Items { get; init; } = [];
    public int TotalCount { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalPages { get; init; }
    public bool HasMore { get; init; }
}

/// <summary>
/// One audit log entry.
/// </summary>
public sealed class AuditLogEntry
{
    public Guid Id { get; init; }
    public Guid ProjectId { get; init; }
    public AuditCategory Category { get; init; }
    public required string Action { get; init; }
    public AuditSeverity Severity { get; init; }
    public string? ActorId { get; init; }
    public string? ActorType { get; init; }
    public string? ResourceType { get; init; }
    public string? ResourceId { get; init; }
    public string? HttpMethod { get; init; }
    public string? RequestPath { get; init; }
    public int? StatusCode { get; init; }
    public string? IpAddress { get; init; }
    public string? UserAgent { get; init; }
    public long? DurationMs { get; init; }
    public IDictionary<string, object?>? Metadata { get; init; }
    public string? ErrorMessage { get; init; }
    public DateTimeOffset Timestamp { get; init; }
}

/// <summary>
/// Counts over a project's audit log in a time range.
/// </summary>
public sealed class AuditStats
{
    public long TotalEvents { get; init; }

    /// <summary>Entries per category, keyed by the category's name in lower case.</summary>
    public IReadOnlyDictionary<string, long> ByCategory { get; init; } = new Dictionary<string, long>();

    /// <summary>Entries per severity, keyed by the severity's name in lower case.</summary>
    public IReadOnlyDictionary<string, long> BySeverity { get; init; } = new Dictionary<string, long>();
    public IReadOnlyList<AuditActorStats> TopActors { get; init; } = [];
    public IReadOnlyList<AuditActionStats> TopActions { get; init; } = [];
    public double ErrorRate { get; init; }
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }
}

/// <summary>
/// Entries by one actor.
/// </summary>
public sealed class AuditActorStats
{
    public required string ActorId { get; init; }
    public string? ActorType { get; init; }
    public long EventCount { get; init; }
}

/// <summary>
/// Entries of one action.
/// </summary>
public sealed class AuditActionStats
{
    public required string Action { get; init; }
    public long EventCount { get; init; }
    public double? AvgDurationMs { get; init; }
}

/// <summary>
/// One recorded change to a table's schema.
/// </summary>
public sealed class SchemaChange
{
    public Guid ChangeId { get; init; }
    public Guid TableId { get; init; }

    /// <summary>What was done, such as <c>CreateTable</c> or <c>AddColumn</c>.</summary>
    public required string Operation { get; init; }

    /// <summary>The table's schema version after the change.</summary>
    public int SchemaVersion { get; init; }

    /// <summary>What changed, as the service recorded it.</summary>
    public object? Changes { get; init; }
    public string? PerformedBy { get; init; }
    public DateTimeOffset PerformedAt { get; init; }
}
