using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using MorphDB.Core.Diagnostics;
using MorphDB.Npgsql.Diagnostics;

namespace MorphDB.Tests.Unit.Diagnostics;

/// <summary>
/// Tests for QueryDiagnosticsService slow query detection and statistics.
/// </summary>
public sealed class QueryDiagnosticsServiceTests
{
    private readonly Mock<ILogger<QueryDiagnosticsService>> _loggerMock;
    private readonly QueryDiagnosticsOptions _options;
    private readonly QueryDiagnosticsService _service;

    public QueryDiagnosticsServiceTests()
    {
        _loggerMock = new Mock<ILogger<QueryDiagnosticsService>>();
        _options = new QueryDiagnosticsOptions
        {
            Enabled = true,
            SlowQueryThresholdMs = 100, // Low threshold for testing
            MaxSlowQueryEntries = 50,
            LogSlowQueries = true,
            IncludeQueryPatterns = false
        };

        _service = new QueryDiagnosticsService(
            Options.Create(_options),
            _loggerMock.Object);
    }

    [Fact]
    public void SlowQueryThreshold_ReturnsConfiguredValue()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(100), _service.SlowQueryThreshold);
    }

    [Fact]
    public void RecordQuery_FastQuery_NotStoredAsSlowQuery()
    {
        var entry = CreateEntry(durationMs: 50, isSlow: false);

        _service.RecordQuery(entry);

        var slowQueries = _service.GetRecentSlowQueries(100);
        Assert.Empty(slowQueries);
    }

    [Fact]
    public void RecordQuery_SlowQuery_StoredForRetrieval()
    {
        var entry = CreateEntry(durationMs: 150, isSlow: true);

        _service.RecordQuery(entry);

        var slowQueries = _service.GetRecentSlowQueries(100);
        Assert.Single(slowQueries);
        Assert.Equal(entry.ExecutionId, slowQueries[0].ExecutionId);
    }

    [Fact]
    public void GetRecentSlowQueries_ReturnsNewestFirst()
    {
        var entry1 = CreateEntry(durationMs: 150, isSlow: true, executedAt: DateTimeOffset.UtcNow.AddMinutes(-2));
        var entry2 = CreateEntry(durationMs: 200, isSlow: true, executedAt: DateTimeOffset.UtcNow.AddMinutes(-1));
        var entry3 = CreateEntry(durationMs: 250, isSlow: true, executedAt: DateTimeOffset.UtcNow);

        _service.RecordQuery(entry1);
        _service.RecordQuery(entry2);
        _service.RecordQuery(entry3);

        var slowQueries = _service.GetRecentSlowQueries(100);

        Assert.Equal(3, slowQueries.Count);
        Assert.Equal(entry3.ExecutionId, slowQueries[0].ExecutionId);
        Assert.Equal(entry2.ExecutionId, slowQueries[1].ExecutionId);
        Assert.Equal(entry1.ExecutionId, slowQueries[2].ExecutionId);
    }

    [Fact]
    public void GetRecentSlowQueries_RespectsCountLimit()
    {
        for (int i = 0; i < 10; i++)
        {
            _service.RecordQuery(CreateEntry(durationMs: 150, isSlow: true));
        }

        var slowQueries = _service.GetRecentSlowQueries(5);

        Assert.Equal(5, slowQueries.Count);
    }

    [Fact]
    public void GetRecentSlowQueries_EnforcesMaxEntries()
    {
        // Record more than max entries
        for (int i = 0; i < 60; i++)
        {
            _service.RecordQuery(CreateEntry(durationMs: 150, isSlow: true));
        }

        var slowQueries = _service.GetRecentSlowQueries(100);

        Assert.Equal(_options.MaxSlowQueryEntries, slowQueries.Count);
    }

    [Fact]
    public void GetStatistics_ReturnsAggregatedData()
    {
        _service.RecordQuery(CreateEntry(durationMs: 50, isSlow: false, operationType: QueryOperationType.Select));
        _service.RecordQuery(CreateEntry(durationMs: 100, isSlow: false, operationType: QueryOperationType.Select));
        _service.RecordQuery(CreateEntry(durationMs: 150, isSlow: true, operationType: QueryOperationType.Insert));
        _service.RecordQuery(CreateEntry(durationMs: 200, isSlow: true, operationType: QueryOperationType.Update));

        var stats = _service.GetStatistics();

        Assert.Equal(4, stats.TotalQueries);
        Assert.Equal(2, stats.SlowQueries);
        Assert.Equal(125, stats.AverageDurationMs);
        Assert.Equal(200, stats.MaxDurationMs);
    }

    [Fact]
    public void GetStatistics_ByOperationType_GroupsCorrectly()
    {
        _service.RecordQuery(CreateEntry(durationMs: 50, isSlow: false, operationType: QueryOperationType.Select, rowCount: 10));
        _service.RecordQuery(CreateEntry(durationMs: 100, isSlow: false, operationType: QueryOperationType.Select, rowCount: 20));
        _service.RecordQuery(CreateEntry(durationMs: 150, isSlow: true, operationType: QueryOperationType.Insert, rowCount: 1));

        var stats = _service.GetStatistics();

        Assert.Equal(2, stats.ByOperationType.Count);
        Assert.True(stats.ByOperationType.ContainsKey(QueryOperationType.Select));
        Assert.True(stats.ByOperationType.ContainsKey(QueryOperationType.Insert));

        var selectStats = stats.ByOperationType[QueryOperationType.Select];
        Assert.Equal(2, selectStats.Count);
        Assert.Equal(75, selectStats.AverageDurationMs);
        Assert.Equal(30, selectStats.TotalRowsAffected);

        var insertStats = stats.ByOperationType[QueryOperationType.Insert];
        Assert.Equal(1, insertStats.Count);
        Assert.Equal(1, insertStats.TotalRowsAffected);
    }

    [Fact]
    public void GetStatistics_WithSinceFilter_SetsPeriodStart()
    {
        // The since parameter sets the PeriodStart for reporting purposes,
        // but does not filter the accumulated statistics
        var entry1 = CreateEntry(durationMs: 100, isSlow: false);
        var entry2 = CreateEntry(durationMs: 200, isSlow: true);

        _service.RecordQuery(entry1);
        _service.RecordQuery(entry2);

        var sinceTime = DateTimeOffset.UtcNow.AddHours(-1);
        var stats = _service.GetStatistics(since: sinceTime);

        // All queries are still counted
        Assert.Equal(2, stats.TotalQueries);
        // PeriodStart should be the 'since' parameter
        Assert.Equal(sinceTime, stats.PeriodStart);
    }

    [Fact]
    public void GetStatistics_FailedQueries_CountedCorrectly()
    {
        _service.RecordQuery(CreateEntry(durationMs: 50, isSlow: false, errorMessage: null));
        _service.RecordQuery(CreateEntry(durationMs: 100, isSlow: false, errorMessage: "Connection timeout"));
        _service.RecordQuery(CreateEntry(durationMs: 150, isSlow: true, errorMessage: "Constraint violation"));

        var stats = _service.GetStatistics();

        Assert.Equal(3, stats.TotalQueries);
        Assert.Equal(2, stats.FailedQueries);
    }

    [Fact]
    public void ClearStatistics_ResetsAllData()
    {
        _service.RecordQuery(CreateEntry(durationMs: 150, isSlow: true));
        _service.RecordQuery(CreateEntry(durationMs: 200, isSlow: true));

        _service.ClearStatistics();

        var stats = _service.GetStatistics();
        var slowQueries = _service.GetRecentSlowQueries(100);

        Assert.Equal(0, stats.TotalQueries);
        Assert.Empty(slowQueries);
    }

    [Fact]
    public void RecordQuery_DisabledService_DoesNothing()
    {
        var disabledOptions = new QueryDiagnosticsOptions { Enabled = false };
        var disabledService = new QueryDiagnosticsService(
            Options.Create(disabledOptions),
            _loggerMock.Object);

        disabledService.RecordQuery(CreateEntry(durationMs: 150, isSlow: true));

        var stats = disabledService.GetStatistics();
        Assert.Equal(0, stats.TotalQueries);
    }

    [Fact]
    public void GetStatistics_Percentiles_CalculatedCorrectly()
    {
        // Add entries with known durations for percentile calculation
        for (int i = 1; i <= 100; i++)
        {
            _service.RecordQuery(CreateEntry(durationMs: i * 10, isSlow: i > 10));
        }

        var stats = _service.GetStatistics();

        Assert.Equal(100, stats.TotalQueries);
        Assert.Equal(1000, stats.MaxDurationMs);
        // P95 should be around 950, P99 around 990
        Assert.True(stats.P95DurationMs >= 900 && stats.P95DurationMs <= 1000);
        Assert.True(stats.P99DurationMs >= 980 && stats.P99DurationMs <= 1000);
    }

    private QueryExecutionEntry CreateEntry(
        long durationMs,
        bool isSlow,
        QueryOperationType operationType = QueryOperationType.Select,
        int rowCount = 1,
        string? errorMessage = null,
        DateTimeOffset? executedAt = null)
    {
        return new QueryExecutionEntry
        {
            ExecutionId = Guid.NewGuid(),
            ProjectId = Guid.NewGuid(),
            TableName = "test_table",
            OperationType = operationType,
            QueryPattern = null,
            RowCount = rowCount,
            DurationMs = durationMs,
            IsSlow = isSlow,
            ExecutedAt = executedAt ?? DateTimeOffset.UtcNow,
            ErrorMessage = errorMessage,
            Source = "Unit Test"
        };
    }
}
