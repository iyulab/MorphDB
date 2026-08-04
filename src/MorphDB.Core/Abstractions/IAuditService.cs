namespace MorphDB.Core.Abstractions;

/// <summary>
/// Service for capturing and querying audit events.
/// Provides comprehensive audit trail for security and compliance.
/// </summary>
public interface IAuditService
{
    /// <summary>
    /// Logs an audit event asynchronously.
    /// </summary>
    /// <param name="auditEvent">The audit event to log.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task LogAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default);

    /// <summary>
    /// Logs multiple audit events in a batch.
    /// </summary>
    /// <param name="events">The audit events to log.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task LogBatchAsync(IEnumerable<AuditEvent> events, CancellationToken cancellationToken = default);

    /// <summary>
    /// Queries audit logs for a project.
    /// </summary>
    /// <param name="projectId">The project ID.</param>
    /// <param name="query">Query parameters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Paginated audit log entries.</returns>
    Task<AuditLogPage> QueryAsync(
        Guid projectId,
        AuditLogQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a specific audit log entry.
    /// </summary>
    /// <param name="projectId">The project ID.</param>
    /// <param name="logId">The log entry ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The audit log entry, or null if not found.</returns>
    Task<AuditLogEntry?> GetByIdAsync(
        Guid projectId,
        Guid logId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets audit statistics for a project.
    /// </summary>
    /// <param name="projectId">The project ID.</param>
    /// <param name="fromDate">Start of time range.</param>
    /// <param name="toDate">End of time range.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Audit statistics.</returns>
    Task<AuditStats> GetStatsAsync(
        Guid projectId,
        DateTimeOffset? fromDate = null,
        DateTimeOffset? toDate = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes audit entries older than the project's retention window and reports how many were
    /// removed. A project without a configured window keeps everything and nothing is removed.
    /// </summary>
    /// <remarks>
    /// Retention is applied here rather than exposed as a "delete old entries" call, because a
    /// caller cannot reach the audit table and must not have to ask for its upkeep.
    /// </remarks>
    /// <param name="projectId">The project ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of entries removed.</returns>
    Task<int> ApplyRetentionAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// An audit event to be logged.
/// </summary>
public sealed class AuditEvent
{
    /// <summary>
    /// Project ID where the event occurred.
    /// </summary>
    public Guid ProjectId { get; init; }

    /// <summary>
    /// Category of the audit event.
    /// </summary>
    public AuditCategory Category { get; init; }

    /// <summary>
    /// Specific action that was performed.
    /// </summary>
    public required string Action { get; init; }

    /// <summary>
    /// Severity level of the event.
    /// </summary>
    public AuditSeverity Severity { get; init; } = AuditSeverity.Info;

    /// <summary>
    /// Actor who performed the action (user ID, API key ID, or "system").
    /// </summary>
    public string? ActorId { get; init; }

    /// <summary>
    /// Type of actor (user, apikey, system).
    /// </summary>
    public string? ActorType { get; init; }

    /// <summary>
    /// Resource type affected (table, column, record, etc.).
    /// </summary>
    public string? ResourceType { get; init; }

    /// <summary>
    /// Resource identifier affected.
    /// </summary>
    public string? ResourceId { get; init; }

    /// <summary>
    /// HTTP method if applicable.
    /// </summary>
    public string? HttpMethod { get; init; }

    /// <summary>
    /// Request path if applicable.
    /// </summary>
    public string? RequestPath { get; init; }

    /// <summary>
    /// HTTP status code if applicable.
    /// </summary>
    public int? StatusCode { get; init; }

    /// <summary>
    /// Client IP address.
    /// </summary>
    public string? IpAddress { get; init; }

    /// <summary>
    /// User agent string.
    /// </summary>
    public string? UserAgent { get; init; }

    /// <summary>
    /// Duration of the operation in milliseconds.
    /// </summary>
    public long? DurationMs { get; init; }

    /// <summary>
    /// Additional metadata as JSON.
    /// </summary>
    public Dictionary<string, object?>? Metadata { get; init; }

    /// <summary>
    /// Error message if the operation failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Timestamp of the event (defaults to now).
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Audit event category.
/// </summary>
public enum AuditCategory
{
    /// <summary>
    /// Authentication events (login, logout, token refresh).
    /// </summary>
    Auth = 0,

    /// <summary>
    /// Data operations (CRUD on records).
    /// </summary>
    Data = 1,

    /// <summary>
    /// Schema operations (DDL changes).
    /// </summary>
    Schema = 2,

    /// <summary>
    /// Administrative operations.
    /// </summary>
    Admin = 3,

    /// <summary>
    /// Security events (permission changes, policy updates).
    /// </summary>
    Security = 4,

    /// <summary>
    /// System events (startup, shutdown, errors).
    /// </summary>
    System = 5
}

/// <summary>
/// Audit event severity level.
/// </summary>
public enum AuditSeverity
{
    /// <summary>
    /// Debug-level information.
    /// </summary>
    Debug = 0,

    /// <summary>
    /// Informational events.
    /// </summary>
    Info = 1,

    /// <summary>
    /// Warning events.
    /// </summary>
    Warning = 2,

    /// <summary>
    /// Error events.
    /// </summary>
    Error = 3,

    /// <summary>
    /// Critical events requiring immediate attention.
    /// </summary>
    Critical = 4
}

/// <summary>
/// Query parameters for audit logs.
/// </summary>
public sealed class AuditLogQuery
{
    /// <summary>
    /// Filter by category.
    /// </summary>
    public AuditCategory? Category { get; init; }

    /// <summary>
    /// Filter by minimum severity.
    /// </summary>
    public AuditSeverity? MinSeverity { get; init; }

    /// <summary>
    /// Filter by actor ID.
    /// </summary>
    public string? ActorId { get; init; }

    /// <summary>
    /// Filter by resource type.
    /// </summary>
    public string? ResourceType { get; init; }

    /// <summary>
    /// Filter by resource ID.
    /// </summary>
    public string? ResourceId { get; init; }

    /// <summary>
    /// Filter by action (supports wildcards).
    /// </summary>
    public string? Action { get; init; }

    /// <summary>
    /// Start of time range.
    /// </summary>
    public DateTimeOffset? From { get; init; }

    /// <summary>
    /// End of time range.
    /// </summary>
    public DateTimeOffset? To { get; init; }

    /// <summary>
    /// Search in metadata.
    /// </summary>
    public string? SearchText { get; init; }

    /// <summary>
    /// Page number (1-based).
    /// </summary>
    public int Page { get; init; } = 1;

    /// <summary>
    /// Page size.
    /// </summary>
    public int PageSize { get; init; } = 50;

    /// <summary>
    /// Order by field.
    /// </summary>
    public string OrderBy { get; init; } = "timestamp";

    /// <summary>
    /// Order direction.
    /// </summary>
    public bool Descending { get; init; } = true;
}

/// <summary>
/// A page of audit log entries.
/// </summary>
public sealed class AuditLogPage
{
    /// <summary>
    /// The log entries.
    /// </summary>
    public required IReadOnlyList<AuditLogEntry> Items { get; init; }

    /// <summary>
    /// Total count of matching entries.
    /// </summary>
    public int TotalCount { get; init; }

    /// <summary>
    /// Current page number.
    /// </summary>
    public int Page { get; init; }

    /// <summary>
    /// Page size.
    /// </summary>
    public int PageSize { get; init; }

    /// <summary>
    /// Total number of pages.
    /// </summary>
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)TotalCount / PageSize) : 0;

    /// <summary>
    /// Whether there are more pages.
    /// </summary>
    public bool HasMore => Page < TotalPages;
}

/// <summary>
/// An audit log entry.
/// </summary>
public sealed class AuditLogEntry
{
    /// <summary>
    /// Unique identifier for this log entry.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Project ID.
    /// </summary>
    public Guid ProjectId { get; init; }

    /// <summary>
    /// Category of the event.
    /// </summary>
    public AuditCategory Category { get; init; }

    /// <summary>
    /// Action that was performed.
    /// </summary>
    public required string Action { get; init; }

    /// <summary>
    /// Severity level.
    /// </summary>
    public AuditSeverity Severity { get; init; }

    /// <summary>
    /// Actor who performed the action.
    /// </summary>
    public string? ActorId { get; init; }

    /// <summary>
    /// Type of actor.
    /// </summary>
    public string? ActorType { get; init; }

    /// <summary>
    /// Resource type affected.
    /// </summary>
    public string? ResourceType { get; init; }

    /// <summary>
    /// Resource identifier.
    /// </summary>
    public string? ResourceId { get; init; }

    /// <summary>
    /// HTTP method.
    /// </summary>
    public string? HttpMethod { get; init; }

    /// <summary>
    /// Request path.
    /// </summary>
    public string? RequestPath { get; init; }

    /// <summary>
    /// HTTP status code.
    /// </summary>
    public int? StatusCode { get; init; }

    /// <summary>
    /// Client IP address.
    /// </summary>
    public string? IpAddress { get; init; }

    /// <summary>
    /// User agent.
    /// </summary>
    public string? UserAgent { get; init; }

    /// <summary>
    /// Duration in milliseconds.
    /// </summary>
    public long? DurationMs { get; init; }

    /// <summary>
    /// Additional metadata.
    /// </summary>
    public Dictionary<string, object?>? Metadata { get; init; }

    /// <summary>
    /// Error message if failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Timestamp of the event.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; }
}

/// <summary>
/// Audit statistics.
/// </summary>
public sealed class AuditStats
{
    /// <summary>
    /// Total number of events in the time range.
    /// </summary>
    public long TotalEvents { get; init; }

    /// <summary>
    /// Events grouped by category.
    /// </summary>
    public required Dictionary<AuditCategory, long> ByCategory { get; init; }

    /// <summary>
    /// Events grouped by severity.
    /// </summary>
    public required Dictionary<AuditSeverity, long> BySeverity { get; init; }

    /// <summary>
    /// Top actors by event count.
    /// </summary>
    public required IReadOnlyList<ActorStats> TopActors { get; init; }

    /// <summary>
    /// Top actions by event count.
    /// </summary>
    public required IReadOnlyList<ActionStats> TopActions { get; init; }

    /// <summary>
    /// Error rate percentage.
    /// </summary>
    public double ErrorRate { get; init; }

    /// <summary>
    /// Time range start.
    /// </summary>
    public DateTimeOffset From { get; init; }

    /// <summary>
    /// Time range end.
    /// </summary>
    public DateTimeOffset To { get; init; }
}

/// <summary>
/// Statistics for an actor.
/// </summary>
public sealed class ActorStats
{
    /// <summary>
    /// Actor ID.
    /// </summary>
    public required string ActorId { get; init; }

    /// <summary>
    /// Actor type.
    /// </summary>
    public string? ActorType { get; init; }

    /// <summary>
    /// Event count.
    /// </summary>
    public long EventCount { get; init; }
}

/// <summary>
/// Statistics for an action.
/// </summary>
public sealed class ActionStats
{
    /// <summary>
    /// Action name.
    /// </summary>
    public required string Action { get; init; }

    /// <summary>
    /// Event count.
    /// </summary>
    public long EventCount { get; init; }

    /// <summary>
    /// Average duration in milliseconds.
    /// </summary>
    public double? AvgDurationMs { get; init; }
}
