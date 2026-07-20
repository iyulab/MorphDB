using System.Globalization;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Models;
using MorphDB.Npgsql.Infrastructure;
using MorphDB.Npgsql.Repositories;
using MorphDB.Npgsql.Security;
using MorphDB.Npgsql.Services;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration;

/// <summary>
/// Integration tests for PostgresAggregationService.
/// </summary>
[Collection("PostgreSQL")]
public class AggregationServiceTests
{
    private readonly PostgresFixture _fixture;
    private readonly PostgresSchemaManager _schemaManager;
    private readonly PostgresDataService _dataService;
    private readonly PostgresAggregationService _aggregationService;
    private readonly MetadataRepository _metadataRepository;

    public AggregationServiceTests(PostgresFixture fixture)
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

        var securityPolicyService = new SecurityPolicyService(fixture.DataSource);
        var securityContextAccessor = new SecurityContextAccessor();

        _dataService = new PostgresDataService(
            fixture.DataSource,
            _metadataRepository,
            securityPolicyService,
            securityContextAccessor);

        _aggregationService = new PostgresAggregationService(
            fixture.DataSource,
            _metadataRepository,
            securityPolicyService,
            securityContextAccessor);
    }

    private async Task<TableMetadata> CreateTestTableAsync(Guid projectId, string logicalName)
    {
        return await _schemaManager.CreateTableAsync(new CreateTableRequest
        {
            ProjectId = projectId,
            LogicalName = logicalName,
            Columns =
            [
                new CreateColumnRequest
                {
                    LogicalName = "category",
                    DataType = MorphDataType.Text,
                    IsNullable = false
                },
                new CreateColumnRequest
                {
                    LogicalName = "status",
                    DataType = MorphDataType.Text,
                    IsNullable = false
                },
                new CreateColumnRequest
                {
                    LogicalName = "amount",
                    DataType = MorphDataType.Decimal,
                    IsNullable = false
                },
                new CreateColumnRequest
                {
                    LogicalName = "quantity",
                    DataType = MorphDataType.Integer,
                    IsNullable = false
                }
            ]
        });
    }

    private async Task InsertTestDataAsync(Guid projectId, string tableName)
    {
        // Insert test records with different categories and amounts
        var testData = new[]
        {
            new { category = "electronics", status = "active", amount = 100.00m, quantity = 5 },
            new { category = "electronics", status = "active", amount = 200.00m, quantity = 3 },
            new { category = "electronics", status = "inactive", amount = 50.00m, quantity = 2 },
            new { category = "clothing", status = "active", amount = 75.00m, quantity = 10 },
            new { category = "clothing", status = "active", amount = 125.00m, quantity = 7 },
            new { category = "clothing", status = "inactive", amount = 30.00m, quantity = 4 },
            new { category = "food", status = "active", amount = 25.00m, quantity = 20 },
            new { category = "food", status = "active", amount = 15.00m, quantity = 15 },
        };

        foreach (var item in testData)
        {
            await _dataService.InsertAsync(projectId, tableName, new Dictionary<string, object?>
            {
                ["_id"] = Guid.CreateVersion7(),
                ["category"] = item.category,
                ["status"] = item.status,
                ["amount"] = item.amount,
                ["quantity"] = item.quantity
            });
        }
    }

    #region Count Tests

    [Fact]
    public async Task AggregateAsync_Count_ShouldReturnTotalCount()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var table = await CreateTestTableAsync(projectId, "agg_count_" + Guid.NewGuid().ToString("N")[..8]);
        await InsertTestDataAsync(projectId, table.LogicalName);

        var request = new AggregationRequest
        {
            Aggregations =
            [
                new AggregationColumn
                {
                    Function = AggregateFunction.Count,
                    Alias = "total"
                }
            ]
        };

        // Act
        var result = await _aggregationService.AggregateAsync(projectId, table.LogicalName, request);

        // Assert
        Assert.Single(result.Data);
        Assert.Equal(8L, Convert.ToInt64(result.Data[0]["total"], CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task AggregateAsync_CountWithGroupBy_ShouldReturnCountPerGroup()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var table = await CreateTestTableAsync(projectId, "agg_count_grp_" + Guid.NewGuid().ToString("N")[..8]);
        await InsertTestDataAsync(projectId, table.LogicalName);

        var request = new AggregationRequest
        {
            Aggregations =
            [
                new AggregationColumn
                {
                    Function = AggregateFunction.Count,
                    Alias = "count"
                }
            ],
            GroupBy = ["category"]
        };

        // Act
        var result = await _aggregationService.AggregateAsync(projectId, table.LogicalName, request);

        // Assert
        Assert.Equal(3, result.Data.Count);

        var electronics = result.Data.FirstOrDefault(d => d["category"]?.ToString() == "electronics");
        var clothing = result.Data.FirstOrDefault(d => d["category"]?.ToString() == "clothing");
        var food = result.Data.FirstOrDefault(d => d["category"]?.ToString() == "food");

        Assert.NotNull(electronics);
        Assert.NotNull(clothing);
        Assert.NotNull(food);
        Assert.Equal(3L, Convert.ToInt64(electronics["count"], CultureInfo.InvariantCulture));
        Assert.Equal(3L, Convert.ToInt64(clothing["count"], CultureInfo.InvariantCulture));
        Assert.Equal(2L, Convert.ToInt64(food["count"], CultureInfo.InvariantCulture));
    }

    #endregion

    #region Sum Tests

    [Fact]
    public async Task AggregateAsync_Sum_ShouldReturnTotalSum()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var table = await CreateTestTableAsync(projectId, "agg_sum_" + Guid.NewGuid().ToString("N")[..8]);
        await InsertTestDataAsync(projectId, table.LogicalName);

        var request = new AggregationRequest
        {
            Aggregations =
            [
                new AggregationColumn
                {
                    Function = AggregateFunction.Sum,
                    Column = "amount",
                    Alias = "total_amount"
                }
            ]
        };

        // Act
        var result = await _aggregationService.AggregateAsync(projectId, table.LogicalName, request);

        // Assert
        Assert.Single(result.Data);
        Assert.Equal(620.00m, Convert.ToDecimal(result.Data[0]["total_amount"], CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task AggregateAsync_SumWithGroupBy_ShouldReturnSumPerGroup()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var table = await CreateTestTableAsync(projectId, "agg_sum_grp_" + Guid.NewGuid().ToString("N")[..8]);
        await InsertTestDataAsync(projectId, table.LogicalName);

        var request = new AggregationRequest
        {
            Aggregations =
            [
                new AggregationColumn
                {
                    Function = AggregateFunction.Sum,
                    Column = "amount",
                    Alias = "total_amount"
                }
            ],
            GroupBy = ["category"]
        };

        // Act
        var result = await _aggregationService.AggregateAsync(projectId, table.LogicalName, request);

        // Assert
        Assert.Equal(3, result.Data.Count);

        var electronics = result.Data.FirstOrDefault(d => d["category"]?.ToString() == "electronics");
        Assert.NotNull(electronics);
        Assert.Equal(350.00m, Convert.ToDecimal(electronics["total_amount"], CultureInfo.InvariantCulture));
    }

    #endregion

    #region Avg/Min/Max Tests

    [Fact]
    public async Task AggregateAsync_Avg_ShouldReturnAverage()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var table = await CreateTestTableAsync(projectId, "agg_avg_" + Guid.NewGuid().ToString("N")[..8]);
        await InsertTestDataAsync(projectId, table.LogicalName);

        var request = new AggregationRequest
        {
            Aggregations =
            [
                new AggregationColumn
                {
                    Function = AggregateFunction.Avg,
                    Column = "amount",
                    Alias = "avg_amount"
                }
            ]
        };

        // Act
        var result = await _aggregationService.AggregateAsync(projectId, table.LogicalName, request);

        // Assert
        Assert.Single(result.Data);
        Assert.Equal(77.50m, Convert.ToDecimal(result.Data[0]["avg_amount"], CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task AggregateAsync_MinMax_ShouldReturnMinAndMax()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var table = await CreateTestTableAsync(projectId, "agg_minmax_" + Guid.NewGuid().ToString("N")[..8]);
        await InsertTestDataAsync(projectId, table.LogicalName);

        var request = new AggregationRequest
        {
            Aggregations =
            [
                new AggregationColumn
                {
                    Function = AggregateFunction.Min,
                    Column = "amount",
                    Alias = "min_amount"
                },
                new AggregationColumn
                {
                    Function = AggregateFunction.Max,
                    Column = "amount",
                    Alias = "max_amount"
                }
            ]
        };

        // Act
        var result = await _aggregationService.AggregateAsync(projectId, table.LogicalName, request);

        // Assert
        Assert.Single(result.Data);
        Assert.Equal(15.00m, Convert.ToDecimal(result.Data[0]["min_amount"], CultureInfo.InvariantCulture));
        Assert.Equal(200.00m, Convert.ToDecimal(result.Data[0]["max_amount"], CultureInfo.InvariantCulture));
    }

    #endregion

    #region Filter Tests

    [Fact]
    public async Task AggregateAsync_WithFilter_ShouldApplyFilter()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var table = await CreateTestTableAsync(projectId, "agg_filter_" + Guid.NewGuid().ToString("N")[..8]);
        await InsertTestDataAsync(projectId, table.LogicalName);

        var request = new AggregationRequest
        {
            Aggregations =
            [
                new AggregationColumn
                {
                    Function = AggregateFunction.Count,
                    Alias = "count"
                }
            ],
            Filter =
            [
                new FilterCondition
                {
                    Column = "status",
                    Operator = FilterOperator.Equals,
                    Value = "active"
                }
            ]
        };

        // Act
        var result = await _aggregationService.AggregateAsync(projectId, table.LogicalName, request);

        // Assert
        Assert.Single(result.Data);
        Assert.Equal(6L, Convert.ToInt64(result.Data[0]["count"], CultureInfo.InvariantCulture));
    }

    #endregion

    #region Having Tests

    [Fact]
    public async Task AggregateAsync_WithHaving_ShouldFilterAggregatedResults()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var table = await CreateTestTableAsync(projectId, "agg_having_" + Guid.NewGuid().ToString("N")[..8]);
        await InsertTestDataAsync(projectId, table.LogicalName);

        var request = new AggregationRequest
        {
            Aggregations =
            [
                new AggregationColumn
                {
                    Function = AggregateFunction.Sum,
                    Column = "amount",
                    Alias = "total_amount"
                }
            ],
            GroupBy = ["category"],
            Having =
            [
                new HavingCondition
                {
                    Alias = "total_amount",
                    Operator = FilterOperator.GreaterThan,
                    Value = 100m
                }
            ]
        };

        // Act
        var result = await _aggregationService.AggregateAsync(projectId, table.LogicalName, request);

        // Assert
        Assert.Equal(2, result.Data.Count); // electronics (350) and clothing (230)
        Assert.All(result.Data, d => Assert.True(Convert.ToDecimal(d["total_amount"], CultureInfo.InvariantCulture) > 100m));
    }

    #endregion

    #region OrderBy Tests

    [Fact]
    public async Task AggregateAsync_WithOrderBy_ShouldOrderResults()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var table = await CreateTestTableAsync(projectId, "agg_order_" + Guid.NewGuid().ToString("N")[..8]);
        await InsertTestDataAsync(projectId, table.LogicalName);

        var request = new AggregationRequest
        {
            Aggregations =
            [
                new AggregationColumn
                {
                    Function = AggregateFunction.Sum,
                    Column = "amount",
                    Alias = "total_amount"
                }
            ],
            GroupBy = ["category"],
            OrderBy =
            [
                new AggregationOrderBy
                {
                    Column = "total_amount",
                    Descending = true
                }
            ]
        };

        // Act
        var result = await _aggregationService.AggregateAsync(projectId, table.LogicalName, request);

        // Assert
        Assert.Equal(3, result.Data.Count);
        // Electronics (350) > Clothing (230) > Food (40)
        Assert.Equal("electronics", result.Data[0]["category"]?.ToString());
        Assert.Equal("clothing", result.Data[1]["category"]?.ToString());
        Assert.Equal("food", result.Data[2]["category"]?.ToString());
    }

    #endregion

    #region Limit/Offset Tests

    [Fact]
    public async Task AggregateAsync_WithLimitOffset_ShouldPaginateResults()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var table = await CreateTestTableAsync(projectId, "agg_limit_" + Guid.NewGuid().ToString("N")[..8]);
        await InsertTestDataAsync(projectId, table.LogicalName);

        var request = new AggregationRequest
        {
            Aggregations =
            [
                new AggregationColumn
                {
                    Function = AggregateFunction.Count,
                    Alias = "count"
                }
            ],
            GroupBy = ["category"],
            OrderBy =
            [
                new AggregationOrderBy { Column = "category", Descending = false }
            ],
            Limit = 2,
            Offset = 1
        };

        // Act
        var result = await _aggregationService.AggregateAsync(projectId, table.LogicalName, request);

        // Assert
        Assert.Equal(2, result.Data.Count);
        Assert.Equal(3L, result.TotalGroups); // Total is 3 categories
    }

    #endregion

    #region Multiple Aggregations Tests

    [Fact]
    public async Task AggregateAsync_MultipleAggregations_ShouldReturnAllResults()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var table = await CreateTestTableAsync(projectId, "agg_multi_" + Guid.NewGuid().ToString("N")[..8]);
        await InsertTestDataAsync(projectId, table.LogicalName);

        var request = new AggregationRequest
        {
            Aggregations =
            [
                new AggregationColumn { Function = AggregateFunction.Count, Alias = "count" },
                new AggregationColumn { Function = AggregateFunction.Sum, Column = "amount", Alias = "sum_amount" },
                new AggregationColumn { Function = AggregateFunction.Avg, Column = "amount", Alias = "avg_amount" },
                new AggregationColumn { Function = AggregateFunction.Min, Column = "quantity", Alias = "min_qty" },
                new AggregationColumn { Function = AggregateFunction.Max, Column = "quantity", Alias = "max_qty" }
            ],
            GroupBy = ["category"]
        };

        // Act
        var result = await _aggregationService.AggregateAsync(projectId, table.LogicalName, request);

        // Assert
        Assert.Equal(3, result.Data.Count);

        foreach (var row in result.Data)
        {
            Assert.True(row.ContainsKey("count"));
            Assert.True(row.ContainsKey("sum_amount"));
            Assert.True(row.ContainsKey("avg_amount"));
            Assert.True(row.ContainsKey("min_qty"));
            Assert.True(row.ContainsKey("max_qty"));
            Assert.True(row.ContainsKey("category"));
        }
    }

    #endregion
}
