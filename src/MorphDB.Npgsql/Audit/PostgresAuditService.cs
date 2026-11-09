using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Dapper;
using Microsoft.Extensions.Logging;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Audit;
using MorphDB.Core.Exceptions;
using MorphDB.Npgsql.Infrastructure;
using Npgsql;
using NpgsqlTypes;

namespace MorphDB.Npgsql.Audit;

/// <summary>
/// PostgreSQL implementation of audit logging service.
/// Uses async queue for non-blocking writes: a request returns once its entry is queued, and a background
/// writer stores it in batches. The recording is therefore best-effort — an entry is lost when the queue
/// overflows (the oldest are discarded), when a batch cannot be written (it is discarded after the failure is
/// logged), or when the process dies with entries still queued. <see cref="AuditMetrics"/> counts the first two.
/// Implements PII masking to protect sensitive data in audit trails.
/// </summary>
public sealed partial class PostgresAuditService : IAuditService, IAsyncDisposable
{
    private readonly MorphConnectionFactory _connections;
    private readonly IPiiMaskingService _piiMaskingService;
    private readonly IProjectRepository _projectRepository;
    private readonly ILogger<PostgresAuditService> _logger;
    private readonly Channel<AuditEvent> _eventQueue;
    private readonly Task _processorTask;
    private readonly CancellationTokenSource _cts = new();

    private const int MaxBatchSize = 100;
    private const int MaxQueueSize = 10000;
    private const long OverflowWarningIntervalMs = 10_000;

    private long _lastOverflowWarningAt = long.MinValue / 2;

    public PostgresAuditService(
        MorphConnectionFactory connections,
        IPiiMaskingService piiMaskingService,
        IProjectRepository projectRepository,
        ILogger<PostgresAuditService> logger)
    {
        _connections = connections;
        _piiMaskingService = piiMaskingService;
        _projectRepository = projectRepository;
        _logger = logger;

        // A full DropOldest queue still accepts every write — it discards the oldest entry instead,
        // so a failed TryWrite never signals overflow. The drop callback is the only place it shows.
        _eventQueue = Channel.CreateBounded<AuditEvent>(
            new BoundedChannelOptions(MaxQueueSize)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false
            },
            OnEventDropped);

        _processorTask = ProcessEventsAsync(_cts.Token);
    }

    /// <summary>The project's recorded system schema holds its audit log — see <see cref="IProjectRepository.GetSchemaNamesAsync"/>.</summary>
    private async Task<SchemaNames> RecordedSchemaNamesAsync(Guid projectId, CancellationToken cancellationToken) =>
        await _projectRepository.GetSchemaNamesAsync(projectId, cancellationToken)
        ?? throw new ProjectNotFoundException(projectId);

    /// <inheritdoc/>
    public async Task LogAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        Enqueue(auditEvent);

        await Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task LogBatchAsync(IEnumerable<AuditEvent> events, CancellationToken cancellationToken = default)
    {
        foreach (var evt in events)
        {
            Enqueue(evt);
        }

        await Task.CompletedTask;
    }

    private void Enqueue(AuditEvent auditEvent)
    {
        // The queue only refuses a write once it is completed, i.e. while the service shuts down.
        if (!_eventQueue.Writer.TryWrite(auditEvent))
        {
            AuditMetrics.RecordDropped(1, AuditMetrics.ServiceStopped);
        }
    }

    private void OnEventDropped(AuditEvent dropped)
    {
        AuditMetrics.RecordDropped(1, AuditMetrics.QueueOverflow);

        // An overloaded service drops thousands of entries a second; the counter has the count, the log
        // only needs to say it is happening.
        var now = Environment.TickCount64;
        var last = Volatile.Read(ref _lastOverflowWarningAt);
        if (now - last >= OverflowWarningIntervalMs
            && Interlocked.CompareExchange(ref _lastOverflowWarningAt, now, last) == last)
        {
            LogQueueFull(_logger);
        }
    }

    /// <inheritdoc/>
    public async Task<AuditLogPage> QueryAsync(
        Guid projectId,
        AuditLogQuery query,
        CancellationToken cancellationToken = default)
    {
        var schemaNames = await RecordedSchemaNamesAsync(projectId, cancellationToken);

        await using var connection = await _connections.OpenSystemAsync(cancellationToken);

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
                "acted_by" AS ActedBy,
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
            ActedBy = r.ActedBy,
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
        var schemaNames = await RecordedSchemaNamesAsync(projectId, cancellationToken);

        await using var connection = await _connections.OpenSystemAsync(cancellationToken);

        var sql = $"""
            SELECT
                "id" AS Id,
                "category" AS Category,
                "action" AS Action,
                "severity" AS Severity,
                "actor_id" AS ActorId,
                "actor_type" AS ActorType,
                "acted_by" AS ActedBy,
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
            ActedBy = row.ActedBy,
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
        var schemaNames = await RecordedSchemaNamesAsync(projectId, cancellationToken);
        var actualFrom = fromDate ?? DateTimeOffset.UtcNow.AddDays(-30);
        var actualTo = toDate ?? DateTimeOffset.UtcNow;

        await using var connection = await _connections.OpenSystemAsync(cancellationToken);

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

    /// <inheritdoc />
    public async Task<int> ApplyRetentionAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var project = await _projectRepository.GetByIdAsync(projectId, cancellationToken);
        var retentionDays = project?.Settings?.AuditLogRetentionDays;

        // No window means keep everything. This is checked before any statement is issued so a
        // project that never asked for retention is never touched by it.
        if (retentionDays is not > 0)
        {
            return 0;
        }

        var schemaNames = await RecordedSchemaNamesAsync(projectId, cancellationToken);
        var cutoff = DateTimeOffset.UtcNow.AddDays(-retentionDays.Value);

        await using var connection = await _connections.OpenSystemAsync(cancellationToken);
        var removed = await connection.ExecuteAsync(
            $"""
            DELETE FROM {QuoteIdentifier(schemaNames.SystemSchema)}."_audit_logs"
            WHERE "timestamp" < @cutoff
            """,
            new { cutoff });

        if (removed > 0)
        {
            LogRetentionApplied(_logger, removed, projectId, retentionDays.Value);
        }

        return removed;
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
                // The batch in hand is gone: the next pass clears it. Failures inside a write are counted there.
                if (batch.Count > 0)
                {
                    AuditMetrics.RecordDropped(batch.Count, AuditMetrics.WriteFailed);
                }

                LogProcessingError(_logger, ex);
                await Task.Delay(1000, cancellationToken);
            }
        }

        // Flush remaining events. The batch still holds what the last pass wrote (or failed to) when the stop
        // arrived between passes; writing it again would store those entries twice.
        batch.Clear();
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
                var schemaNames = await RecordedSchemaNamesAsync(projectId, cancellationToken);

                await using var connection = await _connections.OpenSystemAsync(cancellationToken);

                // The whole group is one statement, so it is stored whole or not at all — an entry the database
                // refuses leaves none of its neighbours behind, and the drop counter counts what was not stored.
                // One round trip and one execution for the group, not one of each per entry.
                await using var command = BuildInsert(connection, schemaNames.SystemSchema, events);
                await command.ExecuteNonQueryAsync(cancellationToken);

                LogBatchWritten(_logger, events.Count, projectId);
            }
            catch (Exception ex)
            {
                AuditMetrics.RecordDropped(events.Count, AuditMetrics.WriteFailed);
                LogWriteError(_logger, projectId, events.Count, ex);
            }
        }
    }

    /// <summary>
    /// One <c>INSERT … SELECT FROM unnest(…)</c> for a group of entries: a column of values per field, so the
    /// statement does not grow with the group and the server plans it once.
    /// </summary>
    private NpgsqlCommand BuildInsert(NpgsqlConnection connection, string systemSchema, List<AuditEvent> events)
    {
        var count = events.Count;
        var category = new short[count];
        var action = new string[count];
        var severity = new short[count];
        var actorId = new string?[count];
        var actorType = new string?[count];
        var actedBy = new string?[count];
        var resourceType = new string?[count];
        var resourceId = new string?[count];
        var httpMethod = new string?[count];
        var requestPath = new string?[count];
        var statusCode = new int?[count];
        var ipAddress = new string?[count];
        var userAgent = new string?[count];
        var durationMs = new long?[count];
        var metadata = new string?[count];
        var errorMessage = new string?[count];
        var timestamp = new DateTimeOffset[count];

        for (var i = 0; i < count; i++)
        {
            var evt = events[i];

            // Apply PII masking to metadata before storage
            var maskedMetadata = _piiMaskingService.MaskMetadata(evt.Metadata);

            category[i] = (short)evt.Category;
            action[i] = evt.Action;
            severity[i] = (short)evt.Severity;
            actorId[i] = evt.ActorId;
            actorType[i] = evt.ActorType;
            actedBy[i] = evt.ActedBy;
            resourceType[i] = evt.ResourceType;
            resourceId[i] = evt.ResourceId;
            httpMethod[i] = evt.HttpMethod;
            requestPath[i] = evt.RequestPath;
            statusCode[i] = evt.StatusCode;
            ipAddress[i] = evt.IpAddress;
            userAgent[i] = evt.UserAgent;
            durationMs[i] = evt.DurationMs;
            metadata[i] = maskedMetadata is not null ? JsonSerializer.Serialize(maskedMetadata) : null;
            errorMessage[i] = evt.ErrorMessage;
            timestamp[i] = evt.Timestamp;
        }

        var command = new NpgsqlCommand($"""
            INSERT INTO {QuoteIdentifier(systemSchema)}."_audit_logs"
            ("category", "action", "severity", "actor_id", "actor_type", "acted_by", "resource_type", "resource_id",
             "http_method", "request_path", "status_code", "ip_address", "user_agent",
             "duration_ms", "metadata", "error_message", "timestamp")
            SELECT "category", "action", "severity", "actor_id", "actor_type", "acted_by", "resource_type", "resource_id",
                   "http_method", "request_path", "status_code", "ip_address", "user_agent",
                   "duration_ms", "metadata"::jsonb, "error_message", "timestamp"
            FROM unnest(@category, @action, @severity, @actorId, @actorType, @actedBy, @resourceType, @resourceId,
                        @httpMethod, @requestPath, @statusCode, @ipAddress, @userAgent,
                        @durationMs, @metadata, @errorMessage, @timestamp)
                 AS t("category", "action", "severity", "actor_id", "actor_type", "acted_by", "resource_type", "resource_id",
                      "http_method", "request_path", "status_code", "ip_address", "user_agent",
                      "duration_ms", "metadata", "error_message", "timestamp")
            """, connection);

        command.Parameters.Add(Column("category", NpgsqlDbType.Smallint, category));
        command.Parameters.Add(Column("action", NpgsqlDbType.Text, action));
        command.Parameters.Add(Column("severity", NpgsqlDbType.Smallint, severity));
        command.Parameters.Add(Column("actorId", NpgsqlDbType.Text, actorId));
        command.Parameters.Add(Column("actorType", NpgsqlDbType.Text, actorType));
        command.Parameters.Add(Column("actedBy", NpgsqlDbType.Text, actedBy));
        command.Parameters.Add(Column("resourceType", NpgsqlDbType.Text, resourceType));
        command.Parameters.Add(Column("resourceId", NpgsqlDbType.Text, resourceId));
        command.Parameters.Add(Column("httpMethod", NpgsqlDbType.Text, httpMethod));
        command.Parameters.Add(Column("requestPath", NpgsqlDbType.Text, requestPath));
        command.Parameters.Add(Column("statusCode", NpgsqlDbType.Integer, statusCode));
        command.Parameters.Add(Column("ipAddress", NpgsqlDbType.Text, ipAddress));
        command.Parameters.Add(Column("userAgent", NpgsqlDbType.Text, userAgent));
        command.Parameters.Add(Column("durationMs", NpgsqlDbType.Bigint, durationMs));
        command.Parameters.Add(Column("metadata", NpgsqlDbType.Text, metadata));
        command.Parameters.Add(Column("errorMessage", NpgsqlDbType.Text, errorMessage));
        command.Parameters.Add(Column("timestamp", NpgsqlDbType.TimestampTz, timestamp));
        return command;
    }

    private static NpgsqlParameter<T[]> Column<T>(string name, NpgsqlDbType elementType, T[] values) =>
        new NpgsqlParameter<T[]>(name, NpgsqlDbType.Array | elementType) { TypedValue = values };

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
        public string? ActedBy { get; init; }
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

    [LoggerMessage(LogLevel.Information,
        "Removed {Count} audit entries for project {ProjectId} past its {RetentionDays}-day window")]
    private static partial void LogRetentionApplied(
        ILogger logger, int count, Guid projectId, int retentionDays);
}
