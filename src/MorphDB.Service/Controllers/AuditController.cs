using Microsoft.AspNetCore.Mvc;
using MorphDB.Core.Abstractions;
using MorphDB.Service.Models.Api;

namespace MorphDB.Service.Controllers;

/// <summary>
/// LoggerMessage delegates for AuditController.
/// </summary>
internal static partial class AuditControllerLogs
{
    [LoggerMessage(LogLevel.Information, "Querying audit logs for project {ProjectId}")]
    public static partial void QueryingAuditLogs(ILogger logger, Guid projectId);

    [LoggerMessage(LogLevel.Information, "Getting audit log {LogId} for project {ProjectId}")]
    public static partial void GettingAuditLog(ILogger logger, Guid logId, Guid projectId);

    [LoggerMessage(LogLevel.Information, "Getting audit stats for project {ProjectId}")]
    public static partial void GettingAuditStats(ILogger logger, Guid projectId);
}

/// <summary>
/// REST API controller for querying audit logs.
/// Provides read-only access to audit trail for compliance and security.
/// </summary>
[ApiController]
[Route("api/projects/{projectId:guid}/audit")]
[Produces("application/json")]
public sealed class AuditController : ControllerBase
{
    private readonly IAuditService _auditService;
    private readonly ILogger<AuditController> _logger;

    public AuditController(
        IAuditService auditService,
        ILogger<AuditController> logger)
    {
        _auditService = auditService;
        _logger = logger;
    }

    /// <summary>
    /// Queries audit logs for a project with optional filters.
    /// </summary>
    /// <param name="projectId">The project ID.</param>
    /// <param name="parameters">Query parameters for filtering and pagination.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Paginated audit log entries.</returns>
    [HttpGet("logs")]
    [ProducesResponseType(typeof(AuditLogPageApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> QueryLogs(
        Guid projectId,
        [FromQuery] AuditLogQueryParameters parameters,
        CancellationToken cancellationToken)
    {
        AuditControllerLogs.QueryingAuditLogs(_logger, projectId);

        var query = new AuditLogQuery
        {
            Category = parameters.Category.HasValue
                ? (AuditCategory)parameters.Category.Value
                : null,
            MinSeverity = parameters.MinSeverity.HasValue
                ? (AuditSeverity)parameters.MinSeverity.Value
                : null,
            ActorId = parameters.ActorId,
            ResourceType = parameters.ResourceType,
            ResourceId = parameters.ResourceId,
            Action = parameters.Action,
            From = parameters.From,
            To = parameters.To,
            SearchText = parameters.SearchText,
            Page = Math.Max(1, parameters.Page),
            PageSize = Math.Clamp(parameters.PageSize, 1, 100),
            OrderBy = parameters.OrderBy ?? "timestamp",
            Descending = parameters.Descending
        };

        var page = await _auditService.QueryAsync(projectId, query, cancellationToken);
        return Ok(AuditLogPageApiResponse.FromModel(page));
    }

    /// <summary>
    /// Gets a specific audit log entry by ID.
    /// </summary>
    /// <param name="projectId">The project ID.</param>
    /// <param name="logId">The audit log entry ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The audit log entry.</returns>
    [HttpGet("logs/{logId:guid}")]
    [ProducesResponseType(typeof(AuditLogEntryApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetLog(
        Guid projectId,
        Guid logId,
        CancellationToken cancellationToken)
    {
        AuditControllerLogs.GettingAuditLog(_logger, logId, projectId);

        var entry = await _auditService.GetByIdAsync(projectId, logId, cancellationToken);

        if (entry is null)
        {
            return NotFound(new ErrorResponse
            {
                Error = "AUDIT_LOG_NOT_FOUND",
                Message = $"Audit log entry with ID '{logId}' not found.",
                Code = "AUDIT_LOG_NOT_FOUND"
            });
        }

        return Ok(AuditLogEntryApiResponse.FromModel(entry));
    }

    /// <summary>
    /// Gets audit statistics for a project within a time range.
    /// </summary>
    /// <param name="projectId">The project ID.</param>
    /// <param name="from">Start of time range (optional).</param>
    /// <param name="to">End of time range (optional).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Audit statistics.</returns>
    [HttpGet("stats")]
    [ProducesResponseType(typeof(AuditStatsApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetStats(
        Guid projectId,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        CancellationToken cancellationToken)
    {
        AuditControllerLogs.GettingAuditStats(_logger, projectId);

        var stats = await _auditService.GetStatsAsync(projectId, fromDate: from, toDate: to, cancellationToken);
        return Ok(AuditStatsApiResponse.FromModel(stats));
    }
}

/// <summary>
/// Query parameters for audit log queries.
/// </summary>
public sealed class AuditLogQueryParameters
{
    /// <summary>
    /// Filter by category (0=Auth, 1=Data, 2=Schema, 3=Admin, 4=Security, 5=System).
    /// </summary>
    public int? Category { get; set; }

    /// <summary>
    /// Filter by minimum severity (0=Debug, 1=Info, 2=Warning, 3=Error, 4=Critical).
    /// </summary>
    public int? MinSeverity { get; set; }

    /// <summary>
    /// Filter by actor ID.
    /// </summary>
    public string? ActorId { get; set; }

    /// <summary>
    /// Filter by resource type.
    /// </summary>
    public string? ResourceType { get; set; }

    /// <summary>
    /// Filter by resource ID.
    /// </summary>
    public string? ResourceId { get; set; }

    /// <summary>
    /// Filter by action (supports wildcards).
    /// </summary>
    public string? Action { get; set; }

    /// <summary>
    /// Start of time range.
    /// </summary>
    public DateTimeOffset? From { get; set; }

    /// <summary>
    /// End of time range.
    /// </summary>
    public DateTimeOffset? To { get; set; }

    /// <summary>
    /// Search in metadata.
    /// </summary>
    public string? SearchText { get; set; }

    /// <summary>
    /// Page number (1-based). Default: 1.
    /// </summary>
    public int Page { get; set; } = 1;

    /// <summary>
    /// Page size. Default: 50, Max: 100.
    /// </summary>
    public int PageSize { get; set; } = 50;

    /// <summary>
    /// Order by field. Default: timestamp.
    /// </summary>
    public string? OrderBy { get; set; }

    /// <summary>
    /// Order descending. Default: true.
    /// </summary>
    public bool Descending { get; set; } = true;
}

/// <summary>
/// API response for paginated audit logs.
/// </summary>
public sealed class AuditLogPageApiResponse
{
    public required IReadOnlyList<AuditLogEntryApiResponse> Items { get; init; }
    public int TotalCount { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalPages { get; init; }
    public bool HasMore { get; init; }

    public static AuditLogPageApiResponse FromModel(AuditLogPage model) => new()
    {
        Items = model.Items.Select(AuditLogEntryApiResponse.FromModel).ToList(),
        TotalCount = model.TotalCount,
        Page = model.Page,
        PageSize = model.PageSize,
        TotalPages = model.TotalPages,
        HasMore = model.HasMore
    };
}

/// <summary>
/// API response for a single audit log entry.
/// </summary>
public sealed class AuditLogEntryApiResponse
{
    public Guid Id { get; init; }
    public Guid ProjectId { get; init; }
    public string Category { get; init; } = string.Empty;
    public required string Action { get; init; }
    public string Severity { get; init; } = string.Empty;
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
    public Dictionary<string, object?>? Metadata { get; init; }
    public string? ErrorMessage { get; init; }
    public DateTimeOffset Timestamp { get; init; }

    public static AuditLogEntryApiResponse FromModel(AuditLogEntry model) => new()
    {
        Id = model.Id,
        ProjectId = model.ProjectId,
        Category = model.Category.ToString().ToLowerInvariant(),
        Action = model.Action,
        Severity = model.Severity.ToString().ToLowerInvariant(),
        ActorId = model.ActorId,
        ActorType = model.ActorType,
        ResourceType = model.ResourceType,
        ResourceId = model.ResourceId,
        HttpMethod = model.HttpMethod,
        RequestPath = model.RequestPath,
        StatusCode = model.StatusCode,
        IpAddress = model.IpAddress,
        UserAgent = model.UserAgent,
        DurationMs = model.DurationMs,
        Metadata = model.Metadata,
        ErrorMessage = model.ErrorMessage,
        Timestamp = model.Timestamp
    };
}

/// <summary>
/// API response for audit statistics.
/// </summary>
public sealed class AuditStatsApiResponse
{
    public long TotalEvents { get; init; }
    public required Dictionary<string, long> ByCategory { get; init; }
    public required Dictionary<string, long> BySeverity { get; init; }
    public required IReadOnlyList<ActorStatsApiResponse> TopActors { get; init; }
    public required IReadOnlyList<ActionStatsApiResponse> TopActions { get; init; }
    public double ErrorRate { get; init; }
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }

    public static AuditStatsApiResponse FromModel(AuditStats model) => new()
    {
        TotalEvents = model.TotalEvents,
        ByCategory = model.ByCategory.ToDictionary(
            kvp => kvp.Key.ToString().ToLowerInvariant(),
            kvp => kvp.Value),
        BySeverity = model.BySeverity.ToDictionary(
            kvp => kvp.Key.ToString().ToLowerInvariant(),
            kvp => kvp.Value),
        TopActors = model.TopActors.Select(ActorStatsApiResponse.FromModel).ToList(),
        TopActions = model.TopActions.Select(ActionStatsApiResponse.FromModel).ToList(),
        ErrorRate = model.ErrorRate,
        From = model.From,
        To = model.To
    };
}

/// <summary>
/// API response for actor statistics.
/// </summary>
public sealed class ActorStatsApiResponse
{
    public required string ActorId { get; init; }
    public string? ActorType { get; init; }
    public long EventCount { get; init; }

    public static ActorStatsApiResponse FromModel(ActorStats model) => new()
    {
        ActorId = model.ActorId,
        ActorType = model.ActorType,
        EventCount = model.EventCount
    };
}

/// <summary>
/// API response for action statistics.
/// </summary>
public sealed class ActionStatsApiResponse
{
    public required string Action { get; init; }
    public long EventCount { get; init; }
    public double? AvgDurationMs { get; init; }

    public static ActionStatsApiResponse FromModel(ActionStats model) => new()
    {
        Action = model.Action,
        EventCount = model.EventCount,
        AvgDurationMs = model.AvgDurationMs
    };
}
