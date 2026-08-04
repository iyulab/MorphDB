using MorphDB.Core.Abstractions;
using MorphDB.Core.Exceptions;
using MorphDB.Core.Models;
using MorphDB.Npgsql.Infrastructure;
using MorphDB.Npgsql.Repositories;
using MorphDB.Npgsql.Schema;
using MorphDB.Npgsql.Services;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration;

/// <summary>
/// Integration tests for PostgresSchemaManager.
/// Note: SchemaManager automatically adds system columns (_id, project_id, _created_at, _updated_at, _version).
/// Tests should only include user-defined columns in CreateTableRequest.
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
            new ProjectRepository(fixture.DataSource, new PostgresSchemaNameResolver()),
            schemaOptions);
    }

    [Fact]
    public async Task CreateTableAsync_ShouldCreateTableAndMetadata()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var request = new CreateTableRequest
        {
            ProjectId = projectId,
            LogicalName = "customers_" + Guid.NewGuid().ToString("N")[..8],
            Columns =
            [
                // Only user-defined columns (system columns are auto-added)
                new CreateColumnRequest
                {
                    LogicalName = "email",
                    DataType = MorphDataType.Text,
                    IsNullable = false
                },
                new CreateColumnRequest
                {
                    LogicalName = "name",
                    DataType = MorphDataType.Text,
                    IsNullable = true
                }
            ]
        };

        // Act
        var result = await _schemaManager.CreateTableAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.TableId.Should().NotBeEmpty();
        result.LogicalName.Should().Be(request.LogicalName);
        result.PhysicalName.Should().StartWith("tbl_");

        // 5 system columns + 2 user columns = 7 total
        result.Columns.Should().HaveCount(7);
        result.Columns.Should().Contain(c => c.LogicalName == "_id" && c.IsPrimaryKey);
        result.Columns.Should().Contain(c => c.LogicalName == "project_id");
        result.Columns.Should().Contain(c => c.LogicalName == "_created_at");
        result.Columns.Should().Contain(c => c.LogicalName == "_updated_at");
        result.Columns.Should().Contain(c => c.LogicalName == "_version");
        result.Columns.Should().Contain(c => c.LogicalName == "email");
        result.Columns.Should().Contain(c => c.LogicalName == "name");

        // Verify metadata was persisted
        var storedTable = await _metadataRepository.GetTableByIdAsync(result.TableId, includeColumns: true);
        storedTable.Should().NotBeNull();
        storedTable!.LogicalName.Should().Be(request.LogicalName);
        storedTable.Columns.Should().HaveCount(7);
    }

    [Fact]
    public async Task CreateTableAsync_WithDuplicateName_ShouldThrow()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var tableName = "duplicate_table_" + Guid.NewGuid().ToString("N")[..8];
        var request = new CreateTableRequest
        {
            ProjectId = projectId,
            LogicalName = tableName,
            Columns =
            [
                new CreateColumnRequest
                {
                    LogicalName = "data",
                    DataType = MorphDataType.Text
                }
            ]
        };

        await _schemaManager.CreateTableAsync(request);

        // Act & Assert
        var act = () => _schemaManager.CreateTableAsync(request);
        await act.Should().ThrowAsync<DuplicateNameException>();
    }

    /// <summary>
    /// Redeclaring a table is drop-and-rebuild: delete, then create the new shape under the same
    /// logical name. It is the standard evolution path for a projection layer above this one, and
    /// it used to be a one-way door — DELETE left the metadata row behind as a tombstone, still
    /// holding the derived physical name under a plain UNIQUE, so the second declaration died on a
    /// raw 23505 and the name could never be created again.
    /// </summary>
    [Fact]
    public async Task A_deleted_table_can_be_created_again_with_a_new_shape()
    {
        var projectId = Guid.NewGuid();
        var tableName = "recreated_table_" + Guid.NewGuid().ToString("N")[..8];

        var first = await _schemaManager.CreateTableAsync(new CreateTableRequest
        {
            ProjectId = projectId,
            LogicalName = tableName,
            Columns = [new CreateColumnRequest { LogicalName = "sku", DataType = MorphDataType.Text }]
        });

        await _schemaManager.DeleteTableAsync(first.TableId);

        var second = await _schemaManager.CreateTableAsync(new CreateTableRequest
        {
            ProjectId = projectId,
            LogicalName = tableName,
            Columns =
            [
                new CreateColumnRequest { LogicalName = "sku", DataType = MorphDataType.Text },
                new CreateColumnRequest { LogicalName = "warehouse", DataType = MorphDataType.Text }
            ]
        });

        second.TableId.Should().NotBe(first.TableId, "the second declaration is a new table");
        second.Columns.Should().Contain(c => c.LogicalName == "warehouse", "the new shape must take effect");

        // Third time as well: the guarantee is a property of the delete path, not a one-off pardon.
        await _schemaManager.DeleteTableAsync(second.TableId);
        var third = await _schemaManager.CreateTableAsync(new CreateTableRequest
        {
            ProjectId = projectId,
            LogicalName = tableName,
            Columns = [new CreateColumnRequest { LogicalName = "sku", DataType = MorphDataType.Text }]
        });
        third.TableId.Should().NotBe(second.TableId);
    }

    /// <summary>
    /// The tombstone had to stop occupying the name — but only for names nothing live holds.
    /// Scoping uniqueness to is_active must not become "uniqueness is optional".
    /// </summary>
    [Fact]
    public async Task Two_live_tables_still_cannot_share_a_logical_name_after_a_delete_cycle()
    {
        var projectId = Guid.NewGuid();
        var tableName = "still_unique_" + Guid.NewGuid().ToString("N")[..8];
        var request = new CreateTableRequest
        {
            ProjectId = projectId,
            LogicalName = tableName,
            Columns = [new CreateColumnRequest { LogicalName = "data", DataType = MorphDataType.Text }]
        };

        var created = await _schemaManager.CreateTableAsync(request);
        await _schemaManager.DeleteTableAsync(created.TableId);
        await _schemaManager.CreateTableAsync(request);

        var act = () => _schemaManager.CreateTableAsync(request);
        await act.Should().ThrowAsync<DuplicateNameException>(
            "a tombstone releases the name, a live table does not");
    }

    [Fact]
    public async Task AddColumnAsync_ShouldAddColumnToExistingTable()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var createTableRequest = new CreateTableRequest
        {
            ProjectId = projectId,
            LogicalName = "products_" + Guid.NewGuid().ToString("N")[..8],
            Columns =
            [
                new CreateColumnRequest
                {
                    LogicalName = "name",
                    DataType = MorphDataType.Text,
                    IsNullable = false
                }
            ]
        };

        var table = await _schemaManager.CreateTableAsync(createTableRequest);
        var initialColumnCount = table.Columns.Count;

        var addColumnRequest = new AddColumnRequest
        {
            TableId = table.TableId,
            LogicalName = "price",
            DataType = MorphDataType.Decimal,
            IsNullable = false,
            ExpectedVersion = table.SchemaVersion  // Use current schema version for optimistic concurrency
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
        storedTable!.Columns.Should().HaveCount(initialColumnCount + 1);
        storedTable.Columns.Should().Contain(c => c.LogicalName == "price");
    }

    [Fact]
    public async Task CreateIndexAsync_ShouldCreateIndex()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var createTableRequest = new CreateTableRequest
        {
            ProjectId = projectId,
            LogicalName = "orders_" + Guid.NewGuid().ToString("N")[..8],
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
        var projectId = Guid.NewGuid();

        // Create parent table (customers)
        var customersTable = await _schemaManager.CreateTableAsync(new CreateTableRequest
        {
            ProjectId = projectId,
            LogicalName = "rel_customers_" + Guid.NewGuid().ToString("N")[..8],
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

        // Create child table (orders)
        var ordersTable = await _schemaManager.CreateTableAsync(new CreateTableRequest
        {
            ProjectId = projectId,
            LogicalName = "rel_orders_" + Guid.NewGuid().ToString("N")[..8],
            Columns =
            [
                new CreateColumnRequest
                {
                    LogicalName = "customer_id",
                    DataType = MorphDataType.Uuid,
                    IsNullable = false
                }
            ]
        });

        var sourceColumn = ordersTable.Columns.First(c => c.LogicalName == "customer_id");
        var targetColumn = customersTable.Columns.First(c => c.LogicalName == "_id");

        var createRelationRequest = new CreateRelationRequest
        {
            ProjectId = projectId,
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
        var projectId = Guid.NewGuid();
        var createTableRequest = new CreateTableRequest
        {
            ProjectId = projectId,
            LogicalName = "get_test_table_" + Guid.NewGuid().ToString("N")[..8],
            Columns =
            [
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
        result.LogicalName.Should().Be(createTableRequest.LogicalName);
        // 5 system columns + 1 user column = 6 total
        result.Columns.Should().HaveCount(6);
    }

    [Fact]
    public async Task GetTableAsync_ShouldReturnTableByName()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var logicalName = "get_by_name_table_" + Guid.NewGuid().ToString("N")[..8];
        var createTableRequest = new CreateTableRequest
        {
            ProjectId = projectId,
            LogicalName = logicalName,
            Columns =
            [
                new CreateColumnRequest
                {
                    LogicalName = "data",
                    DataType = MorphDataType.Text
                }
            ]
        };

        await _schemaManager.CreateTableAsync(createTableRequest);

        // Act
        var result = await _schemaManager.GetTableAsync(projectId, logicalName);

        // Assert
        result.Should().NotBeNull();
        result!.LogicalName.Should().Be(logicalName);
    }

    [Fact]
    public async Task CreateTableAsync_WithUnderscorePrefix_ShouldSucceed()
    {
        // Arrange - Underscore-prefixed table names should be allowed (e.g. for embedding scenarios)
        var projectId = Guid.NewGuid();
        var request = new CreateTableRequest
        {
            ProjectId = projectId,
            LogicalName = "_archive_" + Guid.NewGuid().ToString("N")[..8],
            Columns =
            [
                new CreateColumnRequest
                {
                    LogicalName = "data",
                    DataType = MorphDataType.Text,
                    IsNullable = true
                }
            ]
        };

        // Act
        var result = await _schemaManager.CreateTableAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.LogicalName.Should().StartWith("_archive_");
        result.PhysicalName.Should().StartWith("tbl_");
    }

    [Fact]
    public async Task CreateTableAsync_WithMorphPrefix_ShouldThrow()
    {
        // Arrange - _morph_ prefix is reserved for system tables
        var projectId = Guid.NewGuid();
        var request = new CreateTableRequest
        {
            ProjectId = projectId,
            LogicalName = "_morph_system_" + Guid.NewGuid().ToString("N")[..8],
            Columns =
            [
                new CreateColumnRequest
                {
                    LogicalName = "data",
                    DataType = MorphDataType.Text
                }
            ]
        };

        // Act & Assert
        var act = () => _schemaManager.CreateTableAsync(request);
        await act.Should().ThrowAsync<SchemaException>()
            .Where(e => e.ErrorCode == "RESERVED_NAME");
    }

    [Fact]
    public async Task ListTablesAsync_ShouldReturnAllTablesForProject()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var uniqueSuffix = Guid.NewGuid().ToString("N")[..8];

        await _schemaManager.CreateTableAsync(new CreateTableRequest
        {
            ProjectId = projectId,
            LogicalName = "list_table_1_" + uniqueSuffix,
            Columns =
            [
                new CreateColumnRequest
                {
                    LogicalName = "data",
                    DataType = MorphDataType.Text
                }
            ]
        });

        await _schemaManager.CreateTableAsync(new CreateTableRequest
        {
            ProjectId = projectId,
            LogicalName = "list_table_2_" + uniqueSuffix,
            Columns =
            [
                new CreateColumnRequest
                {
                    LogicalName = "data",
                    DataType = MorphDataType.Text
                }
            ]
        });

        // Act
        var tables = await _schemaManager.ListTablesAsync(projectId);

        // Assert
        tables.Should().HaveCountGreaterThanOrEqualTo(2);
        tables.Should().Contain(t => t.LogicalName == "list_table_1_" + uniqueSuffix);
        tables.Should().Contain(t => t.LogicalName == "list_table_2_" + uniqueSuffix);
    }

    /// <summary>
    /// Deleting a table drops the physical table, and with it every index on it. The metadata for
    /// those parts used to stay marked live, so the control plane went on describing columns and
    /// indexes that no longer existed anywhere — and a relation kept pointing at a table that was
    /// gone. Hiding the physical schema is what obliges this layer to keep it tidy.
    /// </summary>
    [Fact]
    public async Task Deleting_a_table_retires_its_columns_indexes_and_relations_with_it()
    {
        var projectId = Guid.NewGuid();
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var parent = await _schemaManager.CreateTableAsync(new CreateTableRequest
        {
            ProjectId = projectId,
            LogicalName = "cascade_parent_" + suffix,
            // Unique: a relation targets a key, and PostgreSQL refuses a foreign key to a column
            // nothing guarantees is unique.
            Columns = [new CreateColumnRequest { LogicalName = "code", DataType = MorphDataType.Text, IsUnique = true }]
        });
        var child = await _schemaManager.CreateTableAsync(new CreateTableRequest
        {
            ProjectId = projectId,
            LogicalName = "cascade_child_" + suffix,
            Columns = [new CreateColumnRequest { LogicalName = "parent_code", DataType = MorphDataType.Text }]
        });

        await _schemaManager.CreateIndexAsync(new CreateIndexRequest
        {
            TableId = parent.TableId,
            LogicalName = "ix_cascade_" + suffix,
            ColumnIds = [parent.Columns.First(c => c.LogicalName == "code").ColumnId]
        });
        await _schemaManager.CreateRelationAsync(new CreateRelationRequest
        {
            ProjectId = projectId,
            LogicalName = "rel_cascade_" + suffix,
            SourceTableId = child.TableId,
            SourceColumnId = child.Columns.First(c => c.LogicalName == "parent_code").ColumnId,
            TargetTableId = parent.TableId,
            TargetColumnId = parent.Columns.First(c => c.LogicalName == "code").ColumnId,
            RelationType = RelationType.OneToMany
        });

        // The child holds the foreign key, so it is the side that can be dropped. (Deleting the
        // target side is a separate matter — the physical DROP has nothing to cascade with.)
        await _schemaManager.CreateIndexAsync(new CreateIndexRequest
        {
            TableId = child.TableId,
            LogicalName = "ix_cascade_child_" + suffix,
            ColumnIds = [child.Columns.First(c => c.LogicalName == "parent_code").ColumnId]
        });

        (await _metadataRepository.GetColumnsByTableIdAsync(child.TableId)).Should().NotBeEmpty();
        (await _metadataRepository.GetIndexesByTableIdAsync(child.TableId)).Should().NotBeEmpty();
        (await _metadataRepository.GetRelationsByTableIdAsync(child.TableId)).Should().NotBeEmpty();

        await _schemaManager.DeleteTableAsync(child.TableId);

        (await _metadataRepository.GetColumnsByTableIdAsync(child.TableId))
            .Should().BeEmpty("the columns went with the table that held them");
        (await _metadataRepository.GetIndexesByTableIdAsync(child.TableId))
            .Should().BeEmpty("dropping the table dropped its indexes physically too");
        (await _metadataRepository.GetRelationsByTableIdAsync(child.TableId))
            .Should().BeEmpty("a relation whose source table is gone is not a relation");

        // The cascade follows the deleted table, not the project: the other side is untouched.
        (await _metadataRepository.GetColumnsByTableIdAsync(parent.TableId))
            .Should().NotBeEmpty("the surviving table keeps its columns");
        (await _metadataRepository.GetIndexesByTableIdAsync(parent.TableId))
            .Should().NotBeEmpty("and its indexes");
    }

    /// <summary>
    /// A relation is a foreign key on the other table, and the DROP carries no CASCADE — deleting
    /// the referenced side is refused by PostgreSQL. That refusal used to escape as an opaque 500
    /// quoting a physical table name the caller is not supposed to know exists.
    /// </summary>
    [Fact]
    public async Task Deleting_a_table_another_one_references_says_so_instead_of_failing_opaquely()
    {
        var projectId = Guid.NewGuid();
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var target = await _schemaManager.CreateTableAsync(new CreateTableRequest
        {
            ProjectId = projectId,
            LogicalName = "referenced_" + suffix,
            Columns = [new CreateColumnRequest { LogicalName = "code", DataType = MorphDataType.Text, IsUnique = true }]
        });
        var source = await _schemaManager.CreateTableAsync(new CreateTableRequest
        {
            ProjectId = projectId,
            LogicalName = "referencing_" + suffix,
            Columns = [new CreateColumnRequest { LogicalName = "target_code", DataType = MorphDataType.Text }]
        });

        await _schemaManager.CreateRelationAsync(new CreateRelationRequest
        {
            ProjectId = projectId,
            LogicalName = "rel_dependent_" + suffix,
            SourceTableId = source.TableId,
            SourceColumnId = source.Columns.First(c => c.LogicalName == "target_code").ColumnId,
            TargetTableId = target.TableId,
            TargetColumnId = target.Columns.First(c => c.LogicalName == "code").ColumnId,
            RelationType = RelationType.OneToMany
        });

        var act = () => _schemaManager.DeleteTableAsync(target.TableId);

        var thrown = await act.Should().ThrowAsync<SchemaException>();
        thrown.Which.ErrorCode.Should().Be("TABLE_HAS_DEPENDENTS");
        thrown.Which.Message.Should().Contain(target.LogicalName, "the caller is told which table, by the name they gave it");
        thrown.Which.Message.Should().NotContain(target.PhysicalName, "the physical name is hidden layer");
    }
}
