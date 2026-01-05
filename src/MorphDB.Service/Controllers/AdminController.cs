using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Diagnostics;
using MorphDB.Core.Models;
using MorphDB.Service.Infrastructure;
using MorphDB.Service.Models.Api;
using Npgsql;

namespace MorphDB.Service.Controllers;

/// <summary>
/// LoggerMessage delegates for AdminController.
/// </summary>
internal static partial class AdminControllerLogs
{
    [LoggerMessage(LogLevel.Information, "Admin: Getting system status")]
    public static partial void GettingSystemStatus(ILogger logger);

    [LoggerMessage(LogLevel.Information, "Admin: Getting system config")]
    public static partial void GettingSystemConfig(ILogger logger);

    [LoggerMessage(LogLevel.Information, "Admin: Listing tenants")]
    public static partial void ListingTenants(ILogger logger);

    [LoggerMessage(LogLevel.Information, "Admin: Getting tenant usage for {TenantId}")]
    public static partial void GettingTenantUsage(ILogger logger, Guid tenantId);

    [LoggerMessage(LogLevel.Information, "Admin: Getting metrics queries")]
    public static partial void GettingMetricsQueries(ILogger logger);

    [LoggerMessage(LogLevel.Information, "Admin: Getting metrics connections")]
    public static partial void GettingMetricsConnections(ILogger logger);

    [LoggerMessage(LogLevel.Information, "Admin: Getting metrics performance")]
    public static partial void GettingMetricsPerformance(ILogger logger);

    [LoggerMessage(LogLevel.Information, "Admin: Getting schema overview")]
    public static partial void GettingSchemaOverview(ILogger logger);

    [LoggerMessage(LogLevel.Information, "Admin: Getting schema tables for tenant {TenantId}")]
    public static partial void GettingSchemaTables(ILogger logger, Guid tenantId);

    [LoggerMessage(LogLevel.Information, "Admin: Getting activity logs")]
    public static partial void GettingActivityLogs(ILogger logger);

    [LoggerMessage(LogLevel.Error, "Admin operation failed: {Error}")]
    public static partial void AdminOperationFailed(ILogger logger, string error, Exception exception);
}

/// <summary>
/// Administrative API controller for system management and monitoring.
/// Provides system status, configuration, tenant management, and usage metrics.
/// Requires Admin authorization.
/// </summary>
[ApiController]
[Route("api/admin")]
[Produces("application/json")]
[Authorize(Policy = "Admin")]
public sealed class AdminController : ControllerBase
{
    private readonly IProjectRepository _projectRepository;
    private readonly IQuotaService _quotaService;
    private readonly IQueryDiagnostics _queryDiagnostics;
    private readonly ISchemaManager _schemaManager;
    private readonly IAuditService _auditService;
    private readonly GracefulShutdownService _shutdownService;
    private readonly NpgsqlDataSource _dataSource;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AdminController> _logger;

    public AdminController(
        IProjectRepository projectRepository,
        IQuotaService quotaService,
        IQueryDiagnostics queryDiagnostics,
        ISchemaManager schemaManager,
        IAuditService auditService,
        GracefulShutdownService shutdownService,
        NpgsqlDataSource dataSource,
        IConfiguration configuration,
        ILogger<AdminController> logger)
    {
        _projectRepository = projectRepository;
        _quotaService = quotaService;
        _queryDiagnostics = queryDiagnostics;
        _schemaManager = schemaManager;
        _auditService = auditService;
        _shutdownService = shutdownService;
        _dataSource = dataSource;
        _configuration = configuration;
        _logger = logger;
    }

    #region System Endpoints

    /// <summary>
    /// Gets comprehensive system status including database, services, and health.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>System status summary.</returns>
    [HttpGet("system/status")]
    [ProducesResponseType(typeof(SystemStatusResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSystemStatus(CancellationToken cancellationToken)
    {
        AdminControllerLogs.GettingSystemStatus(_logger);

        var status = new SystemStatusResponse
        {
            Version = GetVersion(),
            Environment = GetEnvironmentName(),
            StartedAt = GetProcessStartTime(),
            Uptime = GetUptime(),
            IsShuttingDown = _shutdownService.IsShuttingDown,
            ActiveRequests = _shutdownService.ActiveRequestCount,
            Database = await GetDatabaseStatusAsync(cancellationToken),
            QueryStats = GetQueryStats(),
            Timestamp = DateTimeOffset.UtcNow
        };

        return Ok(status);
    }

    /// <summary>
    /// Gets system configuration (with sensitive values masked).
    /// </summary>
    /// <returns>Masked system configuration.</returns>
    [HttpGet("system/config")]
    [ProducesResponseType(typeof(SystemConfigResponse), StatusCodes.Status200OK)]
    public IActionResult GetSystemConfig()
    {
        AdminControllerLogs.GettingSystemConfig(_logger);

        var config = new SystemConfigResponse
        {
            ConnectionString = MaskConnectionString(_configuration.GetConnectionString("MorphDB")),
            RedisConnectionString = MaskConnectionString(_configuration.GetConnectionString("Redis")),
            EncryptionEnabled = !string.IsNullOrEmpty(_configuration["Encryption:MasterKey"]),
            LogLevel = _configuration["Serilog:MinimumLevel:Default"] ?? "Information",
            RateLimiting = new RateLimitingConfigResponse
            {
                DefaultRps = _configuration.GetValue<int>("RateLimiting:DefaultRps", 100),
                DefaultBurst = _configuration.GetValue<int>("RateLimiting:DefaultBurst", 200)
            },
            GracefulShutdown = new GracefulShutdownConfigResponse
            {
                TimeoutSeconds = _configuration.GetValue<int>("GracefulShutdown:ShutdownTimeoutSeconds", 30),
                RejectNewRequests = _configuration.GetValue<bool>("GracefulShutdown:RejectNewRequestsDuringShutdown", false)
            }
        };

        return Ok(config);
    }

    #endregion

    #region Tenant Endpoints

    /// <summary>
    /// Lists all tenants (projects) with optional status filter.
    /// </summary>
    /// <param name="status">Optional status filter (0=Active, 1=Suspended, 2=Archived, 3=Deleted).</param>
    /// <param name="offset">Pagination offset. Default: 0.</param>
    /// <param name="limit">Maximum results. Default: 50, Max: 200.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of tenants.</returns>
    [HttpGet("tenants")]
    [ProducesResponseType(typeof(TenantListResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListTenants(
        [FromQuery] int? status,
        [FromQuery] int offset = 0,
        [FromQuery] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        AdminControllerLogs.ListingTenants(_logger);

        limit = Math.Clamp(limit, 1, 200);
        offset = Math.Max(0, offset);

        ProjectStatus? projectStatus = status.HasValue
            ? (ProjectStatus)status.Value
            : null;

        var projects = await _projectRepository.ListAsync(
            organizationId: null,
            status: projectStatus,
            offset: offset,
            limit: limit,
            cancellationToken: cancellationToken);

        var totalCount = await _projectRepository.CountAsync(
            organizationId: null,
            status: projectStatus,
            cancellationToken: cancellationToken);

        return Ok(new TenantListResponse
        {
            Items = projects.Select(TenantSummaryResponse.FromProject).ToList(),
            TotalCount = totalCount,
            Offset = offset,
            Limit = limit,
            HasMore = offset + projects.Count < totalCount
        });
    }

    /// <summary>
    /// Gets usage statistics for a specific tenant.
    /// </summary>
    /// <param name="tenantId">The tenant (project) ID.</param>
    /// <param name="period">Optional period (format: yyyy-MM). Defaults to current month.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Tenant usage statistics.</returns>
    [HttpGet("tenants/{tenantId:guid}/usage")]
    [ProducesResponseType(typeof(TenantUsageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTenantUsage(
        Guid tenantId,
        [FromQuery] string? period,
        CancellationToken cancellationToken)
    {
        AdminControllerLogs.GettingTenantUsage(_logger, tenantId);

        var project = await _projectRepository.GetByIdAsync(tenantId, cancellationToken);
        if (project is null)
        {
            return NotFound(new ErrorResponse
            {
                Error = "TENANT_NOT_FOUND",
                Message = $"Tenant with ID '{tenantId}' not found.",
                Code = "TENANT_NOT_FOUND"
            });
        }

        DateTimeOffset? targetPeriod = null;
        if (!string.IsNullOrEmpty(period))
        {
            if (DateTimeOffset.TryParseExact(
                period + "-01",
                "yyyy-MM-dd",
                null,
                System.Globalization.DateTimeStyles.AssumeUniversal,
                out var parsed))
            {
                targetPeriod = parsed;
            }
            else
            {
                return BadRequest(new ErrorResponse
                {
                    Error = "INVALID_PERIOD_FORMAT",
                    Message = "Period must be in format yyyy-MM",
                    Code = "INVALID_PERIOD_FORMAT"
                });
            }
        }

        var usage = await _quotaService.GetUsageAsync(tenantId, targetPeriod, cancellationToken);
        var limits = await _quotaService.GetLimitsAsync(tenantId, cancellationToken);

        return Ok(new TenantUsageResponse
        {
            TenantId = tenantId,
            TenantName = project.Name,
            Status = project.Status.ToString().ToLowerInvariant(),
            Period = usage.Period.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture),
            Usage = new UsageMetricsResponse
            {
                ApiRequests = usage.ApiRequests,
                DataReads = usage.DataReads,
                DataWrites = usage.DataWrites,
                StorageBytes = usage.StorageBytes,
                BandwidthBytes = usage.BandwidthBytes
            },
            Limits = new LimitsMetricsResponse
            {
                MaxApiRequests = limits.MaxApiRequests,
                MaxDataReads = limits.MaxDataReads,
                MaxDataWrites = limits.MaxDataWrites,
                MaxStorageBytes = limits.MaxStorageBytes,
                MaxBandwidthBytes = limits.MaxBandwidthBytes,
                Tier = limits.Tier
            },
            LastUpdated = usage.LastUpdated
        });
    }

    #endregion

    #region Metrics Endpoints

    /// <summary>
    /// Gets detailed query statistics and metrics.
    /// </summary>
    /// <param name="since">Optional start time for statistics period (ISO 8601 format).</param>
    /// <returns>Query metrics including operation breakdown and slow queries.</returns>
    [HttpGet("metrics/queries")]
    [ProducesResponseType(typeof(QueryMetricsResponse), StatusCodes.Status200OK)]
    public IActionResult GetQueryMetrics([FromQuery] DateTimeOffset? since)
    {
        AdminControllerLogs.GettingMetricsQueries(_logger);

        var stats = _queryDiagnostics.GetStatistics(since);
        var slowQueries = _queryDiagnostics.GetRecentSlowQueries(50);

        return Ok(new QueryMetricsResponse
        {
            Summary = new QueryStatsSummary
            {
                TotalQueries = stats.TotalQueries,
                SlowQueries = stats.SlowQueries,
                FailedQueries = stats.FailedQueries,
                AverageDurationMs = Math.Round(stats.AverageDurationMs, 2),
                P95DurationMs = Math.Round(stats.P95DurationMs, 2),
                P99DurationMs = Math.Round(stats.P99DurationMs, 2),
                MaxDurationMs = stats.MaxDurationMs,
                SlowQueryThresholdMs = (long)_queryDiagnostics.SlowQueryThreshold.TotalMilliseconds
            },
            ByOperationType = stats.ByOperationType.ToDictionary(
                kvp => kvp.Key.ToString().ToLowerInvariant(),
                kvp => new OperationMetrics
                {
                    Count = kvp.Value.Count,
                    AverageDurationMs = Math.Round(kvp.Value.AverageDurationMs, 2),
                    TotalRowsAffected = kvp.Value.TotalRowsAffected
                }),
            RecentSlowQueries = slowQueries.Take(10).Select(q => new AdminSlowQueryEntry
            {
                ExecutionId = q.ExecutionId,
                TenantId = q.TenantId,
                TableName = q.TableName,
                OperationType = q.OperationType.ToString().ToLowerInvariant(),
                DurationMs = q.DurationMs,
                RowCount = q.RowCount,
                ExecutedAt = q.ExecutedAt,
                Source = q.Source
            }).ToList(),
            Period = new MetricsPeriod
            {
                Start = stats.PeriodStart,
                End = stats.PeriodEnd
            }
        });
    }

    /// <summary>
    /// Gets connection pool statistics from PostgreSQL.
    /// </summary>
    /// <returns>Connection pool metrics.</returns>
    [HttpGet("metrics/connections")]
    [ProducesResponseType(typeof(ConnectionMetricsResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetConnectionMetrics(CancellationToken cancellationToken)
    {
        AdminControllerLogs.GettingMetricsConnections(_logger);

        var poolMetrics = await GetConnectionPoolMetricsAsync(cancellationToken);

        return Ok(new ConnectionMetricsResponse
        {
            Pool = poolMetrics,
            Timestamp = DateTimeOffset.UtcNow
        });
    }

    /// <summary>
    /// Gets aggregated performance metrics.
    /// </summary>
    /// <returns>Overall performance metrics combining query and connection stats.</returns>
    [HttpGet("metrics/performance")]
    [ProducesResponseType(typeof(PerformanceMetricsResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPerformanceMetrics(CancellationToken cancellationToken)
    {
        AdminControllerLogs.GettingMetricsPerformance(_logger);

        var queryStats = _queryDiagnostics.GetStatistics();
        var connMetrics = await GetConnectionPoolMetricsAsync(cancellationToken);
        var dbStatus = await GetDatabaseStatusAsync(cancellationToken);

        // Calculate rates (queries per second based on uptime)
        var uptime = GetUptime();
        var qps = uptime.TotalSeconds > 0
            ? Math.Round(queryStats.TotalQueries / uptime.TotalSeconds, 2)
            : 0;

        // Calculate error rate
        var errorRate = queryStats.TotalQueries > 0
            ? Math.Round((double)queryStats.FailedQueries / queryStats.TotalQueries * 100, 2)
            : 0;

        // Calculate slow query rate
        var slowRate = queryStats.TotalQueries > 0
            ? Math.Round((double)queryStats.SlowQueries / queryStats.TotalQueries * 100, 2)
            : 0;

        return Ok(new PerformanceMetricsResponse
        {
            QueryPerformance = new QueryPerformanceMetrics
            {
                TotalQueries = queryStats.TotalQueries,
                QueriesPerSecond = qps,
                AverageLatencyMs = Math.Round(queryStats.AverageDurationMs, 2),
                P95LatencyMs = Math.Round(queryStats.P95DurationMs, 2),
                P99LatencyMs = Math.Round(queryStats.P99DurationMs, 2),
                ErrorRate = errorRate,
                SlowQueryRate = slowRate
            },
            ConnectionPool = connMetrics,
            Database = new DatabasePerformanceMetrics
            {
                IsHealthy = dbStatus.IsHealthy,
                LatencyMs = dbStatus.LatencyMs,
                ServerVersion = dbStatus.ServerVersion
            },
            System = new SystemPerformanceMetrics
            {
                Uptime = uptime,
                ActiveRequests = _shutdownService.ActiveRequestCount,
                IsShuttingDown = _shutdownService.IsShuttingDown
            },
            Period = new MetricsPeriod
            {
                Start = queryStats.PeriodStart,
                End = queryStats.PeriodEnd
            },
            Timestamp = DateTimeOffset.UtcNow
        });
    }

    #endregion

    #region Activity & Audit Endpoints

    /// <summary>
    /// Gets cross-tenant activity logs with filtering.
    /// </summary>
    /// <param name="tenantId">Optional filter by tenant ID.</param>
    /// <param name="category">Optional filter by audit category (0=Auth, 1=Data, 2=Schema, 3=Admin, 4=Security, 5=System).</param>
    /// <param name="severity">Optional filter by minimum severity (0=Debug, 1=Info, 2=Warning, 3=Error, 4=Critical).</param>
    /// <param name="from">Optional start time filter (ISO 8601).</param>
    /// <param name="to">Optional end time filter (ISO 8601).</param>
    /// <param name="page">Page number (1-based). Default: 1.</param>
    /// <param name="pageSize">Page size. Default: 50, Max: 200.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Paginated activity logs.</returns>
    [HttpGet("activity/logs")]
    [ProducesResponseType(typeof(AdminActivityLogsResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetActivityLogs(
        [FromQuery] Guid? tenantId,
        [FromQuery] int? category,
        [FromQuery] int? severity,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        AdminControllerLogs.GettingActivityLogs(_logger);

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = new AuditLogQuery
        {
            Category = category.HasValue ? (AuditCategory)category.Value : null,
            MinSeverity = severity.HasValue ? (AuditSeverity)severity.Value : null,
            From = from,
            To = to,
            Page = page,
            PageSize = pageSize
        };

        var allLogs = new List<AuditLogEntry>();
        var totalCount = 0;

        if (tenantId.HasValue)
        {
            // Single tenant query
            var result = await _auditService.QueryAsync(tenantId.Value, query, cancellationToken);
            allLogs.AddRange(result.Items);
            totalCount = result.TotalCount;
        }
        else
        {
            // Cross-tenant query - get active projects first
            var projects = await _projectRepository.ListAsync(
                organizationId: null,
                status: ProjectStatus.Active,
                offset: 0,
                limit: 100,
                cancellationToken: cancellationToken);

            // Query each project and aggregate (limited for performance)
            var projectLogs = new List<(Guid TenantId, string TenantName, AuditLogEntry Entry)>();
            foreach (var project in projects.Take(20))
            {
                var result = await _auditService.QueryAsync(project.ProjectId, query, cancellationToken);
                projectLogs.AddRange(result.Items.Select(e => (project.ProjectId, project.Name, e)));
            }

            // Sort by timestamp and take page
            var orderedLogs = projectLogs
                .OrderByDescending(x => x.Entry.Timestamp)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            allLogs.AddRange(orderedLogs.Select(x => x.Entry));
            totalCount = projectLogs.Count;
        }

        return Ok(new AdminActivityLogsResponse
        {
            Items = allLogs.Select(e => new AdminActivityLogEntry
            {
                Id = e.Id,
                TenantId = e.ProjectId,
                Category = e.Category.ToString().ToLowerInvariant(),
                Action = e.Action,
                Severity = e.Severity.ToString().ToLowerInvariant(),
                ActorId = e.ActorId,
                ActorType = e.ActorType,
                ResourceType = e.ResourceType,
                ResourceId = e.ResourceId,
                HttpMethod = e.HttpMethod,
                RequestPath = e.RequestPath,
                StatusCode = e.StatusCode,
                IpAddress = e.IpAddress,
                DurationMs = e.DurationMs,
                ErrorMessage = e.ErrorMessage,
                Timestamp = e.Timestamp
            }).ToList(),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            HasMore = (page * pageSize) < totalCount
        });
    }

    /// <summary>
    /// Gets cross-tenant activity statistics.
    /// </summary>
    /// <param name="tenantId">Optional filter by tenant ID.</param>
    /// <param name="from">Start of time range (defaults to 24 hours ago).</param>
    /// <param name="to">End of time range (defaults to now).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Activity statistics.</returns>
    [HttpGet("activity/stats")]
    [ProducesResponseType(typeof(AdminActivityStatsResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetActivityStats(
        [FromQuery] Guid? tenantId,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        CancellationToken cancellationToken = default)
    {
        var fromDate = from ?? DateTimeOffset.UtcNow.AddHours(-24);
        var toDate = to ?? DateTimeOffset.UtcNow;

        if (tenantId.HasValue)
        {
            // Single tenant stats
            var stats = await _auditService.GetStatsAsync(tenantId.Value, fromDate, toDate, cancellationToken);
            return Ok(new AdminActivityStatsResponse
            {
                TotalEvents = stats.TotalEvents,
                ByCategory = stats.ByCategory.ToDictionary(
                    kvp => kvp.Key.ToString().ToLowerInvariant(),
                    kvp => kvp.Value),
                BySeverity = stats.BySeverity.ToDictionary(
                    kvp => kvp.Key.ToString().ToLowerInvariant(),
                    kvp => kvp.Value),
                TopActions = stats.TopActions.Select(a => new AdminActionStats
                {
                    Action = a.Action,
                    EventCount = a.EventCount,
                    AvgDurationMs = a.AvgDurationMs
                }).ToList(),
                ErrorRate = Math.Round(stats.ErrorRate, 2),
                From = fromDate,
                To = toDate,
                Timestamp = DateTimeOffset.UtcNow
            });
        }

        // Cross-tenant stats aggregation
        var projects = await _projectRepository.ListAsync(
            organizationId: null,
            status: ProjectStatus.Active,
            offset: 0,
            limit: 100,
            cancellationToken: cancellationToken);

        long totalEvents = 0;
        var byCategory = new Dictionary<string, long>();
        var bySeverity = new Dictionary<string, long>();
        var actionCounts = new Dictionary<string, (long Count, double TotalDurationMs, int DurationCount)>();
        long errorCount = 0;

        foreach (var project in projects.Take(20))
        {
            var stats = await _auditService.GetStatsAsync(project.ProjectId, fromDate, toDate, cancellationToken);
            totalEvents += stats.TotalEvents;
            errorCount += (long)(stats.TotalEvents * stats.ErrorRate / 100);

            foreach (var (cat, count) in stats.ByCategory)
            {
                var key = cat.ToString().ToLowerInvariant();
                byCategory[key] = byCategory.GetValueOrDefault(key) + count;
            }

            foreach (var (sev, count) in stats.BySeverity)
            {
                var key = sev.ToString().ToLowerInvariant();
                bySeverity[key] = bySeverity.GetValueOrDefault(key) + count;
            }

            foreach (var action in stats.TopActions)
            {
                var existing = actionCounts.GetValueOrDefault(action.Action, (0, 0, 0));
                actionCounts[action.Action] = (
                    existing.Count + action.EventCount,
                    existing.TotalDurationMs + (action.AvgDurationMs ?? 0) * action.EventCount,
                    existing.DurationCount + (action.AvgDurationMs.HasValue ? (int)action.EventCount : 0)
                );
            }
        }

        return Ok(new AdminActivityStatsResponse
        {
            TotalEvents = totalEvents,
            ByCategory = byCategory,
            BySeverity = bySeverity,
            TopActions = actionCounts
                .OrderByDescending(kvp => kvp.Value.Count)
                .Take(10)
                .Select(kvp => new AdminActionStats
                {
                    Action = kvp.Key,
                    EventCount = kvp.Value.Count,
                    AvgDurationMs = kvp.Value.DurationCount > 0
                        ? Math.Round(kvp.Value.TotalDurationMs / kvp.Value.DurationCount, 2)
                        : null
                }).ToList(),
            ErrorRate = totalEvents > 0 ? Math.Round((double)errorCount / totalEvents * 100, 2) : 0,
            From = fromDate,
            To = toDate,
            Timestamp = DateTimeOffset.UtcNow
        });
    }

    #endregion

    #region Schema Endpoints

    /// <summary>
    /// Gets cross-tenant schema overview with aggregate statistics.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Schema overview with aggregate statistics.</returns>
    [HttpGet("schema/overview")]
    [ProducesResponseType(typeof(SchemaOverviewResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSchemaOverview(CancellationToken cancellationToken)
    {
        AdminControllerLogs.GettingSchemaOverview(_logger);

        var projects = await _projectRepository.ListAsync(
            organizationId: null,
            status: ProjectStatus.Active,
            offset: 0,
            limit: 1000,
            cancellationToken: cancellationToken);

        var tenantStats = new List<TenantSchemaStats>();
        long totalTables = 0;
        long totalColumns = 0;
        long totalRelations = 0;
        long totalIndexes = 0;

        foreach (var project in projects)
        {
            var tables = await _schemaManager.ListTablesAsync(project.ProjectId, cancellationToken);
            var tableCount = tables.Count;
            var columnCount = tables.Sum(t => t.Columns.Count);
            var relationCount = tables.Sum(t => t.Relations.Count);
            var indexCount = tables.Sum(t => t.Indexes.Count);

            totalTables += tableCount;
            totalColumns += columnCount;
            totalRelations += relationCount;
            totalIndexes += indexCount;

            if (tableCount > 0)
            {
                tenantStats.Add(new TenantSchemaStats
                {
                    TenantId = project.ProjectId,
                    TenantName = project.Name,
                    TableCount = tableCount,
                    ColumnCount = columnCount,
                    RelationCount = relationCount,
                    IndexCount = indexCount
                });
            }
        }

        return Ok(new SchemaOverviewResponse
        {
            TotalTenants = projects.Count,
            TenantsWithTables = tenantStats.Count,
            TotalTables = (int)totalTables,
            TotalColumns = (int)totalColumns,
            TotalRelations = (int)totalRelations,
            TotalIndexes = (int)totalIndexes,
            TopTenantsByTables = tenantStats.OrderByDescending(t => t.TableCount).Take(10).ToList(),
            Timestamp = DateTimeOffset.UtcNow
        });
    }

    /// <summary>
    /// Gets schema tables for a specific tenant with logical-physical mapping.
    /// </summary>
    /// <param name="tenantId">The tenant (project) ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Tenant schema with tables, columns, and mappings.</returns>
    [HttpGet("schema/tenants/{tenantId:guid}/tables")]
    [ProducesResponseType(typeof(TenantSchemaResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTenantSchemaTables(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        AdminControllerLogs.GettingSchemaTables(_logger, tenantId);

        var project = await _projectRepository.GetByIdAsync(tenantId, cancellationToken);
        if (project is null)
        {
            return NotFound(new ErrorResponse
            {
                Error = "TENANT_NOT_FOUND",
                Message = $"Tenant with ID '{tenantId}' not found.",
                Code = "TENANT_NOT_FOUND"
            });
        }

        var tables = await _schemaManager.ListTablesAsync(tenantId, cancellationToken);

        return Ok(new TenantSchemaResponse
        {
            TenantId = tenantId,
            TenantName = project.Name,
            Tables = tables.Select(t => new AdminTableInfo
            {
                TableId = t.TableId,
                LogicalName = t.LogicalName,
                PhysicalName = t.PhysicalName,
                SchemaVersion = t.SchemaVersion,
                IsActive = t.IsActive,
                CreatedAt = t.CreatedAt,
                UpdatedAt = t.UpdatedAt,
                Columns = t.Columns.Select(c => new AdminColumnInfo
                {
                    ColumnId = c.ColumnId,
                    LogicalName = c.LogicalName,
                    PhysicalName = c.PhysicalName,
                    DataType = c.DataType.ToString().ToLowerInvariant(),
                    IsNullable = c.IsNullable,
                    IsUnique = c.IsUnique,
                    IsPrimaryKey = c.IsPrimaryKey,
                    IsSystemColumn = c.IsSystemColumn,
                    IsActive = c.IsActive
                }).ToList(),
                Relations = t.Relations.Select(r => new AdminRelationInfo
                {
                    RelationId = r.RelationId,
                    LogicalName = r.LogicalName,
                    SourceTableId = r.SourceTableId,
                    SourceColumnId = r.SourceColumnId,
                    TargetTableId = r.TargetTableId,
                    TargetColumnId = r.TargetColumnId,
                    RelationType = r.RelationType.ToString().ToLowerInvariant(),
                    OnDelete = r.OnDelete.ToString().ToLowerInvariant()
                }).ToList(),
                Indexes = t.Indexes.Select(i => new AdminIndexInfo
                {
                    IndexId = i.IndexId,
                    LogicalName = i.LogicalName,
                    PhysicalName = i.PhysicalName,
                    IndexType = i.IndexType.ToString().ToLowerInvariant(),
                    IsUnique = i.IsUnique,
                    Columns = i.Columns.Select(c => new AdminIndexColumnInfo
                    {
                        ColumnId = c.ColumnId,
                        LogicalName = c.LogicalName,
                        PhysicalName = c.PhysicalName
                    }).ToList()
                }).ToList()
            }).ToList(),
            Timestamp = DateTimeOffset.UtcNow
        });
    }

    #endregion

    #region Private Helpers

    private static string GetVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version;
        return version?.ToString() ?? "1.0.0";
    }

    private string GetEnvironmentName()
    {
        return _configuration["ASPNETCORE_ENVIRONMENT"]
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            ?? "Production";
    }

    private static DateTimeOffset GetProcessStartTime()
    {
        return new DateTimeOffset(System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime());
    }

    private static TimeSpan GetUptime()
    {
        return DateTimeOffset.UtcNow - GetProcessStartTime();
    }

    private async Task<ConnectionPoolMetrics> GetConnectionPoolMetricsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken);
            await using var cmd = conn.CreateCommand();

            // Query PostgreSQL for connection statistics from the application's perspective
            cmd.CommandText = @"
                SELECT
                    count(*) FILTER (WHERE state IS NOT NULL) as total_connections,
                    count(*) FILTER (WHERE state = 'idle') as idle_connections,
                    count(*) FILTER (WHERE state = 'active') as active_connections,
                    count(*) FILTER (WHERE state = 'idle in transaction') as idle_in_transaction
                FROM pg_stat_activity
                WHERE pid <> pg_backend_pid()
                  AND datname = current_database()";

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                var total = reader.GetInt64(0);
                var idle = reader.GetInt64(1);
                var active = reader.GetInt64(2);
                var idleInTx = reader.GetInt64(3);

                return new ConnectionPoolMetrics
                {
                    TotalConnections = (int)total,
                    IdleConnections = (int)idle,
                    BusyConnections = (int)(active + idleInTx),
                    Utilization = total > 0
                        ? Math.Round((double)(active + idleInTx) / total * 100, 2)
                        : 0
                };
            }
        }
        catch (Exception ex)
        {
            AdminControllerLogs.AdminOperationFailed(_logger, "Failed to get connection pool metrics", ex);
        }

        return new ConnectionPoolMetrics
        {
            TotalConnections = 0,
            IdleConnections = 0,
            BusyConnections = 0,
            Utilization = 0
        };
    }

    private async Task<DatabaseStatusResponse> GetDatabaseStatusAsync(CancellationToken cancellationToken)
    {
        var isHealthy = true;
        var latencyMs = 0L;
        string? error = null;
        string? serverVersion = null;

        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT version()";
            serverVersion = (await cmd.ExecuteScalarAsync(cancellationToken))?.ToString();
            sw.Stop();
            latencyMs = sw.ElapsedMilliseconds;
        }
        catch (Exception ex)
        {
            isHealthy = false;
            error = ex.Message;
        }

        return new DatabaseStatusResponse
        {
            IsHealthy = isHealthy,
            LatencyMs = latencyMs,
            ServerVersion = serverVersion,
            ErrorMessage = error
        };
    }

    private QueryStatsResponse GetQueryStats()
    {
        var stats = _queryDiagnostics.GetStatistics();
        return new QueryStatsResponse
        {
            TotalQueries = stats.TotalQueries,
            SlowQueries = stats.SlowQueries,
            FailedQueries = stats.FailedQueries,
            AverageDurationMs = Math.Round(stats.AverageDurationMs, 2),
            P95DurationMs = Math.Round(stats.P95DurationMs, 2),
            P99DurationMs = Math.Round(stats.P99DurationMs, 2),
            SlowQueryThresholdMs = (long)_queryDiagnostics.SlowQueryThreshold.TotalMilliseconds
        };
    }

    private static string? MaskConnectionString(string? connectionString)
    {
        if (string.IsNullOrEmpty(connectionString))
            return null;

        try
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString)
            {
                Password = "***"
            };
            return builder.ToString();
        }
        catch
        {
            return "***";
        }
    }

    #endregion
}

#region Response Models

/// <summary>
/// System status response.
/// </summary>
public sealed class SystemStatusResponse
{
    public required string Version { get; init; }
    public required string Environment { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public TimeSpan Uptime { get; init; }
    public bool IsShuttingDown { get; init; }
    public int ActiveRequests { get; init; }
    public required DatabaseStatusResponse Database { get; init; }
    public required QueryStatsResponse QueryStats { get; init; }
    public DateTimeOffset Timestamp { get; init; }
}

/// <summary>
/// Database status response.
/// </summary>
public sealed class DatabaseStatusResponse
{
    public bool IsHealthy { get; init; }
    public long LatencyMs { get; init; }
    public string? ServerVersion { get; init; }
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Query statistics response.
/// </summary>
public sealed class QueryStatsResponse
{
    public long TotalQueries { get; init; }
    public long SlowQueries { get; init; }
    public long FailedQueries { get; init; }
    public double AverageDurationMs { get; init; }
    public double P95DurationMs { get; init; }
    public double P99DurationMs { get; init; }
    public long SlowQueryThresholdMs { get; init; }
}

/// <summary>
/// System configuration response (with sensitive values masked).
/// </summary>
public sealed class SystemConfigResponse
{
    public string? ConnectionString { get; init; }
    public string? RedisConnectionString { get; init; }
    public bool EncryptionEnabled { get; init; }
    public required string LogLevel { get; init; }
    public required RateLimitingConfigResponse RateLimiting { get; init; }
    public required GracefulShutdownConfigResponse GracefulShutdown { get; init; }
}

/// <summary>
/// Rate limiting configuration response.
/// </summary>
public sealed class RateLimitingConfigResponse
{
    public int DefaultRps { get; init; }
    public int DefaultBurst { get; init; }
}

/// <summary>
/// Graceful shutdown configuration response.
/// </summary>
public sealed class GracefulShutdownConfigResponse
{
    public int TimeoutSeconds { get; init; }
    public bool RejectNewRequests { get; init; }
}

/// <summary>
/// Tenant list response.
/// </summary>
public sealed class TenantListResponse
{
    public required IReadOnlyList<TenantSummaryResponse> Items { get; init; }
    public int TotalCount { get; init; }
    public int Offset { get; init; }
    public int Limit { get; init; }
    public bool HasMore { get; init; }
}

/// <summary>
/// Tenant summary response.
/// </summary>
public sealed class TenantSummaryResponse
{
    public Guid Id { get; init; }
    public required string Name { get; init; }
    public required string Slug { get; init; }
    public required string Status { get; init; }
    public Guid? OrganizationId { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }

    public static TenantSummaryResponse FromProject(Project project) => new()
    {
        Id = project.ProjectId,
        Name = project.Name,
        Slug = project.Slug,
        Status = project.Status.ToString().ToLowerInvariant(),
        OrganizationId = project.OrganizationId,
        CreatedAt = project.CreatedAt,
        UpdatedAt = project.UpdatedAt
    };
}

/// <summary>
/// Tenant usage response.
/// </summary>
public sealed class TenantUsageResponse
{
    public Guid TenantId { get; init; }
    public required string TenantName { get; init; }
    public required string Status { get; init; }
    public required string Period { get; init; }
    public required UsageMetricsResponse Usage { get; init; }
    public required LimitsMetricsResponse Limits { get; init; }
    public DateTimeOffset LastUpdated { get; init; }
}

/// <summary>
/// Usage metrics response.
/// </summary>
public sealed class UsageMetricsResponse
{
    public long ApiRequests { get; init; }
    public long DataReads { get; init; }
    public long DataWrites { get; init; }
    public long StorageBytes { get; init; }
    public long BandwidthBytes { get; init; }
}

/// <summary>
/// Limits metrics response.
/// </summary>
public sealed class LimitsMetricsResponse
{
    public long MaxApiRequests { get; init; }
    public long MaxDataReads { get; init; }
    public long MaxDataWrites { get; init; }
    public long MaxStorageBytes { get; init; }
    public long MaxBandwidthBytes { get; init; }
    public required string Tier { get; init; }
}

/// <summary>
/// Query metrics response with detailed breakdown.
/// </summary>
public sealed class QueryMetricsResponse
{
    public required QueryStatsSummary Summary { get; init; }
    public required IReadOnlyDictionary<string, OperationMetrics> ByOperationType { get; init; }
    public required IReadOnlyList<AdminSlowQueryEntry> RecentSlowQueries { get; init; }
    public required MetricsPeriod Period { get; init; }
}

/// <summary>
/// Query statistics summary.
/// </summary>
public sealed class QueryStatsSummary
{
    public long TotalQueries { get; init; }
    public long SlowQueries { get; init; }
    public long FailedQueries { get; init; }
    public double AverageDurationMs { get; init; }
    public double P95DurationMs { get; init; }
    public double P99DurationMs { get; init; }
    public long MaxDurationMs { get; init; }
    public long SlowQueryThresholdMs { get; init; }
}

/// <summary>
/// Operation-specific metrics.
/// </summary>
public sealed class OperationMetrics
{
    public long Count { get; init; }
    public double AverageDurationMs { get; init; }
    public long TotalRowsAffected { get; init; }
}

/// <summary>
/// Slow query entry for recent slow queries list (Admin API).
/// </summary>
public sealed class AdminSlowQueryEntry
{
    public Guid ExecutionId { get; init; }
    public Guid? TenantId { get; init; }
    public string? TableName { get; init; }
    public required string OperationType { get; init; }
    public long DurationMs { get; init; }
    public int RowCount { get; init; }
    public DateTimeOffset ExecutedAt { get; init; }
    public string? Source { get; init; }
}

/// <summary>
/// Metrics time period.
/// </summary>
public sealed class MetricsPeriod
{
    public DateTimeOffset Start { get; init; }
    public DateTimeOffset End { get; init; }
}

/// <summary>
/// Connection pool metrics response.
/// </summary>
public sealed class ConnectionMetricsResponse
{
    public required ConnectionPoolMetrics Pool { get; init; }
    public DateTimeOffset Timestamp { get; init; }
}

/// <summary>
/// Connection pool statistics.
/// </summary>
public sealed class ConnectionPoolMetrics
{
    public int TotalConnections { get; init; }
    public int IdleConnections { get; init; }
    public int BusyConnections { get; init; }
    public double Utilization { get; init; }
}

/// <summary>
/// Aggregated performance metrics response.
/// </summary>
public sealed class PerformanceMetricsResponse
{
    public required QueryPerformanceMetrics QueryPerformance { get; init; }
    public required ConnectionPoolMetrics ConnectionPool { get; init; }
    public required DatabasePerformanceMetrics Database { get; init; }
    public required SystemPerformanceMetrics System { get; init; }
    public required MetricsPeriod Period { get; init; }
    public DateTimeOffset Timestamp { get; init; }
}

/// <summary>
/// Query performance metrics.
/// </summary>
public sealed class QueryPerformanceMetrics
{
    public long TotalQueries { get; init; }
    public double QueriesPerSecond { get; init; }
    public double AverageLatencyMs { get; init; }
    public double P95LatencyMs { get; init; }
    public double P99LatencyMs { get; init; }
    public double ErrorRate { get; init; }
    public double SlowQueryRate { get; init; }
}

/// <summary>
/// Database performance metrics.
/// </summary>
public sealed class DatabasePerformanceMetrics
{
    public bool IsHealthy { get; init; }
    public long LatencyMs { get; init; }
    public string? ServerVersion { get; init; }
}

/// <summary>
/// System performance metrics.
/// </summary>
public sealed class SystemPerformanceMetrics
{
    public TimeSpan Uptime { get; init; }
    public int ActiveRequests { get; init; }
    public bool IsShuttingDown { get; init; }
}

/// <summary>
/// Schema overview response with aggregate statistics.
/// </summary>
public sealed class SchemaOverviewResponse
{
    public int TotalTenants { get; init; }
    public int TenantsWithTables { get; init; }
    public int TotalTables { get; init; }
    public int TotalColumns { get; init; }
    public int TotalRelations { get; init; }
    public int TotalIndexes { get; init; }
    public required IReadOnlyList<TenantSchemaStats> TopTenantsByTables { get; init; }
    public DateTimeOffset Timestamp { get; init; }
}

/// <summary>
/// Tenant schema statistics summary.
/// </summary>
public sealed class TenantSchemaStats
{
    public Guid TenantId { get; init; }
    public required string TenantName { get; init; }
    public int TableCount { get; init; }
    public int ColumnCount { get; init; }
    public int RelationCount { get; init; }
    public int IndexCount { get; init; }
}

/// <summary>
/// Tenant schema response with detailed table information.
/// </summary>
public sealed class TenantSchemaResponse
{
    public Guid TenantId { get; init; }
    public required string TenantName { get; init; }
    public required IReadOnlyList<AdminTableInfo> Tables { get; init; }
    public DateTimeOffset Timestamp { get; init; }
}

/// <summary>
/// Admin table information with logical-physical mapping.
/// </summary>
public sealed class AdminTableInfo
{
    public Guid TableId { get; init; }
    public required string LogicalName { get; init; }
    public required string PhysicalName { get; init; }
    public int SchemaVersion { get; init; }
    public bool IsActive { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public required IReadOnlyList<AdminColumnInfo> Columns { get; init; }
    public required IReadOnlyList<AdminRelationInfo> Relations { get; init; }
    public required IReadOnlyList<AdminIndexInfo> Indexes { get; init; }
}

/// <summary>
/// Admin column information with logical-physical mapping.
/// </summary>
public sealed class AdminColumnInfo
{
    public Guid ColumnId { get; init; }
    public required string LogicalName { get; init; }
    public required string PhysicalName { get; init; }
    public required string DataType { get; init; }
    public bool IsNullable { get; init; }
    public bool IsUnique { get; init; }
    public bool IsPrimaryKey { get; init; }
    public bool IsSystemColumn { get; init; }
    public bool IsActive { get; init; }
}

/// <summary>
/// Admin relation information.
/// </summary>
public sealed class AdminRelationInfo
{
    public Guid RelationId { get; init; }
    public required string LogicalName { get; init; }
    public Guid SourceTableId { get; init; }
    public Guid SourceColumnId { get; init; }
    public Guid TargetTableId { get; init; }
    public Guid TargetColumnId { get; init; }
    public required string RelationType { get; init; }
    public required string OnDelete { get; init; }
}

/// <summary>
/// Admin index information with logical-physical mapping.
/// </summary>
public sealed class AdminIndexInfo
{
    public Guid IndexId { get; init; }
    public required string LogicalName { get; init; }
    public required string PhysicalName { get; init; }
    public required string IndexType { get; init; }
    public bool IsUnique { get; init; }
    public required IReadOnlyList<AdminIndexColumnInfo> Columns { get; init; }
}

/// <summary>
/// Admin index column information.
/// </summary>
public sealed class AdminIndexColumnInfo
{
    public Guid ColumnId { get; init; }
    public required string LogicalName { get; init; }
    public required string PhysicalName { get; init; }
}

/// <summary>
/// Admin activity logs response.
/// </summary>
public sealed class AdminActivityLogsResponse
{
    public required IReadOnlyList<AdminActivityLogEntry> Items { get; init; }
    public int TotalCount { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
    public bool HasMore { get; init; }
}

/// <summary>
/// Admin activity log entry.
/// </summary>
public sealed class AdminActivityLogEntry
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }
    public required string Category { get; init; }
    public required string Action { get; init; }
    public required string Severity { get; init; }
    public string? ActorId { get; init; }
    public string? ActorType { get; init; }
    public string? ResourceType { get; init; }
    public string? ResourceId { get; init; }
    public string? HttpMethod { get; init; }
    public string? RequestPath { get; init; }
    public int? StatusCode { get; init; }
    public string? IpAddress { get; init; }
    public long? DurationMs { get; init; }
    public string? ErrorMessage { get; init; }
    public DateTimeOffset Timestamp { get; init; }
}

/// <summary>
/// Admin activity statistics response.
/// </summary>
public sealed class AdminActivityStatsResponse
{
    public long TotalEvents { get; init; }
    public required IReadOnlyDictionary<string, long> ByCategory { get; init; }
    public required IReadOnlyDictionary<string, long> BySeverity { get; init; }
    public required IReadOnlyList<AdminActionStats> TopActions { get; init; }
    public double ErrorRate { get; init; }
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }
    public DateTimeOffset Timestamp { get; init; }
}

/// <summary>
/// Admin action statistics.
/// </summary>
public sealed class AdminActionStats
{
    public required string Action { get; init; }
    public long EventCount { get; init; }
    public double? AvgDurationMs { get; init; }
}

#endregion
