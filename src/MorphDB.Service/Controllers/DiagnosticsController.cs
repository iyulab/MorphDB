using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MorphDB.Core.Diagnostics;
using MorphDB.Service.Infrastructure;
using Npgsql;

namespace MorphDB.Service.Controllers;

/// <summary>
/// API controller for system diagnostics and performance monitoring.
/// Provides endpoints for health metrics, query performance, and connection pool status.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public partial class DiagnosticsController : ControllerBase
{
    private readonly IQueryDiagnostics _queryDiagnostics;
    private readonly NpgsqlDataSource _dataSource;
    private readonly GracefulShutdownService _shutdownService;
    private readonly ILogger<DiagnosticsController> _logger;

    public DiagnosticsController(
        IQueryDiagnostics queryDiagnostics,
        NpgsqlDataSource dataSource,
        GracefulShutdownService shutdownService,
        ILogger<DiagnosticsController> logger)
    {
        _queryDiagnostics = queryDiagnostics;
        _dataSource = dataSource;
        _shutdownService = shutdownService;
        _logger = logger;
    }

    /// <summary>
    /// Gets query performance statistics.
    /// </summary>
    /// <param name="since">Optional start time for statistics period (ISO 8601 format).</param>
    /// <returns>Aggregated query statistics.</returns>
    [HttpGet("queries/stats")]
    [ProducesResponseType(typeof(QueryStatisticsResponse), StatusCodes.Status200OK)]
    public IActionResult GetQueryStatistics([FromQuery] DateTimeOffset? since = null)
    {
        var stats = _queryDiagnostics.GetStatistics(since);
        return Ok(new QueryStatisticsResponse
        {
            TotalQueries = stats.TotalQueries,
            SlowQueries = stats.SlowQueries,
            FailedQueries = stats.FailedQueries,
            AverageDurationMs = Math.Round(stats.AverageDurationMs, 2),
            P95DurationMs = Math.Round(stats.P95DurationMs, 2),
            P99DurationMs = Math.Round(stats.P99DurationMs, 2),
            MaxDurationMs = stats.MaxDurationMs,
            SlowQueryThresholdMs = (long)_queryDiagnostics.SlowQueryThreshold.TotalMilliseconds,
            ByOperationType = stats.ByOperationType.ToDictionary(
                kvp => kvp.Key.ToString(),
                kvp => new OperationStatsResponse
                {
                    Count = kvp.Value.Count,
                    AverageDurationMs = Math.Round(kvp.Value.AverageDurationMs, 2),
                    TotalRowsAffected = kvp.Value.TotalRowsAffected
                }),
            PeriodStart = stats.PeriodStart,
            PeriodEnd = stats.PeriodEnd
        });
    }

    /// <summary>
    /// Gets recent slow queries for analysis.
    /// </summary>
    /// <param name="count">Maximum number of entries to return (default: 50, max: 500).</param>
    /// <returns>List of recent slow query entries.</returns>
    [HttpGet("queries/slow")]
    [ProducesResponseType(typeof(SlowQueriesResponse), StatusCodes.Status200OK)]
    public IActionResult GetSlowQueries([FromQuery] int count = 50)
    {
        count = Math.Clamp(count, 1, 500);
        var entries = _queryDiagnostics.GetRecentSlowQueries(count);

        return Ok(new SlowQueriesResponse
        {
            Count = entries.Count,
            ThresholdMs = (long)_queryDiagnostics.SlowQueryThreshold.TotalMilliseconds,
            Entries = entries.Select(e => new SlowQueryEntry
            {
                ExecutionId = e.ExecutionId,
                TenantId = e.TenantId,
                TableName = e.TableName,
                OperationType = e.OperationType.ToString(),
                DurationMs = e.DurationMs,
                RowCount = e.RowCount,
                ExecutedAt = e.ExecutedAt,
                Source = e.Source,
                HasError = e.ErrorMessage is not null
            }).ToList()
        });
    }

    /// <summary>
    /// Gets connection pool health and basic metrics.
    /// Note: Detailed pool statistics are available via OpenTelemetry metrics at /metrics endpoint.
    /// </summary>
    /// <returns>Connection pool health status.</returns>
    [HttpGet("pool")]
    [ProducesResponseType(typeof(ConnectionPoolResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetConnectionPoolStats()
    {
        // Test connection to verify pool health
        var isHealthy = true;
        var healthCheckMs = 0L;
        string? errorMessage = null;

        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            await using var conn = await _dataSource.OpenConnectionAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1";
            await cmd.ExecuteScalarAsync();
            sw.Stop();
            healthCheckMs = sw.ElapsedMilliseconds;
        }
        catch (Exception ex)
        {
            isHealthy = false;
            errorMessage = ex.Message;
            LogConnectionPoolHealthCheckFailed(ex);
        }

        return Ok(new ConnectionPoolResponse
        {
            IsHealthy = isHealthy,
            HealthCheckDurationMs = healthCheckMs,
            ErrorMessage = errorMessage,
            ConnectionString = SanitizeConnectionString(_dataSource.ConnectionString),
            MetricsEndpoint = "/metrics"
        });
    }

    /// <summary>
    /// Gets the current shutdown status and active request count.
    /// </summary>
    /// <returns>Shutdown status information.</returns>
    [HttpGet("shutdown")]
    [ProducesResponseType(typeof(ShutdownStatusResponse), StatusCodes.Status200OK)]
    public IActionResult GetShutdownStatus()
    {
        return Ok(new ShutdownStatusResponse
        {
            IsShuttingDown = _shutdownService.IsShuttingDown,
            ActiveRequestCount = _shutdownService.ActiveRequestCount,
            Timestamp = DateTimeOffset.UtcNow
        });
    }

    /// <summary>
    /// Clears accumulated query statistics.
    /// </summary>
    /// <returns>Confirmation of statistics reset.</returns>
    [HttpPost("queries/stats/reset")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public IActionResult ResetStatistics()
    {
        _queryDiagnostics.ClearStatistics();
        LogStatisticsCleared();
        return Ok(new { message = "Statistics cleared", timestamp = DateTimeOffset.UtcNow });
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Connection pool health check failed")]
    private partial void LogConnectionPoolHealthCheckFailed(Exception ex);

    [LoggerMessage(Level = LogLevel.Information, Message = "Query diagnostics statistics cleared by user")]
    private partial void LogStatisticsCleared();

    private static string SanitizeConnectionString(string connectionString)
    {
        // Remove password from connection string for display
        var builder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Password = "***"
        };
        return builder.ToString();
    }
}

#region Response Models

public sealed class QueryStatisticsResponse
{
    public long TotalQueries { get; init; }
    public long SlowQueries { get; init; }
    public long FailedQueries { get; init; }
    public double AverageDurationMs { get; init; }
    public double P95DurationMs { get; init; }
    public double P99DurationMs { get; init; }
    public long MaxDurationMs { get; init; }
    public long SlowQueryThresholdMs { get; init; }
    public Dictionary<string, OperationStatsResponse> ByOperationType { get; init; } = [];
    public DateTimeOffset PeriodStart { get; init; }
    public DateTimeOffset PeriodEnd { get; init; }
}

public sealed class OperationStatsResponse
{
    public long Count { get; init; }
    public double AverageDurationMs { get; init; }
    public long TotalRowsAffected { get; init; }
}

public sealed class SlowQueriesResponse
{
    public int Count { get; init; }
    public long ThresholdMs { get; init; }
    public List<SlowQueryEntry> Entries { get; init; } = [];
}

public sealed class SlowQueryEntry
{
    public Guid ExecutionId { get; init; }
    public Guid? TenantId { get; init; }
    public string? TableName { get; init; }
    public required string OperationType { get; init; }
    public long DurationMs { get; init; }
    public int RowCount { get; init; }
    public DateTimeOffset ExecutedAt { get; init; }
    public string? Source { get; init; }
    public bool HasError { get; init; }
}

public sealed class ConnectionPoolResponse
{
    public bool IsHealthy { get; init; }
    public long HealthCheckDurationMs { get; init; }
    public string? ErrorMessage { get; init; }
    public required string ConnectionString { get; init; }
    public required string MetricsEndpoint { get; init; }
}

public sealed class ShutdownStatusResponse
{
    /// <summary>
    /// Whether the application is currently shutting down.
    /// </summary>
    public bool IsShuttingDown { get; init; }

    /// <summary>
    /// Current count of active requests being tracked.
    /// </summary>
    public int ActiveRequestCount { get; init; }

    /// <summary>
    /// Timestamp of the status check.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; }
}

#endregion
