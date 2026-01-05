using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Dapper;
using Microsoft.Extensions.Logging;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Audit;
using Npgsql;

namespace MorphDB.Npgsql.Audit;

/// <summary>
/// PostgreSQL implementation of audit logging service.
/// Uses async queue for non-blocking writes.
/// Implements PII masking to protect sensitive data in audit trails.
/// </summary>
public sealed partial class PostgresAuditService : IAuditService, IAsyncDisposable
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly ISchemaNameResolver _schemaNameResolver;
    private readonly IPiiMaskingService _piiMaskingService;
    private readonly ILogger<PostgresAuditService> _logger;
    private readonly Channel<AuditEvent> _eventQueue;
    private readonly Task _processorTask;
    private readonly CancellationTokenSource _cts = new();

    private const int MaxBatchSize = 100;
    private const int MaxQueueSize = 10000;

    public PostgresAuditService(
        NpgsqlDataSource dataSource,
        ISchemaNameResolver schemaNameResolver,
        IPiiMaskingService piiMaskingService,
        ILogger<PostgresAuditService> logger)
    {
        _dataSource = dataSource;
        _schemaNameResolver = schemaNameResolver;
        _piiMaskingService = piiMaskingService;
        _logger = logger;

        _eventQueue = Channel.CreateBounded<AuditEvent>(new BoundedChannelOptions(MaxQueueSize)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });

        _processorTask = ProcessEventsAsync(_cts.Token);
    }

    /// <inheritdoc/>
    public async Task LogAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        if (!_eventQueue.Writer.TryWrite(auditEvent))
        {
            LogQueueFull(_logger);
        }

        await Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task LogBatchAsync(IEnumerable<AuditEvent> events, CancellationToken cancellationToken = default)
    {
        foreach (var evt in events)
        {
            _eventQueue.Writer.TryWrite(evt);
        }

        await Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task<AuditLogPage> QueryAsync(
        Guid projectId,
        AuditLogQuery query,
        CancellationToken cancellationToken = default)
    {
        var schemaNames = _schemaNameResolver.GetSchemaNames(projectId);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        var (whereClauses, parameters) = BuildWhereClause(query);
        var whereClause = whereClauses.Count > 0 ? $"WHERE {string.Join(" AND ", whereClauses)}" : "";

        var orderDirection = query.Descending ? "DESC" : "ASC";
        var orderColumn = query.OrderBy switch
        {
            "category" => "category",
            "severity" => "severity",
            "action" => "action",
            "actor_id" => "actor_id",
            _ => "timestamp"
        };

        // Get total count
        var countSql = $"""
            SELECT COUNT(*) FROM {QuoteIdentifier(schemaNames.SystemSchema)}."_audit_logs"
            {whereClause}
            """;

        var totalCount = await connection.ExecuteScalarAsync<int>(countSql, parameters);

        // Get page
        var offset = (query.Page - 1) * query.PageSize;
        var dataSql = $"""
            SELECT
                "id" AS Id,
                "category" AS Category,
                "action" AS Action,
                "severity" AS Severity,
                "actor_id" AS ActorId,
                "actor_type" AS ActorType,
                "resource_type" AS ResourceType,
                "resource_id" AS ResourceId,
                "http_method" AS HttpMethod,
                "request_path" AS RequestPath,
                "status_code" AS StatusCode,
                "ip_address" AS IpAddress,
                "user_agent" AS UserAgent,
                "duration_ms" AS DurationMs,
                "metadata" AS MetadataJson,
                "error_message" AS ErrorMessage,
                "timestamp" AS Timestamp
            FROM {QuoteIdentifier(schemaNames.SystemSchema)}."_audit_logs"
            {whereClause}
            ORDER BY "{orderColumn}" {orderDirection}
            LIMIT @limit OFFSET @offset
            """;

        parameters.Add("limit", query.PageSize);
        parameters.Add("offset", offset);

        var rows = await connection.QueryAsync<AuditLogDto>(dataSql, parameters);

        var items = rows.Select(r => new AuditLogEntry
        {
            Id = r.Id,
            ProjectId = projectId,
            Category = (AuditCategory)r.Category,
            Action = r.Action,
            Severity = (AuditSeverity)r.Severity,
            ActorId = r.ActorId,
            ActorType = r.ActorType,
            ResourceType = r.ResourceType,
            ResourceId = r.ResourceId,
            HttpMethod = r.HttpMethod,
            RequestPath = r.RequestPath,
            StatusCode = r.StatusCode,
            IpAddress = r.IpAddress,
            UserAgent = r.UserAgent,
            DurationMs = r.DurationMs,
            Metadata = r.MetadataJson is not null
                ? JsonSerializer.Deserialize<Dictionary<string, object?>>(r.MetadataJson)
                : null,
            ErrorMessage = r.ErrorMessage,
            Timestamp = r.Timestamp
        }).ToList();

        return new AuditLogPage
        {
            Items = items,
            TotalCount = totalCount,
            Page = query.Page,
            PageSize = query.PageSize
        };
    }

    /// <inheritdoc/>
    public async Task<AuditLogEntry?> GetByIdAsync(
        Guid projectId,
        Guid logId,
        CancellationToken cancellationToken = default)
    {
        var schemaNames = _schemaNameResolver.GetSchemaNames(projectId);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        var sql = $"""
            SELECT
                "id" AS Id,
                "category" AS Category,
                "action" AS Action,
                "severity" AS Severity,
                "actor_id" AS ActorId,
                "actor_type" AS ActorType,
                "resource_type" AS ResourceType,
                "resource_id" AS ResourceId,
                "http_method" AS HttpMethod,
                "request_path" AS RequestPath,
                "status_code" AS StatusCode,
                "ip_address" AS IpAddress,
                "user_agent" AS UserAgent,
                "duration_ms" AS DurationMs,
                "metadata" AS MetadataJson,
                "error_message" AS ErrorMessage,
                "timestamp" AS Timestamp
            FROM {QuoteIdentifier(schemaNames.SystemSchema)}."_audit_logs"
            WHERE "id" = @id
            """;

        var row = await connection.QuerySingleOrDefaultAsync<AuditLogDto>(sql, new { id = logId });

        if (row is null)
            return null;

        return new AuditLogEntry
        {
            Id = row.Id,
            ProjectId = projectId,
            Category = (AuditCategory)row.Category,
            Action = row.Action,
            Severity = (AuditSeverity)row.Severity,
            ActorId = row.ActorId,
            ActorType = row.ActorType,
            ResourceType = row.ResourceType,
            ResourceId = row.ResourceId,
            HttpMethod = row.HttpMethod,
            RequestPath = row.RequestPath,
            StatusCode = row.StatusCode,
            IpAddress = row.IpAddress,
            UserAgent = row.UserAgent,
            DurationMs = row.DurationMs,
            Metadata = row.MetadataJson is not null
                ? JsonSerializer.Deserialize<Dictionary<string, object?>>(row.MetadataJson)
                : null,
            ErrorMessage = row.ErrorMessage,
            Timestamp = row.Timestamp
        };
    }

    /// <inheritdoc/>
    public async Task<AuditStats> GetStatsAsync(
        Guid projectId,
        DateTimeOffset? fromDate = null,
        DateTimeOffset? toDate = null,
        CancellationToken cancellationToken = default)
    {
        var schemaNames = _schemaNameResolver.GetSchemaNames(projectId);
        var actualFrom = fromDate ?? DateTimeOffset.UtcNow.AddDays(-30);
        var actualTo = toDate ?? DateTimeOffset.UtcNow;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        // Total and by category/severity
        var statsSql = $"""
            SELECT
                COUNT(*) AS total,
                COUNT(*) FILTER (WHERE "severity" >= 3) AS error_count,
                "category",
                "severity",
                COUNT(*) AS cnt
            FROM {QuoteIdentifier(schemaNames.SystemSchema)}."_audit_logs"
            WHERE "timestamp" BETWEEN @from AND @to
            GROUP BY GROUPING SETS ((), ("category"), ("severity"))
            """;

        var statsRows = await connection.QueryAsync<dynamic>(statsSql, new { from = actualFrom, to = actualTo });

        var byCategory = new Dictionary<AuditCategory, long>();
        var bySeverity = new Dictionary<AuditSeverity, long>();
        long total = 0;
        long errorCount = 0;

        foreach (var row in statsRows)
        {
            if (row.category is null && row.severity is null)
            {
                total = (long)row.total;
                errorCount = (long)row.error_count;
            }
            else if (row.category is not null)
            {
                byCategory[(AuditCategory)(int)row.category] = (long)row.cnt;
            }
            else if (row.severity is not null)
            {
                bySeverity[(AuditSeverity)(int)row.severity] = (long)row.cnt;
            }
        }

        // Top actors
        var actorsSql = $"""
            SELECT "actor_id" AS ActorId, "actor_type" AS ActorType, COUNT(*) AS EventCount
            FROM {QuoteIdentifier(schemaNames.SystemSchema)}."_audit_logs"
            WHERE "timestamp" BETWEEN @from AND @to AND "actor_id" IS NOT NULL
            GROUP BY "actor_id", "actor_type"
            ORDER BY EventCount DESC
            LIMIT 10
            """;

        var topActors = (await connection.QueryAsync<ActorStats>(actorsSql, new { from = actualFrom, to = actualTo })).ToList();

        // Top actions
        var actionsSql = $"""
            SELECT "action" AS Action, COUNT(*) AS EventCount, AVG("duration_ms") AS AvgDurationMs
            FROM {QuoteIdentifier(schemaNames.SystemSchema)}."_audit_logs"
            WHERE "timestamp" BETWEEN @from AND @to
            GROUP BY "action"
            ORDER BY EventCount DESC
            LIMIT 10
            """;

        var topActions = (await connection.QueryAsync<ActionStats>(actionsSql, new { from = actualFrom, to = actualTo })).ToList();

        return new AuditStats
        {
            TotalEvents = total,
            ByCategory = byCategory,
            BySeverity = bySeverity,
            TopActors = topActors,
            TopActions = topActions,
            ErrorRate = total > 0 ? (double)errorCount / total * 100 : 0,
            From = actualFrom,
            To = actualTo
        };
    }

    private async Task ProcessEventsAsync(CancellationToken cancellationToken)
    {
        var batch = new List<AuditEvent>(MaxBatchSize);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                batch.Clear();

                // Wait for first event
                if (await _eventQueue.Reader.WaitToReadAsync(cancellationToken))
                {
                    // Collect batch
                    while (batch.Count < MaxBatchSize && _eventQueue.Reader.TryRead(out var evt))
                    {
                        batch.Add(evt);
                    }

                    if (batch.Count > 0)
                    {
                        await WriteBatchAsync(batch, cancellationToken);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                LogProcessingError(_logger, ex);
                await Task.Delay(1000, cancellationToken);
            }
        }

        // Flush remaining events
        while (_eventQueue.Reader.TryRead(out var evt))
        {
            batch.Add(evt);
        }

        if (batch.Count > 0)
        {
            try
            {
                await WriteBatchAsync(batch, CancellationToken.None);
            }
            catch (Exception ex)
            {
                LogFlushError(_logger, batch.Count, ex);
            }
        }
    }

    private async Task WriteBatchAsync(List<AuditEvent> batch, CancellationToken cancellationToken)
    {
        // Group by project
        var byProject = batch.GroupBy(e => e.ProjectId);

        foreach (var group in byProject)
        {
            var projectId = group.Key;
            var events = group.ToList();

            try
            {
                var schemaNames = _schemaNameResolver.GetSchemaNames(projectId);

                await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

                var sql = $"""
                    INSERT INTO {QuoteIdentifier(schemaNames.SystemSchema)}."_audit_logs"
                    ("category", "action", "severity", "actor_id", "actor_type", "resource_type", "resource_id",
                     "http_method", "request_path", "status_code", "ip_address", "user_agent",
                     "duration_ms", "metadata", "error_message", "timestamp")
                    VALUES
                    (@category, @action, @severity, @actorId, @actorType, @resourceType, @resourceId,
                     @httpMethod, @requestPath, @statusCode, @ipAddress, @userAgent,
                     @durationMs, @metadata::jsonb, @errorMessage, @timestamp)
                    """;

                foreach (var evt in events)
                {
                    // Apply PII masking to metadata before storage
                    var maskedMetadata = _piiMaskingService.MaskMetadata(evt.Metadata);

                    await connection.ExecuteAsync(sql, new
                    {
                        category = (int)evt.Category,
                        action = evt.Action,
                        severity = (int)evt.Severity,
                        actorId = evt.ActorId,
                        actorType = evt.ActorType,
                        resourceType = evt.ResourceType,
                        resourceId = evt.ResourceId,
                        httpMethod = evt.HttpMethod,
                        requestPath = evt.RequestPath,
                        statusCode = evt.StatusCode,
                        ipAddress = evt.IpAddress,
                        userAgent = evt.UserAgent,
                        durationMs = evt.DurationMs,
                        metadata = maskedMetadata is not null ? JsonSerializer.Serialize(maskedMetadata) : null,
                        errorMessage = evt.ErrorMessage,
                        timestamp = evt.Timestamp
                    });
                }

                LogBatchWritten(_logger, events.Count, projectId);
            }
            catch (Exception ex)
            {
                LogWriteError(_logger, projectId, events.Count, ex);
            }
        }
    }

    private static (List<string> clauses, DynamicParameters parameters) BuildWhereClause(AuditLogQuery query)
    {
        var clauses = new List<string>();
        var parameters = new DynamicParameters();

        if (query.Category.HasValue)
        {
            clauses.Add("\"category\" = @category");
            parameters.Add("category", (int)query.Category.Value);
        }

        if (query.MinSeverity.HasValue)
        {
            clauses.Add("\"severity\" >= @minSeverity");
            parameters.Add("minSeverity", (int)query.MinSeverity.Value);
        }

        if (!string.IsNullOrEmpty(query.ActorId))
        {
            clauses.Add("\"actor_id\" = @actorId");
            parameters.Add("actorId", query.ActorId);
        }

        if (!string.IsNullOrEmpty(query.ResourceType))
        {
            clauses.Add("\"resource_type\" = @resourceType");
            parameters.Add("resourceType", query.ResourceType);
        }

        if (!string.IsNullOrEmpty(query.ResourceId))
        {
            clauses.Add("\"resource_id\" = @resourceId");
            parameters.Add("resourceId", query.ResourceId);
        }

        if (!string.IsNullOrEmpty(query.Action))
        {
            if (query.Action.Contains('*'))
            {
                clauses.Add("\"action\" LIKE @action");
                parameters.Add("action", query.Action.Replace("*", "%"));
            }
            else
            {
                clauses.Add("\"action\" = @action");
                parameters.Add("action", query.Action);
            }
        }

        if (query.From.HasValue)
        {
            clauses.Add("\"timestamp\" >= @from");
            parameters.Add("from", query.From.Value);
        }

        if (query.To.HasValue)
        {
            clauses.Add("\"timestamp\" <= @to");
            parameters.Add("to", query.To.Value);
        }

        if (!string.IsNullOrEmpty(query.SearchText))
        {
            clauses.Add("(\"action\" ILIKE @search OR \"error_message\" ILIKE @search OR \"metadata\"::text ILIKE @search)");
            parameters.Add("search", $"%{query.SearchText}%");
        }

        return (clauses, parameters);
    }

    private static string QuoteIdentifier(string identifier)
    {
        return $"\"{identifier.Replace("\"", "\"\"")}\"";
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _eventQueue.Writer.Complete();

        try
        {
            await _processorTask;
        }
        catch (OperationCanceledException)
        {
            // Expected
        }

        _cts.Dispose();
    }

    // DTOs
    private sealed class AuditLogDto
    {
        public Guid Id { get; init; }
        public int Category { get; init; }
        public string Action { get; init; } = default!;
        public int Severity { get; init; }
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
        public string? MetadataJson { get; init; }
        public string? ErrorMessage { get; init; }
        public DateTimeOffset Timestamp { get; init; }
    }

    // LoggerMessage delegates
    [LoggerMessage(LogLevel.Warning, "Audit event queue is full, dropping oldest events")]
    private static partial void LogQueueFull(ILogger logger);

    [LoggerMessage(LogLevel.Error, "Error processing audit events")]
    private static partial void LogProcessingError(ILogger logger, Exception exception);

    [LoggerMessage(LogLevel.Error, "Error flushing {Count} remaining audit events")]
    private static partial void LogFlushError(ILogger logger, int count, Exception exception);

    [LoggerMessage(LogLevel.Debug, "Wrote {Count} audit events for project {ProjectId}")]
    private static partial void LogBatchWritten(ILogger logger, int count, Guid projectId);

    [LoggerMessage(LogLevel.Error, "Failed to write {Count} audit events for project {ProjectId}")]
    private static partial void LogWriteError(ILogger logger, Guid projectId, int count, Exception exception);
}
