using MorphDB.Core.Abstractions;
using MorphDB.Core.Exceptions;
using MorphDB.Core.Models;
using MorphDB.Npgsql.Infrastructure;
using MorphDB.Npgsql.Repositories;
using MorphDB.Npgsql.Services;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration;

/// <summary>
/// Integration tests for PostgresSchemaManager.
/// </summary>
[Collection("PostgreSQL")]
public class SchemaManagerTests
{
    private readonly PostgresFixture _fixture;
    private readonly PostgresSchemaManager _schemaManager;
    private readonly MetadataRepository _metadataRepository;

    public SchemaManagerTests(PostgresFixture fixture)
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
    public async Task CreateTableAsync_ShouldCreateTableAndMetadata()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var request = new CreateTableRequest
        {
            TenantId = tenantId,
            LogicalName = "customers",
            Columns =
            [
                new CreateColumnRequest
                {
                    LogicalName = "id",
                    DataType = MorphDataType.Uuid,
                    IsPrimaryKey = true
                },
                new CreateColumnRequest
                {
                    LogicalName = "email",
                    DataType = MorphDataType.Text,
                    IsNullable = false
                },
                new CreateColumnRequest
                {
                    LogicalName = "created_at",
                    DataType = MorphDataType.DateTime,
                    IsNullable = false
                }
            ]
        };

        // Act
        var result = await _schemaManager.CreateTableAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.TableId.Should().NotBeEmpty();
        result.LogicalName.Should().Be("customers");
        result.PhysicalName.Should().StartWith("t_");
        result.Columns.Should().HaveCount(3);
        result.Columns.Should().Contain(c => c.LogicalName == "id" && c.IsPrimaryKey);
        result.Columns.Should().Contain(c => c.LogicalName == "email");
        result.Columns.Should().Contain(c => c.LogicalName == "created_at");

        // Verify metadata was persisted
        var storedTable = await _metadataRepository.GetTableByIdAsync(result.TableId, includeColumns: true);
        storedTable.Should().NotBeNull();
        storedTable!.LogicalName.Should().Be("customers");
        storedTable.Columns.Should().HaveCount(3);
    }

    [Fact]
    public async Task CreateTableAsync_WithDuplicateName_ShouldThrow()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var request = new CreateTableRequest
        {
            TenantId = tenantId,
            LogicalName = "duplicate_table",
            Columns =
            [
                new CreateColumnRequest
                {
                    LogicalName = "id",
                    DataType = MorphDataType.Uuid,
                    IsPrimaryKey = true
                }
            ]
        };

        await _schemaManager.CreateTableAsync(request);

        // Act & Assert
        var act = () => _schemaManager.CreateTableAsync(request);
        await act.Should().ThrowAsync<DuplicateNameException>();
    }

    [Fact]
    public async Task AddColumnAsync_ShouldAddColumnToExistingTable()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var createTableRequest = new CreateTableRequest
        {
            TenantId = tenantId,
            LogicalName = "products",
            Columns =
            [
                new CreateColumnRequest
                {
                    LogicalName = "id",
                    DataType = MorphDataType.Uuid,
                    IsPrimaryKey = true
                },
                new CreateColumnRequest
                {
                    LogicalName = "name",
                    DataType = MorphDataType.Text,
                    IsNullable = false
                }
            ]
        };

        var table = await _schemaManager.CreateTableAsync(createTableRequest);

        var addColumnRequest = new AddColumnRequest
        {
            TableId = table.TableId,
            LogicalName = "price",
            DataType = MorphDataType.Decimal,
            IsNullable = false
        };

        // Act
        var column = await _schemaManager.AddColumnAsync(addColumnRequest);

        // Assert
        column.Should().NotBeNull();
        column.LogicalName.Should().Be("price");
        column.DataType.Should().Be(MorphDataType.Decimal);
        column.IsNullable.Should().BeFalse();

        // Verify metadata was updated
        var storedTable = await _metadataRepository.GetTableByIdAsync(table.TableId, includeColumns: true);
        storedTable!.Columns.Should().HaveCount(3);
        storedTable.Columns.Should().Contain(c => c.LogicalName == "price");
    }

    [Fact]
    public async Task CreateIndexAsync_ShouldCreateIndex()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var createTableRequest = new CreateTableRequest
        {
            TenantId = tenantId,
            LogicalName = "orders",
            Columns =
            [
                new CreateColumnRequest
                {
                    LogicalName = "id",
                    DataType = MorphDataType.Uuid,
                    IsPrimaryKey = true
                },
                new CreateColumnRequest
                {
                    LogicalName = "customer_id",
                    DataType = MorphDataType.Uuid,
                    IsNullable = false
                },
                new CreateColumnRequest
                {
                    LogicalName = "order_date",
                    DataType = MorphDataType.DateTime,
                    IsNullable = false
                }
            ]
        };

        var table = await _schemaManager.CreateTableAsync(createTableRequest);
        var customerIdColumn = table.Columns.First(c => c.LogicalName == "customer_id");

        var createIndexRequest = new CreateIndexRequest
        {
            TableId = table.TableId,
            LogicalName = "idx_orders_customer",
            ColumnIds = [customerIdColumn.ColumnId],
            IndexType = IndexType.BTree,
            IsUnique = false
        };

        // Act
        var index = await _schemaManager.CreateIndexAsync(createIndexRequest);

        // Assert
        index.Should().NotBeNull();
        index.LogicalName.Should().Be("idx_orders_customer");
        index.IndexType.Should().Be(IndexType.BTree);
        index.IsUnique.Should().BeFalse();

        // Verify metadata was persisted
        var indexes = await _metadataRepository.GetIndexesByTableIdAsync(table.TableId);
        indexes.Should().Contain(i => i.LogicalName == "idx_orders_customer");
    }

    [Fact]
    public async Task CreateRelationAsync_ShouldCreateForeignKey()
    {
        // Arrange
        var tenantId = Guid.NewGuid();

        // Create parent table (customers)
        var customersTable = await _schemaManager.CreateTableAsync(new CreateTableRequest
        {
            TenantId = tenantId,
            LogicalName = "rel_customers",
            Columns =
            [
                new CreateColumnRequest
                {
                    LogicalName = "id",
                    DataType = MorphDataType.Uuid,
                    IsPrimaryKey = true
                },
                new CreateColumnRequest
                {
                    LogicalName = "name",
                    DataType = MorphDataType.Text,
                    IsNullable = false
                }
            ]
        });

        // Create child table (orders)
        var ordersTable = await _schemaManager.CreateTableAsync(new CreateTableRequest
        {
            TenantId = tenantId,
            LogicalName = "rel_orders",
            Columns =
            [
                new CreateColumnRequest
                {
                    LogicalName = "id",
                    DataType = MorphDataType.Uuid,
                    IsPrimaryKey = true
                },
                new CreateColumnRequest
                {
                    LogicalName = "customer_id",
                    DataType = MorphDataType.Uuid,
                    IsNullable = false
                }
            ]
        });

        var sourceColumn = ordersTable.Columns.First(c => c.LogicalName == "customer_id");
        var targetColumn = customersTable.Columns.First(c => c.LogicalName == "id");

        var createRelationRequest = new CreateRelationRequest
        {
            TenantId = tenantId,
            LogicalName = "fk_orders_customer",
            SourceTableId = ordersTable.TableId,
            SourceColumnId = sourceColumn.ColumnId,
            TargetTableId = customersTable.TableId,
            TargetColumnId = targetColumn.ColumnId,
            RelationType = RelationType.OneToMany,
            OnDelete = OnDeleteAction.Cascade
        };

        // Act
        var relation = await _schemaManager.CreateRelationAsync(createRelationRequest);

        // Assert
        relation.Should().NotBeNull();
        relation.LogicalName.Should().Be("fk_orders_customer");
        relation.RelationType.Should().Be(RelationType.OneToMany);
        relation.OnDelete.Should().Be(OnDeleteAction.Cascade);

        // Verify metadata was persisted
        var relations = await _metadataRepository.GetRelationsByTableIdAsync(ordersTable.TableId);
        relations.Should().Contain(r => r.LogicalName == "fk_orders_customer");
    }

    [Fact]
    public async Task GetTableByIdAsync_ShouldReturnTableWithColumns()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var createTableRequest = new CreateTableRequest
        {
            TenantId = tenantId,
            LogicalName = "get_test_table",
            Columns =
            [
                new CreateColumnRequest
                {
                    LogicalName = "id",
                    DataType = MorphDataType.Uuid,
                    IsPrimaryKey = true
                },
                new CreateColumnRequest
                {
                    LogicalName = "data",
                    DataType = MorphDataType.Json,
                    IsNullable = true
                }
            ]
        };

        var created = await _schemaManager.CreateTableAsync(createTableRequest);

        // Act
        var result = await _schemaManager.GetTableByIdAsync(created.TableId);

        // Assert
        result.Should().NotBeNull();
        result!.TableId.Should().Be(created.TableId);
        result.LogicalName.Should().Be("get_test_table");
        result.Columns.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetTableAsync_ShouldReturnTableByName()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var logicalName = "get_by_name_table_" + Guid.NewGuid().ToString("N")[..8];
        var createTableRequest = new CreateTableRequest
        {
            TenantId = tenantId,
            LogicalName = logicalName,
            Columns =
            [
                new CreateColumnRequest
                {
                    LogicalName = "id",
                    DataType = MorphDataType.Uuid,
                    IsPrimaryKey = true
                }
            ]
        };

        await _schemaManager.CreateTableAsync(createTableRequest);

        // Act
        var result = await _schemaManager.GetTableAsync(tenantId, logicalName);

        // Assert
        result.Should().NotBeNull();
        result!.LogicalName.Should().Be(logicalName);
    }

    [Fact]
    public async Task ListTablesAsync_ShouldReturnAllTablesForTenant()
    {
        // Arrange
        var tenantId = Guid.NewGuid();

        await _schemaManager.CreateTableAsync(new CreateTableRequest
        {
            TenantId = tenantId,
            LogicalName = "list_table_1",
            Columns =
            [
                new CreateColumnRequest
                {
                    LogicalName = "id",
                    DataType = MorphDataType.Uuid,
                    IsPrimaryKey = true
                }
            ]
        });

        await _schemaManager.CreateTableAsync(new CreateTableRequest
        {
            TenantId = tenantId,
            LogicalName = "list_table_2",
            Columns =
            [
                new CreateColumnRequest
                {
                    LogicalName = "id",
                    DataType = MorphDataType.Uuid,
                    IsPrimaryKey = true
                }
            ]
        });

        // Act
        var tables = await _schemaManager.ListTablesAsync(tenantId);

        // Assert
        tables.Should().HaveCountGreaterThanOrEqualTo(2);
        tables.Should().Contain(t => t.LogicalName == "list_table_1");
        tables.Should().Contain(t => t.LogicalName == "list_table_2");
    }
}
