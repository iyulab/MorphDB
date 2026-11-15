using MorphDB.Core.Models;
using MorphDB.Npgsql.Repositories;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration;

/// <summary>
/// Integration tests for MetadataRepository.
/// </summary>
[Collection("PostgreSQL")]
public class MetadataRepositoryTests
{
    private readonly PostgresFixture _fixture;
    private readonly MetadataRepository _repository;

    public MetadataRepositoryTests(PostgresFixture fixture)
    {
        _fixture = fixture;
        _repository = new MetadataRepository(fixture.Connections);
    }

    #region Table CRUD Tests

    [Fact]
    public async Task InsertTableAsync_ShouldInsertAndReturn()
    {
        // Arrange
        var table = new TableMetadata
        {
            TableId = Guid.NewGuid(),
            ProjectId = Guid.NewGuid(),
            LogicalName = "test_table_" + Guid.NewGuid().ToString("N")[..8],
            PhysicalName = "t_" + Guid.NewGuid().ToString("N")[..16],
            SchemaVersion = 1
        };

        // Act
        var result = await _repository.InsertTableAsync(table, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Should().NotBeNull();
        result.TableId.Should().Be(table.TableId);
        result.LogicalName.Should().Be(table.LogicalName);
        result.PhysicalName.Should().Be(table.PhysicalName);
        result.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task GetTableByIdAsync_ShouldReturnTable()
    {
        // Arrange
        var table = new TableMetadata
        {
            TableId = Guid.NewGuid(),
            ProjectId = Guid.NewGuid(),
            LogicalName = "test_table_" + Guid.NewGuid().ToString("N")[..8],
            PhysicalName = "t_" + Guid.NewGuid().ToString("N")[..16],
            SchemaVersion = 1
        };
        await _repository.InsertTableAsync(table, cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var result = await _repository.GetTableByIdAsync(table.TableId, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Should().NotBeNull();
        result!.TableId.Should().Be(table.TableId);
        result.LogicalName.Should().Be(table.LogicalName);
    }

    [Fact]
    public async Task GetTableByIdAsync_WithNonExistentId_ShouldReturnNull()
    {
        // Act
        var result = await _repository.GetTableByIdAsync(Guid.NewGuid(), cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetTableByNameAsync_ShouldReturnTable()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var table = new TableMetadata
        {
            TableId = Guid.NewGuid(),
            ProjectId = projectId,
            LogicalName = "test_table_" + Guid.NewGuid().ToString("N")[..8],
            PhysicalName = "t_" + Guid.NewGuid().ToString("N")[..16],
            SchemaVersion = 1
        };
        await _repository.InsertTableAsync(table, cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var result = await _repository.GetTableByNameAsync(projectId, table.LogicalName, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Should().NotBeNull();
        result!.LogicalName.Should().Be(table.LogicalName);
    }

    [Fact]
    public async Task SoftDeleteTableAsync_ShouldMarkAsInactive()
    {
        // Arrange
        var table = new TableMetadata
        {
            TableId = Guid.NewGuid(),
            ProjectId = Guid.NewGuid(),
            LogicalName = "test_table_" + Guid.NewGuid().ToString("N")[..8],
            PhysicalName = "t_" + Guid.NewGuid().ToString("N")[..16],
            SchemaVersion = 1
        };
        await _repository.InsertTableAsync(table, cancellationToken: TestContext.Current.CancellationToken);

        // Act
        await _repository.SoftDeleteTableAsync(table.TableId, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var result = await _repository.GetTableByIdAsync(table.TableId, cancellationToken: TestContext.Current.CancellationToken);
        result.Should().BeNull(); // Soft deleted tables are not returned
    }

    #endregion

    #region Column CRUD Tests

    [Fact]
    public async Task InsertColumnAsync_ShouldInsertAndReturn()
    {
        // Arrange
        var table = new TableMetadata
        {
            TableId = Guid.NewGuid(),
            ProjectId = Guid.NewGuid(),
            LogicalName = "test_table_" + Guid.NewGuid().ToString("N")[..8],
            PhysicalName = "t_" + Guid.NewGuid().ToString("N")[..16],
            SchemaVersion = 1
        };
        await _repository.InsertTableAsync(table, cancellationToken: TestContext.Current.CancellationToken);

        var column = new ColumnMetadata
        {
            ColumnId = Guid.NewGuid(),
            TableId = table.TableId,
            LogicalName = "test_column",
            PhysicalName = "c_test_column",
            DataType = MorphDataType.Text,
            NativeType = "TEXT",
            IsNullable = true,
            OrdinalPosition = 1
        };

        // Act
        var result = await _repository.InsertColumnAsync(column, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Should().NotBeNull();
        result.ColumnId.Should().Be(column.ColumnId);
        result.LogicalName.Should().Be("test_column");
        result.DataType.Should().Be(MorphDataType.Text);
    }

    [Fact]
    public async Task GetColumnsByTableIdAsync_ShouldReturnAllColumns()
    {
        // Arrange
        var table = new TableMetadata
        {
            TableId = Guid.NewGuid(),
            ProjectId = Guid.NewGuid(),
            LogicalName = "test_table_" + Guid.NewGuid().ToString("N")[..8],
            PhysicalName = "t_" + Guid.NewGuid().ToString("N")[..16],
            SchemaVersion = 1
        };
        await _repository.InsertTableAsync(table, cancellationToken: TestContext.Current.CancellationToken);

        var column1 = new ColumnMetadata
        {
            ColumnId = Guid.NewGuid(),
            TableId = table.TableId,
            LogicalName = "col1",
            PhysicalName = "c_col1",
            DataType = MorphDataType.Text,
            NativeType = "TEXT",
            OrdinalPosition = 1
        };

        var column2 = new ColumnMetadata
        {
            ColumnId = Guid.NewGuid(),
            TableId = table.TableId,
            LogicalName = "col2",
            PhysicalName = "c_col2",
            DataType = MorphDataType.Integer,
            NativeType = "INTEGER",
            OrdinalPosition = 2
        };

        await _repository.InsertColumnAsync(column1, cancellationToken: TestContext.Current.CancellationToken);
        await _repository.InsertColumnAsync(column2, cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var columns = await _repository.GetColumnsByTableIdAsync(table.TableId, TestContext.Current.CancellationToken);

        // Assert
        columns.Should().HaveCount(2);
        columns.Should().Contain(c => c.LogicalName == "col1");
        columns.Should().Contain(c => c.LogicalName == "col2");
    }

    [Fact]
    public async Task GetNextOrdinalPositionAsync_ShouldReturnCorrectValue()
    {
        // Arrange
        var table = new TableMetadata
        {
            TableId = Guid.NewGuid(),
            ProjectId = Guid.NewGuid(),
            LogicalName = "test_table_" + Guid.NewGuid().ToString("N")[..8],
            PhysicalName = "t_" + Guid.NewGuid().ToString("N")[..16],
            SchemaVersion = 1
        };
        await _repository.InsertTableAsync(table, cancellationToken: TestContext.Current.CancellationToken);

        var column = new ColumnMetadata
        {
            ColumnId = Guid.NewGuid(),
            TableId = table.TableId,
            LogicalName = "col1",
            PhysicalName = "c_col1",
            DataType = MorphDataType.Text,
            NativeType = "TEXT",
            OrdinalPosition = 1
        };
        await _repository.InsertColumnAsync(column, cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var nextPosition = await _repository.GetNextOrdinalPositionAsync(table.TableId, TestContext.Current.CancellationToken);

        // Assert
        nextPosition.Should().Be(2);
    }

    #endregion

    #region Index CRUD Tests

    [Fact]
    public async Task InsertIndexAsync_ShouldInsertAndReturn()
    {
        // Arrange
        var table = new TableMetadata
        {
            TableId = Guid.NewGuid(),
            ProjectId = Guid.NewGuid(),
            LogicalName = "test_table_" + Guid.NewGuid().ToString("N")[..8],
            PhysicalName = "t_" + Guid.NewGuid().ToString("N")[..16],
            SchemaVersion = 1
        };
        await _repository.InsertTableAsync(table, cancellationToken: TestContext.Current.CancellationToken);

        var column = new ColumnMetadata
        {
            ColumnId = Guid.NewGuid(),
            TableId = table.TableId,
            LogicalName = "indexed_col",
            PhysicalName = "c_indexed_col",
            DataType = MorphDataType.Text,
            NativeType = "TEXT",
            OrdinalPosition = 1
        };
        await _repository.InsertColumnAsync(column, cancellationToken: TestContext.Current.CancellationToken);

        var index = new IndexMetadata
        {
            IndexId = Guid.NewGuid(),
            TableId = table.TableId,
            LogicalName = "idx_test",
            PhysicalName = "i_" + Guid.NewGuid().ToString("N")[..16],
            Columns =
            [
                new IndexColumnInfo
                {
                    ColumnId = column.ColumnId,
                    LogicalName = column.LogicalName,
                    PhysicalName = column.PhysicalName,
                    Direction = SortDirection.Ascending,
                    NullsPosition = NullsPosition.Last
                }
            ],
            IndexType = IndexType.BTree,
            IsUnique = false
        };

        // Act
        var result = await _repository.InsertIndexAsync(index, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Should().NotBeNull();
        result.IndexId.Should().Be(index.IndexId);
        result.LogicalName.Should().Be("idx_test");
        result.Columns.Should().HaveCount(1);
    }

    /// <summary>
    /// A row written before 0.2.1 recorded no logical column name. It used to be read as if the physical name were the
    /// logical one -- a different name to every caller that addresses the index's columns -- and is now refused, saying how
    /// to rewrite it.
    /// </summary>
    [Fact]
    public async Task GetIndexByIdAsync_RefusesAnIndexRowThatRecordsNoLogicalColumnName()
    {
        var ct = TestContext.Current.CancellationToken;
        var table = new TableMetadata
        {
            TableId = Guid.NewGuid(),
            ProjectId = Guid.NewGuid(),
            LogicalName = "test_table_" + Guid.NewGuid().ToString("N")[..8],
            PhysicalName = "t_" + Guid.NewGuid().ToString("N")[..16],
            SchemaVersion = 1
        };
        await _repository.InsertTableAsync(table, cancellationToken: ct);
        var column = new ColumnMetadata
        {
            ColumnId = Guid.NewGuid(),
            TableId = table.TableId,
            LogicalName = "legacy_col",
            PhysicalName = "c_legacy_col",
            DataType = MorphDataType.Text,
            NativeType = "TEXT",
            OrdinalPosition = 1
        };
        await _repository.InsertColumnAsync(column, cancellationToken: ct);
        var index = new IndexMetadata
        {
            IndexId = Guid.NewGuid(),
            TableId = table.TableId,
            LogicalName = "idx_legacy",
            PhysicalName = "i_" + Guid.NewGuid().ToString("N")[..16],
            Columns =
            [
                new IndexColumnInfo
                {
                    ColumnId = column.ColumnId, LogicalName = column.LogicalName, PhysicalName = column.PhysicalName,
                    Direction = SortDirection.Ascending, NullsPosition = NullsPosition.Last
                }
            ],
            IndexType = IndexType.BTree
        };
        await _repository.InsertIndexAsync(index, cancellationToken: ct);
        await using (var connection = await _fixture.DataSource.OpenConnectionAsync(ct))
        await using (var command = connection.CreateCommand())
        {
            // The shape a database from before 0.2.1 holds: the column entries without `logical_name`.
            command.CommandText = "UPDATE morphdb._morph_indexes SET columns = (SELECT jsonb_agg(elem - 'logical_name') FROM jsonb_array_elements(columns) elem) WHERE index_id = @id";
            command.Parameters.AddWithValue("id", index.IndexId);
            await command.ExecuteNonQueryAsync(ct);
        }

        var read = async () => await _repository.GetIndexByIdAsync(index.IndexId, ct);

        (await read.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("idx_legacy").And.Contain("before MorphDB 0.2.1").And.Contain("Drop and recreate");
    }

    [Fact]
    public async Task GetIndexesByTableIdAsync_ShouldReturnAllIndexes()
    {
        // Arrange
        var table = new TableMetadata
        {
            TableId = Guid.NewGuid(),
            ProjectId = Guid.NewGuid(),
            LogicalName = "test_table_" + Guid.NewGuid().ToString("N")[..8],
            PhysicalName = "t_" + Guid.NewGuid().ToString("N")[..16],
            SchemaVersion = 1
        };
        await _repository.InsertTableAsync(table, cancellationToken: TestContext.Current.CancellationToken);

        var column = new ColumnMetadata
        {
            ColumnId = Guid.NewGuid(),
            TableId = table.TableId,
            LogicalName = "indexed_col",
            PhysicalName = "c_indexed_col",
            DataType = MorphDataType.Text,
            NativeType = "TEXT",
            OrdinalPosition = 1
        };
        await _repository.InsertColumnAsync(column, cancellationToken: TestContext.Current.CancellationToken);

        var index = new IndexMetadata
        {
            IndexId = Guid.NewGuid(),
            TableId = table.TableId,
            LogicalName = "idx_test_list",
            PhysicalName = "i_" + Guid.NewGuid().ToString("N")[..16],
            Columns =
            [
                new IndexColumnInfo
                {
                    ColumnId = column.ColumnId,
                    LogicalName = column.LogicalName,
                    PhysicalName = column.PhysicalName,
                    Direction = SortDirection.Ascending,
                    NullsPosition = NullsPosition.Last
                }
            ],
            IndexType = IndexType.BTree,
            IsUnique = false
        };
        await _repository.InsertIndexAsync(index, cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var indexes = await _repository.GetIndexesByTableIdAsync(table.TableId, TestContext.Current.CancellationToken);

        // Assert
        indexes.Should().Contain(i => i.LogicalName == "idx_test_list");
    }

    #endregion

    #region Version Tests

    [Fact]
    public async Task IncrementVersionAsync_ShouldIncrementVersion()
    {
        // Arrange
        var table = new TableMetadata
        {
            TableId = Guid.NewGuid(),
            ProjectId = Guid.NewGuid(),
            LogicalName = "test_table_" + Guid.NewGuid().ToString("N")[..8],
            PhysicalName = "t_" + Guid.NewGuid().ToString("N")[..16],
            SchemaVersion = 1
        };
        await _repository.InsertTableAsync(table, cancellationToken: TestContext.Current.CancellationToken);

        // Act
        await _repository.IncrementVersionAsync(table.TableId, cancellationToken: TestContext.Current.CancellationToken);
        var version = await _repository.GetCurrentVersionAsync(table.TableId, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        version.Should().Be(2);
    }

    #endregion
}
