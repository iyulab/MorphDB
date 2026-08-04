using MorphDB.Core.Abstractions;
using MorphDB.Core.Models;
using MorphDB.Npgsql.Infrastructure;
using MorphDB.Npgsql.Repositories;
using MorphDB.Npgsql.Schema;
using MorphDB.Npgsql.Services;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration;

/// <summary>
/// Integration tests for Rollup Field functionality.
/// Rollup fields are virtual columns that aggregate data from related tables.
/// </summary>
[Collection("PostgreSQL")]
public class RollupFieldTests
{
    private readonly PostgresFixture _fixture;
    private readonly PostgresSchemaManager _schemaManager;
    private readonly MetadataRepository _metadataRepository;

    public RollupFieldTests(PostgresFixture fixture)
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
            new ProjectRepository(fixture.DataSource, new PostgresSchemaNameResolver()),
            schemaOptions);
    }

    [Fact]
    public async Task CreateTableAsync_WithRollupColumn_ShouldCreateVirtualColumn()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var uniqueSuffix = Guid.NewGuid().ToString("N")[..8];

        // Create parent table (customers) with rollup column
        var customersTable = await _schemaManager.CreateTableAsync(new CreateTableRequest
        {
            ProjectId = projectId,
            LogicalName = "rollup_customers_" + uniqueSuffix,
            Columns =
            [
                new CreateColumnRequest
                {
                    LogicalName = "name",
                    DataType = MorphDataType.Text,
                    IsNullable = false
                },
                new CreateColumnRequest
                {
                    LogicalName = "order_count",
                    DataType = MorphDataType.Integer,
                    IsNullable = true,
                    RollupConfig = new RollupColumnConfig
                    {
                        Relation = "customer_orders",
                        TargetTable = "rollup_orders_" + uniqueSuffix,
                        ForeignKeyColumn = "customer_id",
                        SourceColumn = "*",
                        Aggregation = RollupAggregation.Count
                    }
                }
            ]
        });

        // Create child table (orders) that references customers
        var ordersTable = await _schemaManager.CreateTableAsync(new CreateTableRequest
        {
            ProjectId = projectId,
            LogicalName = "rollup_orders_" + uniqueSuffix,
            Columns =
            [
                new CreateColumnRequest
                {
                    LogicalName = "customer_id",
                    DataType = MorphDataType.Uuid,
                    IsNullable = false
                },
                new CreateColumnRequest
                {
                    LogicalName = "amount",
                    DataType = MorphDataType.Decimal,
                    IsNullable = false
                }
            ]
        });

        // Assert
        customersTable.Should().NotBeNull();
        customersTable.Columns.Should().Contain(c => c.LogicalName == "name");
        customersTable.Columns.Should().Contain(c => c.LogicalName == "order_count");

        var rollupColumn = customersTable.Columns.First(c => c.LogicalName == "order_count");
        rollupColumn.IsDerived.Should().BeTrue();
        rollupColumn.RollupConfig.Should().NotBeNull();
        rollupColumn.RollupConfig!.Relation.Should().Be("customer_orders");
        rollupColumn.RollupConfig.TargetTable.Should().Be("rollup_orders_" + uniqueSuffix);
        rollupColumn.RollupConfig.ForeignKeyColumn.Should().Be("customer_id");
        rollupColumn.RollupConfig.SourceColumn.Should().Be("*");
        rollupColumn.RollupConfig.Aggregation.Should().Be(RollupAggregation.Count);

        // Virtual column should have special physical name and native type
        rollupColumn.PhysicalName.Should().StartWith("virtual_");
        rollupColumn.NativeType.Should().Be("virtual");
    }

    [Fact]
    public async Task CreateTableAsync_WithRollupColumn_ShouldPersistRollupConfig()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var uniqueSuffix = Guid.NewGuid().ToString("N")[..8];

        // Create parent table with rollup
        var parentTable = await _schemaManager.CreateTableAsync(new CreateTableRequest
        {
            ProjectId = projectId,
            LogicalName = "rollup_persist_parent_" + uniqueSuffix,
            Columns =
            [
                new CreateColumnRequest
                {
                    LogicalName = "name",
                    DataType = MorphDataType.Text,
                    IsNullable = false
                },
                new CreateColumnRequest
                {
                    LogicalName = "total_amount",
                    DataType = MorphDataType.Decimal,
                    IsNullable = true,
                    RollupConfig = new RollupColumnConfig
                    {
                        Relation = "parent_items",
                        TargetTable = "rollup_persist_items_" + uniqueSuffix,
                        ForeignKeyColumn = "parent_id",
                        SourceColumn = "amount",
                        Aggregation = RollupAggregation.Sum,
                        Filter = new RollupFilter
                        {
                            Field = "status",
                            Operator = FilterOperator.Equals,
                            Value = "active"
                        }
                    }
                }
            ]
        });

        // Act - Retrieve the table to verify persistence
        var storedTable = await _metadataRepository.GetTableByIdAsync(parentTable.TableId, includeColumns: true);

        // Assert
        storedTable.Should().NotBeNull();
        var rollupColumn = storedTable!.Columns.First(c => c.LogicalName == "total_amount");
        rollupColumn.RollupConfig.Should().NotBeNull();
        rollupColumn.RollupConfig!.Relation.Should().Be("parent_items");
        rollupColumn.RollupConfig.TargetTable.Should().Be("rollup_persist_items_" + uniqueSuffix);
        rollupColumn.RollupConfig.ForeignKeyColumn.Should().Be("parent_id");
        rollupColumn.RollupConfig.SourceColumn.Should().Be("amount");
        rollupColumn.RollupConfig.Aggregation.Should().Be(RollupAggregation.Sum);
        rollupColumn.RollupConfig.Filter.Should().NotBeNull();
        rollupColumn.RollupConfig.Filter!.Field.Should().Be("status");
        rollupColumn.RollupConfig.Filter.Operator.Should().Be(FilterOperator.Equals);
        rollupColumn.RollupConfig.Filter.Value?.ToString().Should().Be("active");
    }

    [Fact]
    public async Task AddColumnAsync_WithRollupConfig_ShouldAddVirtualColumn()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var uniqueSuffix = Guid.NewGuid().ToString("N")[..8];

        // Create parent table without rollup initially
        var parentTable = await _schemaManager.CreateTableAsync(new CreateTableRequest
        {
            ProjectId = projectId,
            LogicalName = "addrollup_parent_" + uniqueSuffix,
            Columns =
            [
                new CreateColumnRequest
                {
                    LogicalName = "name",
                    DataType = MorphDataType.Text,
                    IsNullable = false
                }
            ]
        });

        // Create child table
        var childTable = await _schemaManager.CreateTableAsync(new CreateTableRequest
        {
            ProjectId = projectId,
            LogicalName = "addrollup_child_" + uniqueSuffix,
            Columns =
            [
                new CreateColumnRequest
                {
                    LogicalName = "parent_id",
                    DataType = MorphDataType.Uuid,
                    IsNullable = false
                },
                new CreateColumnRequest
                {
                    LogicalName = "score",
                    DataType = MorphDataType.Decimal,
                    IsNullable = false
                }
            ]
        });

        var addColumnRequest = new AddColumnRequest
        {
            TableId = parentTable.TableId,
            LogicalName = "avg_score",
            DataType = MorphDataType.Decimal,
            IsNullable = true,
            ExpectedVersion = parentTable.SchemaVersion,
            RollupConfig = new RollupColumnConfig
            {
                Relation = "parent_children",
                TargetTable = "addrollup_child_" + uniqueSuffix,
                ForeignKeyColumn = "parent_id",
                SourceColumn = "score",
                Aggregation = RollupAggregation.Average
            }
        };

        // Act
        var newColumn = await _schemaManager.AddColumnAsync(addColumnRequest);

        // Assert
        newColumn.Should().NotBeNull();
        newColumn.LogicalName.Should().Be("avg_score");
        newColumn.IsDerived.Should().BeTrue();
        newColumn.RollupConfig.Should().NotBeNull();
        newColumn.RollupConfig!.Relation.Should().Be("parent_children");
        newColumn.RollupConfig.TargetTable.Should().Be("addrollup_child_" + uniqueSuffix);
        newColumn.RollupConfig.ForeignKeyColumn.Should().Be("parent_id");
        newColumn.RollupConfig.SourceColumn.Should().Be("score");
        newColumn.RollupConfig.Aggregation.Should().Be(RollupAggregation.Average);
        newColumn.PhysicalName.Should().StartWith("virtual_");
        newColumn.NativeType.Should().Be("virtual");
    }

    [Fact]
    public async Task CreateTableAsync_WithMultipleRollupColumns_ShouldCreateAllVirtuals()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var uniqueSuffix = Guid.NewGuid().ToString("N")[..8];

        // Create parent table with multiple rollups
        var parentTable = await _schemaManager.CreateTableAsync(new CreateTableRequest
        {
            ProjectId = projectId,
            LogicalName = "multirollup_parent_" + uniqueSuffix,
            Columns =
            [
                new CreateColumnRequest
                {
                    LogicalName = "name",
                    DataType = MorphDataType.Text,
                    IsNullable = false
                },
                new CreateColumnRequest
                {
                    LogicalName = "item_count",
                    DataType = MorphDataType.Integer,
                    IsNullable = true,
                    RollupConfig = new RollupColumnConfig
                    {
                        Relation = "parent_items",
                        TargetTable = "multirollup_items_" + uniqueSuffix,
                        ForeignKeyColumn = "parent_id",
                        SourceColumn = "*",
                        Aggregation = RollupAggregation.Count
                    }
                },
                new CreateColumnRequest
                {
                    LogicalName = "total_amount",
                    DataType = MorphDataType.Decimal,
                    IsNullable = true,
                    RollupConfig = new RollupColumnConfig
                    {
                        Relation = "parent_items",
                        TargetTable = "multirollup_items_" + uniqueSuffix,
                        ForeignKeyColumn = "parent_id",
                        SourceColumn = "amount",
                        Aggregation = RollupAggregation.Sum
                    }
                },
                new CreateColumnRequest
                {
                    LogicalName = "min_amount",
                    DataType = MorphDataType.Decimal,
                    IsNullable = true,
                    RollupConfig = new RollupColumnConfig
                    {
                        Relation = "parent_items",
                        TargetTable = "multirollup_items_" + uniqueSuffix,
                        ForeignKeyColumn = "parent_id",
                        SourceColumn = "amount",
                        Aggregation = RollupAggregation.Min
                    }
                },
                new CreateColumnRequest
                {
                    LogicalName = "max_amount",
                    DataType = MorphDataType.Decimal,
                    IsNullable = true,
                    RollupConfig = new RollupColumnConfig
                    {
                        Relation = "parent_items",
                        TargetTable = "multirollup_items_" + uniqueSuffix,
                        ForeignKeyColumn = "parent_id",
                        SourceColumn = "amount",
                        Aggregation = RollupAggregation.Max
                    }
                }
            ]
        });

        // Assert
        parentTable.Should().NotBeNull();

        var rollupColumns = parentTable.Columns.Where(c => c.RollupConfig != null).ToList();
        rollupColumns.Should().HaveCount(4);

        foreach (var col in rollupColumns)
        {
            col.IsDerived.Should().BeTrue();
            col.PhysicalName.Should().StartWith("virtual_");
            col.NativeType.Should().Be("virtual");
        }

        // Verify each aggregation type
        rollupColumns.First(c => c.LogicalName == "item_count").RollupConfig!.Aggregation.Should().Be(RollupAggregation.Count);
        rollupColumns.First(c => c.LogicalName == "total_amount").RollupConfig!.Aggregation.Should().Be(RollupAggregation.Sum);
        rollupColumns.First(c => c.LogicalName == "min_amount").RollupConfig!.Aggregation.Should().Be(RollupAggregation.Min);
        rollupColumns.First(c => c.LogicalName == "max_amount").RollupConfig!.Aggregation.Should().Be(RollupAggregation.Max);
    }

    [Fact]
    public async Task CreateTableAsync_RollupColumn_ShouldNotCreatePhysicalColumn()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var uniqueSuffix = Guid.NewGuid().ToString("N")[..8];

        // Create table with rollup
        var parentTable = await _schemaManager.CreateTableAsync(new CreateTableRequest
        {
            ProjectId = projectId,
            LogicalName = "physical_rollup_parent_" + uniqueSuffix,
            Columns =
            [
                new CreateColumnRequest
                {
                    LogicalName = "name",
                    DataType = MorphDataType.Text,
                    IsNullable = false
                },
                new CreateColumnRequest
                {
                    LogicalName = "child_count",
                    DataType = MorphDataType.Integer,
                    IsNullable = true,
                    RollupConfig = new RollupColumnConfig
                    {
                        Relation = "parent_children",
                        TargetTable = "physical_rollup_child_" + uniqueSuffix,
                        ForeignKeyColumn = "parent_id",
                        SourceColumn = "*",
                        Aggregation = RollupAggregation.Count
                    }
                }
            ]
        });

        // Act - Query PostgreSQL to check actual physical columns
        await using var connection = await _fixture.DataSource.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT column_name
            FROM information_schema.columns
            WHERE table_name = '{parentTable.PhysicalName}'
            ORDER BY ordinal_position
            """;

        var physicalColumns = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            physicalColumns.Add(reader.GetString(0));
        }

        // Assert
        // The rollup column "child_count" should NOT exist in the physical schema
        var nameColumn = parentTable.Columns.First(c => c.LogicalName == "name");
        var rollupColumn = parentTable.Columns.First(c => c.LogicalName == "child_count");

        // The regular column should have a physical column
        physicalColumns.Should().Contain(nameColumn.PhysicalName);

        // The virtual rollup column should NOT have a physical column
        physicalColumns.Should().NotContain(rollupColumn.PhysicalName);
        physicalColumns.Should().NotContain("child_count");

        // Verify the virtual column has the expected physical name format
        rollupColumn.PhysicalName.Should().StartWith("virtual_");
    }

    [Fact]
    public async Task CreateTableAsync_WithStringConcatRollup_ShouldStoreDelimiter()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var uniqueSuffix = Guid.NewGuid().ToString("N")[..8];

        // Create parent table with StringConcat rollup
        var parentTable = await _schemaManager.CreateTableAsync(new CreateTableRequest
        {
            ProjectId = projectId,
            LogicalName = "concat_parent_" + uniqueSuffix,
            Columns =
            [
                new CreateColumnRequest
                {
                    LogicalName = "name",
                    DataType = MorphDataType.Text,
                    IsNullable = false
                },
                new CreateColumnRequest
                {
                    LogicalName = "all_tags",
                    DataType = MorphDataType.Text,
                    IsNullable = true,
                    RollupConfig = new RollupColumnConfig
                    {
                        Relation = "parent_tags",
                        TargetTable = "concat_tags_" + uniqueSuffix,
                        ForeignKeyColumn = "parent_id",
                        SourceColumn = "tag_name",
                        Aggregation = RollupAggregation.StringConcat,
                        Delimiter = "; ",
                        OrderBy = "tag_name ASC"
                    }
                }
            ]
        });

        // Assert
        var rollupColumn = parentTable.Columns.First(c => c.LogicalName == "all_tags");
        rollupColumn.RollupConfig.Should().NotBeNull();
        rollupColumn.RollupConfig!.Aggregation.Should().Be(RollupAggregation.StringConcat);
        rollupColumn.RollupConfig.Delimiter.Should().Be("; ");
        rollupColumn.RollupConfig.OrderBy.Should().Be("tag_name ASC");
    }

    [Fact]
    public async Task CreateTableAsync_WithAllAggregationTypes_ShouldStoreCorrectly()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var uniqueSuffix = Guid.NewGuid().ToString("N")[..8];

        // Test a few different aggregation types
        var table = await _schemaManager.CreateTableAsync(new CreateTableRequest
        {
            ProjectId = projectId,
            LogicalName = "agg_types_" + uniqueSuffix,
            Columns =
            [
                new CreateColumnRequest
                {
                    LogicalName = "name",
                    DataType = MorphDataType.Text,
                    IsNullable = false
                },
                new CreateColumnRequest
                {
                    LogicalName = "percent_checked",
                    DataType = MorphDataType.Decimal,
                    IsNullable = true,
                    RollupConfig = new RollupColumnConfig
                    {
                        Relation = "items",
                        TargetTable = "agg_items_" + uniqueSuffix,
                        ForeignKeyColumn = "parent_id",
                        SourceColumn = "is_complete",
                        Aggregation = RollupAggregation.PercentChecked
                    }
                },
                new CreateColumnRequest
                {
                    LogicalName = "earliest_date",
                    DataType = MorphDataType.DateTime,
                    IsNullable = true,
                    RollupConfig = new RollupColumnConfig
                    {
                        Relation = "items",
                        TargetTable = "agg_items_" + uniqueSuffix,
                        ForeignKeyColumn = "parent_id",
                        SourceColumn = "created_at",
                        Aggregation = RollupAggregation.EarliestDate
                    }
                },
                new CreateColumnRequest
                {
                    LogicalName = "all_true",
                    DataType = MorphDataType.Boolean,
                    IsNullable = true,
                    RollupConfig = new RollupColumnConfig
                    {
                        Relation = "items",
                        TargetTable = "agg_items_" + uniqueSuffix,
                        ForeignKeyColumn = "parent_id",
                        SourceColumn = "is_valid",
                        Aggregation = RollupAggregation.AllTrue
                    }
                }
            ]
        });

        // Assert
        var percentCol = table.Columns.First(c => c.LogicalName == "percent_checked");
        percentCol.RollupConfig!.Aggregation.Should().Be(RollupAggregation.PercentChecked);

        var earliestCol = table.Columns.First(c => c.LogicalName == "earliest_date");
        earliestCol.RollupConfig!.Aggregation.Should().Be(RollupAggregation.EarliestDate);

        var allTrueCol = table.Columns.First(c => c.LogicalName == "all_true");
        allTrueCol.RollupConfig!.Aggregation.Should().Be(RollupAggregation.AllTrue);
    }
}
