using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using MorphDB.Core.Diagnostics;
using MorphDB.Npgsql.Diagnostics;

namespace MorphDB.Tests.Unit.Diagnostics;

/// <summary>
/// Tests for QueryExecutionScope automatic timing and recording.
/// </summary>
public sealed class QueryExecutionScopeTests
{
    private readonly Mock<ILogger<QueryDiagnosticsService>> _loggerMock;
    private readonly QueryDiagnosticsService _diagnostics;

    public QueryExecutionScopeTests()
    {
        _loggerMock = new Mock<ILogger<QueryDiagnosticsService>>();
        var options = new QueryDiagnosticsOptions
        {
            Enabled = true,
            SlowQueryThresholdMs = 50, // Low threshold for testing
            MaxSlowQueryEntries = 100,
            LogSlowQueries = true
        };

        _diagnostics = new QueryDiagnosticsService(
            Options.Create(options),
            _loggerMock.Object);
    }

    [Fact]
    public void Dispose_RecordsQueryExecution()
    {
        var tenantId = Guid.NewGuid();

        using (var scope = new QueryExecutionScope(
            _diagnostics,
            tenantId,
            "test_table",
            QueryOperationType.Select,
            "Unit Test"))
        {
            scope.SetRowCount(5);
        }

        var stats = _diagnostics.GetStatistics();
        Assert.Equal(1, stats.TotalQueries);
    }

    [Fact]
    public void Dispose_CalculatesDuration()
    {
        var tenantId = Guid.NewGuid();

        using (var scope = new QueryExecutionScope(
            _diagnostics,
            tenantId,
            "test_table",
            QueryOperationType.Select,
            "Unit Test"))
        {
            Thread.Sleep(20); // Ensure measurable duration
        }

        var stats = _diagnostics.GetStatistics();
        Assert.True(stats.AverageDurationMs >= 10); // Should have some duration
    }

    [Fact]
    public void SetRowCount_ReflectedInStatistics()
    {
        var tenantId = Guid.NewGuid();

        using (var scope = new QueryExecutionScope(
            _diagnostics,
            tenantId,
            "test_table",
            QueryOperationType.Insert,
            "Unit Test"))
        {
            scope.SetRowCount(42);
        }

        var stats = _diagnostics.GetStatistics();
        var insertStats = stats.ByOperationType[QueryOperationType.Insert];
        Assert.Equal(42, insertStats.TotalRowsAffected);
    }

    [Fact]
    public void SetError_RecordsAsFailedQuery()
    {
        var tenantId = Guid.NewGuid();

        using (var scope = new QueryExecutionScope(
            _diagnostics,
            tenantId,
            "test_table",
            QueryOperationType.Update,
            "Unit Test"))
        {
            scope.SetError("Connection failed");
        }

        var stats = _diagnostics.GetStatistics();
        Assert.Equal(1, stats.FailedQueries);
    }

    [Fact]
    public void SlowQuery_MarkedCorrectly()
    {
        var tenantId = Guid.NewGuid();

        using (var scope = new QueryExecutionScope(
            _diagnostics,
            tenantId,
            "test_table",
            QueryOperationType.Select,
            "Unit Test"))
        {
            Thread.Sleep(60); // Exceed the 50ms threshold
        }

        var slowQueries = _diagnostics.GetRecentSlowQueries(10);
        Assert.Single(slowQueries);
        Assert.True(slowQueries[0].IsSlow);
    }

    [Fact]
    public void FastQuery_NotMarkedAsSlow()
    {
        var tenantId = Guid.NewGuid();

        using (var scope = new QueryExecutionScope(
            _diagnostics,
            tenantId,
            "test_table",
            QueryOperationType.Select,
            "Unit Test"))
        {
            // Fast query - no delay
        }

        var slowQueries = _diagnostics.GetRecentSlowQueries(10);
        Assert.Empty(slowQueries);
    }

    [Fact]
    public void Source_RecordedCorrectly()
    {
        var tenantId = Guid.NewGuid();

        using (var scope = new QueryExecutionScope(
            _diagnostics,
            tenantId,
            "test_table",
            QueryOperationType.Select,
            "API/Data/Query"))
        {
            Thread.Sleep(60); // Make it slow to retrieve from slow queries
        }

        var slowQueries = _diagnostics.GetRecentSlowQueries(10);
        Assert.Single(slowQueries);
        Assert.Equal("API/Data/Query", slowQueries[0].Source);
    }

    [Fact]
    public void TenantId_RecordedCorrectly()
    {
        var tenantId = Guid.NewGuid();

        using (var scope = new QueryExecutionScope(
            _diagnostics,
            tenantId,
            "test_table",
            QueryOperationType.Select,
            "Unit Test"))
        {
            Thread.Sleep(60); // Make it slow to retrieve from slow queries
        }

        var slowQueries = _diagnostics.GetRecentSlowQueries(10);
        Assert.Single(slowQueries);
        Assert.Equal(tenantId, slowQueries[0].TenantId);
    }

    [Fact]
    public void TableName_RecordedCorrectly()
    {
        var tenantId = Guid.NewGuid();

        using (var scope = new QueryExecutionScope(
            _diagnostics,
            tenantId,
            "customers",
            QueryOperationType.Delete,
            "Unit Test"))
        {
            Thread.Sleep(60); // Make it slow to retrieve from slow queries
        }

        var slowQueries = _diagnostics.GetRecentSlowQueries(10);
        Assert.Single(slowQueries);
        Assert.Equal("customers", slowQueries[0].TableName);
    }

    [Fact]
    public void OperationType_RecordedCorrectly()
    {
        var tenantId = Guid.NewGuid();

        using (var scope = new QueryExecutionScope(
            _diagnostics,
            tenantId,
            "test_table",
            QueryOperationType.Upsert,
            "Unit Test"))
        {
            scope.SetRowCount(1);
        }

        var stats = _diagnostics.GetStatistics();
        Assert.True(stats.ByOperationType.ContainsKey(QueryOperationType.Upsert));
    }

    [Fact]
    public void MultipleScopes_AllRecorded()
    {
        var tenantId = Guid.NewGuid();

        using (var scope1 = new QueryExecutionScope(_diagnostics, tenantId, "table1", QueryOperationType.Select, "Test"))
        {
            scope1.SetRowCount(10);
        }

        using (var scope2 = new QueryExecutionScope(_diagnostics, tenantId, "table2", QueryOperationType.Insert, "Test"))
        {
            scope2.SetRowCount(1);
        }

        using (var scope3 = new QueryExecutionScope(_diagnostics, tenantId, "table3", QueryOperationType.Update, "Test"))
        {
            scope3.SetRowCount(5);
        }

        var stats = _diagnostics.GetStatistics();
        Assert.Equal(3, stats.TotalQueries);
        Assert.Equal(3, stats.ByOperationType.Count);
    }
}
