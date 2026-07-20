using MorphDB.Core.Abstractions;
using MorphDB.Core.Models;
using MorphDB.Npgsql.Infrastructure;
using MorphDB.Npgsql.Repositories;
using MorphDB.Npgsql.Services;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration;

/// <summary>
/// Integration tests for Lookup Field functionality.
/// Lookup fields are virtual columns that reference data from related tables.
/// </summary>
[Collection("PostgreSQL")]
public class LookupFieldTests
{
    private readonly PostgresFixture _fixture;
    private readonly PostgresSchemaManager _schemaManager;
    private readonly MetadataRepository _metadataRepository;

    public LookupFieldTests(PostgresFixture fixture)
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
    }

    [Fact]
    public async Task CreateTableAsync_WithLookupColumn_ShouldCreateVirtualColumn()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var uniqueSuffix = Guid.NewGuid().ToString("N")[..8];

        // Create parent table (customers)
        var customersTable = await _schemaManager.CreateTableAsync(new CreateTableRequest
        {
            ProjectId = projectId,
            LogicalName = "lookup_customers_" + uniqueSuffix,
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
                    LogicalName = "email",
                    DataType = MorphDataType.Text,
                    IsNullable = true
                }
            ]
        });

        // Create child table (orders) with lookup field
        var request = new CreateTableRequest
        {
            ProjectId = projectId,
            LogicalName = "lookup_orders_" + uniqueSuffix,
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
                    LogicalName = "customer_name",
                    DataType = MorphDataType.Text,
                    IsNullable = true,
                    LookupConfig = new LookupColumnConfig
                    {
                        RelationColumn = "customer_id",
                        TargetTable = "lookup_customers_" + uniqueSuffix,
                        TargetColumn = "name",
                        OnDelete = LookupDeleteAction.SetNull,
                        AllowMultiple = false
                    }
                }
            ]
        };

        // Act
        var result = await _schemaManager.CreateTableAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.Columns.Should().Contain(c => c.LogicalName == "customer_id");
        result.Columns.Should().Contain(c => c.LogicalName == "customer_name");

        var lookupColumn = result.Columns.First(c => c.LogicalName == "customer_name");
        lookupColumn.IsDerived.Should().BeTrue();
        lookupColumn.LookupConfig.Should().NotBeNull();
        lookupColumn.LookupConfig!.RelationColumn.Should().Be("customer_id");
        lookupColumn.LookupConfig.TargetTable.Should().Be("lookup_customers_" + uniqueSuffix);
        lookupColumn.LookupConfig.TargetColumn.Should().Be("name");
        lookupColumn.LookupConfig.OnDelete.Should().Be(LookupDeleteAction.SetNull);
        lookupColumn.LookupConfig.AllowMultiple.Should().BeFalse();

        // Virtual column should have special physical name and native type
        lookupColumn.PhysicalName.Should().StartWith("virtual_");
        lookupColumn.NativeType.Should().Be("virtual");
    }

    [Fact]
    public async Task CreateTableAsync_WithLookupColumn_ShouldPersistLookupConfig()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var uniqueSuffix = Guid.NewGuid().ToString("N")[..8];

        // Create parent table
        var parentTable = await _schemaManager.CreateTableAsync(new CreateTableRequest
        {
            ProjectId = projectId,
            LogicalName = "lookup_parent_" + uniqueSuffix,
            Columns =
            [
                new CreateColumnRequest
                {
                    LogicalName = "title",
                    DataType = MorphDataType.Text,
                    IsNullable = false
                }
            ]
        });

        // Create child table with lookup
        var childTable = await _schemaManager.CreateTableAsync(new CreateTableRequest
        {
            ProjectId = projectId,
            LogicalName = "lookup_child_" + uniqueSuffix,
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
                    LogicalName = "parent_title",
                    DataType = MorphDataType.Text,
                    IsNullable = true,
                    LookupConfig = new LookupColumnConfig
                    {
                        RelationColumn = "parent_id",
                        TargetTable = "lookup_parent_" + uniqueSuffix,
                        TargetColumn = "title",
                        OnDelete = LookupDeleteAction.Clear,
                        AllowMultiple = false
                    }
                }
            ]
        });

        // Act - Retrieve the table to verify persistence
        var storedTable = await _metadataRepository.GetTableByIdAsync(childTable.TableId, includeColumns: true);

        // Assert
        storedTable.Should().NotBeNull();
        var lookupColumn = storedTable!.Columns.First(c => c.LogicalName == "parent_title");
        lookupColumn.LookupConfig.Should().NotBeNull();
        lookupColumn.LookupConfig!.RelationColumn.Should().Be("parent_id");
        lookupColumn.LookupConfig.TargetTable.Should().Be("lookup_parent_" + uniqueSuffix);
        lookupColumn.LookupConfig.TargetColumn.Should().Be("title");
        lookupColumn.LookupConfig.OnDelete.Should().Be(LookupDeleteAction.Clear);
    }

    [Fact]
    public async Task AddColumnAsync_WithLookupConfig_ShouldAddVirtualColumn()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var uniqueSuffix = Guid.NewGuid().ToString("N")[..8];

        // Create parent table
        var parentTable = await _schemaManager.CreateTableAsync(new CreateTableRequest
        {
            ProjectId = projectId,
            LogicalName = "addcol_parent_" + uniqueSuffix,
            Columns =
            [
                new CreateColumnRequest
                {
                    LogicalName = "status",
                    DataType = MorphDataType.Text,
                    IsNullable = false
                }
            ]
        });

        // Create child table without lookup initially
        var childTable = await _schemaManager.CreateTableAsync(new CreateTableRequest
        {
            ProjectId = projectId,
            LogicalName = "addcol_child_" + uniqueSuffix,
            Columns =
            [
                new CreateColumnRequest
                {
                    LogicalName = "parent_id",
                    DataType = MorphDataType.Uuid,
                    IsNullable = false
                }
            ]
        });

        var addColumnRequest = new AddColumnRequest
        {
            TableId = childTable.TableId,
            LogicalName = "parent_status",
            DataType = MorphDataType.Text,
            IsNullable = true,
            ExpectedVersion = childTable.SchemaVersion,
            LookupConfig = new LookupColumnConfig
            {
                RelationColumn = "parent_id",
                TargetTable = "addcol_parent_" + uniqueSuffix,
                TargetColumn = "status",
                OnDelete = LookupDeleteAction.SetNull,
                AllowMultiple = false
            }
        };

        // Act
        var newColumn = await _schemaManager.AddColumnAsync(addColumnRequest);

        // Assert
        newColumn.Should().NotBeNull();
        newColumn.LogicalName.Should().Be("parent_status");
        newColumn.IsDerived.Should().BeTrue();
        newColumn.LookupConfig.Should().NotBeNull();
        newColumn.LookupConfig!.RelationColumn.Should().Be("parent_id");
        newColumn.LookupConfig.TargetTable.Should().Be("addcol_parent_" + uniqueSuffix);
        newColumn.LookupConfig.TargetColumn.Should().Be("status");
        newColumn.PhysicalName.Should().StartWith("virtual_");
        newColumn.NativeType.Should().Be("virtual");
    }

    [Fact]
    public async Task CreateTableAsync_WithMultipleLookupColumns_ShouldCreateAllVirtuals()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var uniqueSuffix = Guid.NewGuid().ToString("N")[..8];

        // Create customers table
        var customersTable = await _schemaManager.CreateTableAsync(new CreateTableRequest
        {
            ProjectId = projectId,
            LogicalName = "multi_customers_" + uniqueSuffix,
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
                    LogicalName = "email",
                    DataType = MorphDataType.Text,
                    IsNullable = true
                }
            ]
        });

        // Create products table
        var productsTable = await _schemaManager.CreateTableAsync(new CreateTableRequest
        {
            ProjectId = projectId,
            LogicalName = "multi_products_" + uniqueSuffix,
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
                    LogicalName = "price",
                    DataType = MorphDataType.Decimal,
                    IsNullable = false
                }
            ]
        });

        // Create orders table with multiple lookups
        var ordersTable = await _schemaManager.CreateTableAsync(new CreateTableRequest
        {
            ProjectId = projectId,
            LogicalName = "multi_orders_" + uniqueSuffix,
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
                    LogicalName = "product_id",
                    DataType = MorphDataType.Uuid,
                    IsNullable = false
                },
                new CreateColumnRequest
                {
                    LogicalName = "customer_name",
                    DataType = MorphDataType.Text,
                    IsNullable = true,
                    LookupConfig = new LookupColumnConfig
                    {
                        RelationColumn = "customer_id",
                        TargetTable = "multi_customers_" + uniqueSuffix,
                        TargetColumn = "name",
                        OnDelete = LookupDeleteAction.SetNull
                    }
                },
                new CreateColumnRequest
                {
                    LogicalName = "customer_email",
                    DataType = MorphDataType.Text,
                    IsNullable = true,
                    LookupConfig = new LookupColumnConfig
                    {
                        RelationColumn = "customer_id",
                        TargetTable = "multi_customers_" + uniqueSuffix,
                        TargetColumn = "email",
                        OnDelete = LookupDeleteAction.SetNull
                    }
                },
                new CreateColumnRequest
                {
                    LogicalName = "product_name",
                    DataType = MorphDataType.Text,
                    IsNullable = true,
                    LookupConfig = new LookupColumnConfig
                    {
                        RelationColumn = "product_id",
                        TargetTable = "multi_products_" + uniqueSuffix,
                        TargetColumn = "name",
                        OnDelete = LookupDeleteAction.SetNull
                    }
                },
                new CreateColumnRequest
                {
                    LogicalName = "product_price",
                    DataType = MorphDataType.Decimal,
                    IsNullable = true,
                    LookupConfig = new LookupColumnConfig
                    {
                        RelationColumn = "product_id",
                        TargetTable = "multi_products_" + uniqueSuffix,
                        TargetColumn = "price",
                        OnDelete = LookupDeleteAction.SetNull
                    }
                }
            ]
        });

        // Assert
        ordersTable.Should().NotBeNull();

        // Should have 5 system columns + 6 user columns = 11 total
        // (2 regular columns + 4 lookup columns)
        ordersTable.Columns.Should().HaveCount(11);

        var lookupColumns = ordersTable.Columns.Where(c => c.LookupConfig != null).ToList();
        lookupColumns.Should().HaveCount(4);

        foreach (var col in lookupColumns)
        {
            col.IsDerived.Should().BeTrue();
            col.PhysicalName.Should().StartWith("virtual_");
            col.NativeType.Should().Be("virtual");
        }
    }

    [Fact]
    public async Task CreateTableAsync_LookupColumn_ShouldNotCreatePhysicalColumn()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var uniqueSuffix = Guid.NewGuid().ToString("N")[..8];

        // Create parent table
        var parentTable = await _schemaManager.CreateTableAsync(new CreateTableRequest
        {
            ProjectId = projectId,
            LogicalName = "physical_parent_" + uniqueSuffix,
            Columns =
            [
                new CreateColumnRequest
                {
                    LogicalName = "value",
                    DataType = MorphDataType.Text,
                    IsNullable = false
                }
            ]
        });

        // Create child table with lookup
        var childTable = await _schemaManager.CreateTableAsync(new CreateTableRequest
        {
            ProjectId = projectId,
            LogicalName = "physical_child_" + uniqueSuffix,
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
                    LogicalName = "parent_value",
                    DataType = MorphDataType.Text,
                    IsNullable = true,
                    LookupConfig = new LookupColumnConfig
                    {
                        RelationColumn = "parent_id",
                        TargetTable = "physical_parent_" + uniqueSuffix,
                        TargetColumn = "value",
                        OnDelete = LookupDeleteAction.SetNull
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
            WHERE table_name = '{childTable.PhysicalName}'
            ORDER BY ordinal_position
            """;

        var physicalColumns = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            physicalColumns.Add(reader.GetString(0));
        }

        // Assert
        // The lookup column "parent_value" should NOT exist in the physical schema
        // Physical columns use hash names (col_xxx), but virtual columns don't create physical columns at all
        var parentIdColumn = childTable.Columns.First(c => c.LogicalName == "parent_id");
        var lookupColumn = childTable.Columns.First(c => c.LogicalName == "parent_value");

        // The FK column should have a physical column
        physicalColumns.Should().Contain(parentIdColumn.PhysicalName);

        // The virtual lookup column should NOT have a physical column
        physicalColumns.Should().NotContain(lookupColumn.PhysicalName);
        physicalColumns.Should().NotContain("parent_value");

        // Verify the virtual column has the expected physical name format
        lookupColumn.PhysicalName.Should().StartWith("virtual_");
    }
}
