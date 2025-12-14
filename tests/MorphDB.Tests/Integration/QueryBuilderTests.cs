using MorphDB.Core.Abstractions;
using MorphDB.Core.Models;
using MorphDB.Npgsql.Infrastructure;
using MorphDB.Npgsql.Repositories;
using MorphDB.Npgsql.Services;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration;

/// <summary>
/// Integration tests for MorphQueryBuilder.
/// </summary>
[Collection("PostgreSQL")]
public class QueryBuilderTests
{
    private readonly PostgresFixture _fixture;
    private readonly PostgresSchemaManager _schemaManager;
    private readonly PostgresDataService _dataService;
    private readonly MetadataRepository _metadataRepository;

    public QueryBuilderTests(PostgresFixture fixture)
    {
        _fixture = fixture;
        _metadataRepository = new MetadataRepository(fixture.DataSource);

        var nameHasher = new Sha256NameHasher();
        var lockOptions = new AdvisoryLockOptions();
        var lockManager = new PostgresAdvisoryLockManager(fixture.DataSource, lockOptions);
        var changeLogger = new ChangeLogger(fixture.DataSource);
        var schemaOptions = new SchemaManagerOptions();

        _schemaManager = new PostgresSchemaManager(
            fixture.DataSource,
            _metadataRepository,
            lockManager,
            nameHasher,
            changeLogger,
            schemaOptions);

        _dataService = new PostgresDataService(fixture.DataSource, _metadataRepository);
    }

    private async Task<(Guid TenantId, TableMetadata Table)> SetupTestTableWithDataAsync()
    {
        var tenantId = Guid.NewGuid();
        var table = await _schemaManager.CreateTableAsync(new CreateTableRequest
        {
            TenantId = tenantId,
            LogicalName = "query_test_" + Guid.NewGuid().ToString("N")[..8],
            Columns =
            [
                new CreateColumnRequest { LogicalName = "name", DataType = MorphDataType.Text, IsNullable = false },
                new CreateColumnRequest { LogicalName = "email", DataType = MorphDataType.Text, IsNullable = false },
                new CreateColumnRequest { LogicalName = "age", DataType = MorphDataType.Integer, IsNullable = true },
                new CreateColumnRequest { LogicalName = "score", DataType = MorphDataType.Decimal, IsNullable = true },
                new CreateColumnRequest { LogicalName = "is_active", DataType = MorphDataType.Boolean, IsNullable = false }
            ]
        });

        // Insert test data
        var records = new List<IDictionary<string, object?>>
        {
            new Dictionary<string, object?>
            {
                ["id"] = Guid.NewGuid(), ["name"] = "Alice", ["email"] = "alice@example.com",
                ["age"] = 25, ["score"] = 85.5m, ["is_active"] = true
            },
            new Dictionary<string, object?>
            {
                ["id"] = Guid.NewGuid(), ["name"] = "Bob", ["email"] = "bob@example.com",
                ["age"] = 30, ["score"] = 92.0m, ["is_active"] = true
            },
            new Dictionary<string, object?>
            {
                ["id"] = Guid.NewGuid(), ["name"] = "Charlie", ["email"] = "charlie@example.com",
                ["age"] = 35, ["score"] = 78.5m, ["is_active"] = false
            },
            new Dictionary<string, object?>
            {
                ["id"] = Guid.NewGuid(), ["name"] = "Diana", ["email"] = "diana@example.com",
                ["age"] = 28, ["score"] = 95.0m, ["is_active"] = true
            },
            new Dictionary<string, object?>
            {
                ["id"] = Guid.NewGuid(), ["name"] = "Eve", ["email"] = "eve@example.com",
                ["age"] = 22, ["score"] = 88.5m, ["is_active"] = true
            }
        };

        await _dataService.InsertBatchAsync(tenantId, table.LogicalName, records);

        return (tenantId, table);
    }

    #region Basic Query Tests

    [Fact]
    public async Task Query_SelectAll_ShouldReturnAllRecords()
    {
        // Arrange
        var (tenantId, table) = await SetupTestTableWithDataAsync();

        // Act
        var results = await _dataService.Query(tenantId)
            .From(table.LogicalName)
            .SelectAll()
            .ToListAsync();

        // Assert
        results.Should().HaveCount(5);
        results.Should().AllSatisfy(r => r.Should().ContainKey("name"));
        results.Should().AllSatisfy(r => r.Should().ContainKey("email"));
    }

    [Fact]
    public async Task Query_SelectColumns_ShouldReturnOnlySelectedColumns()
    {
        // Arrange
        var (tenantId, table) = await SetupTestTableWithDataAsync();

        // Act
        var results = await _dataService.Query(tenantId)
            .From(table.LogicalName)
            .SelectColumns("name", "email")
            .ToListAsync();

        // Assert
        results.Should().HaveCount(5);
        results.Should().AllSatisfy(r =>
        {
            r.Should().ContainKey("name");
            r.Should().ContainKey("email");
        });
    }

    #endregion

    #region WHERE Clause Tests

    [Fact]
    public async Task Query_WhereEquals_ShouldFilterCorrectly()
    {
        // Arrange
        var (tenantId, table) = await SetupTestTableWithDataAsync();

        // Act
        var results = await _dataService.Query(tenantId)
            .From(table.LogicalName)
            .SelectAll()
            .Where("name", FilterOperator.Equals, "Alice")
            .ToListAsync();

        // Assert
        results.Should().HaveCount(1);
        results[0]["name"].Should().Be("Alice");
    }

    [Fact]
    public async Task Query_WhereGreaterThan_ShouldFilterCorrectly()
    {
        // Arrange
        var (tenantId, table) = await SetupTestTableWithDataAsync();

        // Act
        var results = await _dataService.Query(tenantId)
            .From(table.LogicalName)
            .SelectAll()
            .Where("age", FilterOperator.GreaterThan, 28)
            .ToListAsync();

        // Assert
        results.Should().HaveCount(2); // Bob (30) and Charlie (35)
        results.Should().AllSatisfy(r => ((int)r["age"]!).Should().BeGreaterThan(28));
    }

    [Fact]
    public async Task Query_WhereIn_ShouldFilterCorrectly()
    {
        // Arrange
        var (tenantId, table) = await SetupTestTableWithDataAsync();

        // Act
        var results = await _dataService.Query(tenantId)
            .From(table.LogicalName)
            .SelectAll()
            .WhereIn("name", new object[] { "Alice", "Bob", "Charlie" })
            .ToListAsync();

        // Assert
        results.Should().HaveCount(3);
    }

    [Fact]
    public async Task Query_WhereContains_ShouldFilterCorrectly()
    {
        // Arrange
        var (tenantId, table) = await SetupTestTableWithDataAsync();

        // Act
        var results = await _dataService.Query(tenantId)
            .From(table.LogicalName)
            .SelectAll()
            .Where("email", FilterOperator.Contains, "example")
            .ToListAsync();

        // Assert
        results.Should().HaveCount(5); // All emails contain "example"
    }

    [Fact]
    public async Task Query_MultipleWhereConditions_ShouldFilterCorrectly()
    {
        // Arrange
        var (tenantId, table) = await SetupTestTableWithDataAsync();

        // Act
        var results = await _dataService.Query(tenantId)
            .From(table.LogicalName)
            .SelectAll()
            .Where("is_active", FilterOperator.Equals, true)
            .AndWhere("age", FilterOperator.GreaterThanOrEquals, 25)
            .ToListAsync();

        // Assert
        results.Should().HaveCount(3); // Alice (25), Bob (30), Diana (28)
    }

    #endregion

    #region ORDER BY Tests

    [Fact]
    public async Task Query_OrderBy_ShouldSortAscending()
    {
        // Arrange
        var (tenantId, table) = await SetupTestTableWithDataAsync();

        // Act
        var results = await _dataService.Query(tenantId)
            .From(table.LogicalName)
            .SelectAll()
            .OrderBy("age")
            .ToListAsync();

        // Assert
        results.Should().HaveCount(5);
        var ages = results.Select(r => (int)r["age"]!).ToList();
        ages.Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task Query_OrderByDesc_ShouldSortDescending()
    {
        // Arrange
        var (tenantId, table) = await SetupTestTableWithDataAsync();

        // Act
        var results = await _dataService.Query(tenantId)
            .From(table.LogicalName)
            .SelectAll()
            .OrderByDesc("score")
            .ToListAsync();

        // Assert
        results.Should().HaveCount(5);
        var scores = results.Select(r => (decimal)r["score"]!).ToList();
        scores.Should().BeInDescendingOrder();
    }

    #endregion

    #region Pagination Tests

    [Fact]
    public async Task Query_LimitOffset_ShouldPaginateCorrectly()
    {
        // Arrange
        var (tenantId, table) = await SetupTestTableWithDataAsync();

        // Act
        var page1 = await _dataService.Query(tenantId)
            .From(table.LogicalName)
            .SelectAll()
            .OrderBy("name")
            .Limit(2)
            .Offset(0)
            .ToListAsync();

        var page2 = await _dataService.Query(tenantId)
            .From(table.LogicalName)
            .SelectAll()
            .OrderBy("name")
            .Limit(2)
            .Offset(2)
            .ToListAsync();

        // Assert
        page1.Should().HaveCount(2);
        page2.Should().HaveCount(2);
        page1[0]["name"].Should().Be("Alice");
        page2[0]["name"].Should().Be("Charlie");
    }

    [Fact]
    public async Task Query_FirstOrDefault_ShouldReturnFirstRecord()
    {
        // Arrange
        var (tenantId, table) = await SetupTestTableWithDataAsync();

        // Act
        var result = await _dataService.Query(tenantId)
            .From(table.LogicalName)
            .SelectAll()
            .OrderBy("name")
            .FirstOrDefaultAsync();

        // Assert
        result.Should().NotBeNull();
        result!["name"].Should().Be("Alice");
    }

    [Fact]
    public async Task Query_FirstOrDefault_WithNoMatch_ShouldReturnNull()
    {
        // Arrange
        var (tenantId, table) = await SetupTestTableWithDataAsync();

        // Act
        var result = await _dataService.Query(tenantId)
            .From(table.LogicalName)
            .SelectAll()
            .Where("name", FilterOperator.Equals, "NonExistent")
            .FirstOrDefaultAsync();

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region Aggregate Tests

    [Fact]
    public async Task Query_Count_ShouldReturnCorrectCount()
    {
        // Arrange
        var (tenantId, table) = await SetupTestTableWithDataAsync();

        // Act
        var count = await _dataService.Query(tenantId)
            .From(table.LogicalName)
            .CountAsync();

        // Assert
        count.Should().Be(5);
    }

    [Fact]
    public async Task Query_CountWithWhere_ShouldReturnFilteredCount()
    {
        // Arrange
        var (tenantId, table) = await SetupTestTableWithDataAsync();

        // Act
        var count = await _dataService.Query(tenantId)
            .From(table.LogicalName)
            .Where("is_active", FilterOperator.Equals, true)
            .CountAsync();

        // Assert
        count.Should().Be(4); // All except Charlie
    }

    [Fact]
    public async Task Query_Sum_ShouldReturnCorrectSum()
    {
        // Arrange
        var (tenantId, table) = await SetupTestTableWithDataAsync();

        // Act
        var sum = await _dataService.Query(tenantId)
            .From(table.LogicalName)
            .SumAsync("score");

        // Assert
        sum.Should().Be(439.5m); // 85.5 + 92.0 + 78.5 + 95.0 + 88.5
    }

    [Fact]
    public async Task Query_Avg_ShouldReturnCorrectAverage()
    {
        // Arrange
        var (tenantId, table) = await SetupTestTableWithDataAsync();

        // Act
        var avg = await _dataService.Query(tenantId)
            .From(table.LogicalName)
            .AvgAsync("age");

        // Assert
        avg.Should().Be(28m); // (25 + 30 + 35 + 28 + 22) / 5 = 28
    }

    [Fact]
    public async Task Query_Min_ShouldReturnMinValue()
    {
        // Arrange
        var (tenantId, table) = await SetupTestTableWithDataAsync();

        // Act
        var min = await _dataService.Query(tenantId)
            .From(table.LogicalName)
            .MinAsync<int>("age");

        // Assert
        min.Should().Be(22); // Eve
    }

    [Fact]
    public async Task Query_Max_ShouldReturnMaxValue()
    {
        // Arrange
        var (tenantId, table) = await SetupTestTableWithDataAsync();

        // Act
        var max = await _dataService.Query(tenantId)
            .From(table.LogicalName)
            .MaxAsync<decimal>("score");

        // Assert
        max.Should().Be(95.0m); // Diana
    }

    #endregion

    #region SQL Generation Tests

    [Fact]
    public async Task Query_ToSql_ShouldReturnValidSql()
    {
        // Arrange
        var (tenantId, table) = await SetupTestTableWithDataAsync();

        // Act
        var sql = _dataService.Query(tenantId)
            .From(table.LogicalName)
            .SelectAll()
            .Where("name", FilterOperator.Equals, "Alice")
            .OrderBy("age")
            .Limit(10)
            .ToSql();

        // Assert
        sql.Should().Contain("SELECT");
        sql.Should().Contain("FROM");
        sql.Should().Contain("WHERE");
        sql.Should().Contain("ORDER BY");
        sql.Should().Contain("LIMIT");
    }

    #endregion
}
