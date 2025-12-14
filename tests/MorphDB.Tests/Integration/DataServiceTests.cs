using MorphDB.Core.Abstractions;
using MorphDB.Core.Exceptions;
using MorphDB.Core.Models;
using MorphDB.Npgsql.Infrastructure;
using MorphDB.Npgsql.Repositories;
using MorphDB.Npgsql.Services;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration;

/// <summary>
/// Integration tests for PostgresDataService.
/// </summary>
[Collection("PostgreSQL")]
public class DataServiceTests
{
    private readonly PostgresFixture _fixture;
    private readonly PostgresSchemaManager _schemaManager;
    private readonly PostgresDataService _dataService;
    private readonly MetadataRepository _metadataRepository;

    public DataServiceTests(PostgresFixture fixture)
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

    /// <summary>
    /// Creates a test table with user-defined columns.
    /// Note: System columns (id, tenant_id, created_at, updated_at) are auto-added by SchemaManager.
    /// </summary>
    private async Task<TableMetadata> CreateTestTableAsync(Guid tenantId, string logicalName)
    {
        return await _schemaManager.CreateTableAsync(new CreateTableRequest
        {
            TenantId = tenantId,
            LogicalName = logicalName,
            Columns =
            [
                // Only user-defined columns - system columns (id, tenant_id, created_at, updated_at) are auto-added
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
                },
                new CreateColumnRequest
                {
                    LogicalName = "age",
                    DataType = MorphDataType.Integer,
                    IsNullable = true
                },
                new CreateColumnRequest
                {
                    LogicalName = "is_active",
                    DataType = MorphDataType.Boolean,
                    IsNullable = false
                }
            ]
        });
    }

    #region InsertAsync Tests

    [Fact]
    public async Task InsertAsync_ShouldInsertAndReturnRecord()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var table = await CreateTestTableAsync(tenantId, "insert_test_" + Guid.NewGuid().ToString("N")[..8]);
        var id = Guid.NewGuid();

        var data = new Dictionary<string, object?>
        {
            ["id"] = id,
            ["email"] = "test@example.com",
            ["name"] = "Test User",
            ["age"] = 25,
            ["is_active"] = true
        };

        // Act
        var result = await _dataService.InsertAsync(tenantId, table.LogicalName, data);

        // Assert
        result.Should().NotBeNull();
        result["id"].Should().Be(id);
        result["email"].Should().Be("test@example.com");
        result["name"].Should().Be("Test User");
        result["age"].Should().Be(25);
        result["is_active"].Should().Be(true);
    }

    [Fact]
    public async Task InsertAsync_WithNullValues_ShouldInsertSuccessfully()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var table = await CreateTestTableAsync(tenantId, "insert_null_" + Guid.NewGuid().ToString("N")[..8]);
        var id = Guid.NewGuid();

        var data = new Dictionary<string, object?>
        {
            ["id"] = id,
            ["email"] = "test@example.com",
            ["name"] = null, // Nullable
            ["age"] = null,  // Nullable
            ["is_active"] = false
        };

        // Act
        var result = await _dataService.InsertAsync(tenantId, table.LogicalName, data);

        // Assert
        result["id"].Should().Be(id);
        result["name"].Should().BeNull();
        result["age"].Should().BeNull();
    }

    [Fact]
    public async Task InsertAsync_WithInvalidColumn_ShouldThrow()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var table = await CreateTestTableAsync(tenantId, "insert_invalid_" + Guid.NewGuid().ToString("N")[..8]);

        var data = new Dictionary<string, object?>
        {
            ["id"] = Guid.NewGuid(),
            ["email"] = "test@example.com",
            ["nonexistent_column"] = "value", // This column doesn't exist
            ["is_active"] = true
        };

        // Act & Assert
        var act = () => _dataService.InsertAsync(tenantId, table.LogicalName, data);
        await act.Should().ThrowAsync<ValidationException>();
    }

    #endregion

    #region GetByIdAsync Tests

    [Fact]
    public async Task GetByIdAsync_ShouldReturnRecord()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var table = await CreateTestTableAsync(tenantId, "getbyid_test_" + Guid.NewGuid().ToString("N")[..8]);
        var id = Guid.NewGuid();

        await _dataService.InsertAsync(tenantId, table.LogicalName, new Dictionary<string, object?>
        {
            ["id"] = id,
            ["email"] = "find@example.com",
            ["name"] = "Find Me",
            ["is_active"] = true
        });

        // Act
        var result = await _dataService.GetByIdAsync(tenantId, table.LogicalName, id);

        // Assert
        result.Should().NotBeNull();
        result!["id"].Should().Be(id);
        result["email"].Should().Be("find@example.com");
        result["name"].Should().Be("Find Me");
    }

    [Fact]
    public async Task GetByIdAsync_WithNonExistentId_ShouldReturnNull()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var table = await CreateTestTableAsync(tenantId, "getbyid_none_" + Guid.NewGuid().ToString("N")[..8]);
        var nonExistentId = Guid.NewGuid();

        // Act
        var result = await _dataService.GetByIdAsync(tenantId, table.LogicalName, nonExistentId);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByIdAsync_WithNonExistentTable_ShouldThrow()
    {
        // Arrange
        var tenantId = Guid.NewGuid();

        // Act & Assert
        var act = () => _dataService.GetByIdAsync(tenantId, "nonexistent_table", Guid.NewGuid());
        await act.Should().ThrowAsync<NotFoundException>();
    }

    #endregion

    #region UpdateAsync Tests

    [Fact]
    public async Task UpdateAsync_ShouldUpdateAndReturnRecord()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var table = await CreateTestTableAsync(tenantId, "update_test_" + Guid.NewGuid().ToString("N")[..8]);
        var id = Guid.NewGuid();

        await _dataService.InsertAsync(tenantId, table.LogicalName, new Dictionary<string, object?>
        {
            ["id"] = id,
            ["email"] = "old@example.com",
            ["name"] = "Old Name",
            ["is_active"] = true
        });

        // Act
        var result = await _dataService.UpdateAsync(tenantId, table.LogicalName, id, new Dictionary<string, object?>
        {
            ["email"] = "new@example.com",
            ["name"] = "New Name"
        });

        // Assert
        result.Should().NotBeNull();
        result["id"].Should().Be(id);
        result["email"].Should().Be("new@example.com");
        result["name"].Should().Be("New Name");
        result["is_active"].Should().Be(true); // Unchanged
    }

    [Fact]
    public async Task UpdateAsync_WithNonExistentId_ShouldThrow()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var table = await CreateTestTableAsync(tenantId, "update_none_" + Guid.NewGuid().ToString("N")[..8]);
        var nonExistentId = Guid.NewGuid();

        // Act & Assert
        var act = () => _dataService.UpdateAsync(tenantId, table.LogicalName, nonExistentId, new Dictionary<string, object?>
        {
            ["email"] = "new@example.com"
        });
        await act.Should().ThrowAsync<NotFoundException>();
    }

    #endregion

    #region DeleteAsync Tests

    [Fact]
    public async Task DeleteAsync_ShouldDeleteAndReturnTrue()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var table = await CreateTestTableAsync(tenantId, "delete_test_" + Guid.NewGuid().ToString("N")[..8]);
        var id = Guid.NewGuid();

        await _dataService.InsertAsync(tenantId, table.LogicalName, new Dictionary<string, object?>
        {
            ["id"] = id,
            ["email"] = "delete@example.com",
            ["is_active"] = true
        });

        // Act
        var result = await _dataService.DeleteAsync(tenantId, table.LogicalName, id);

        // Assert
        result.Should().BeTrue();

        // Verify deletion
        var getResult = await _dataService.GetByIdAsync(tenantId, table.LogicalName, id);
        getResult.Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_WithNonExistentId_ShouldReturnFalse()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var table = await CreateTestTableAsync(tenantId, "delete_none_" + Guid.NewGuid().ToString("N")[..8]);

        // Act
        var result = await _dataService.DeleteAsync(tenantId, table.LogicalName, Guid.NewGuid());

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region InsertBatchAsync Tests

    [Fact]
    public async Task InsertBatchAsync_ShouldInsertMultipleRecords()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var table = await CreateTestTableAsync(tenantId, "batch_insert_" + Guid.NewGuid().ToString("N")[..8]);

        var records = new List<IDictionary<string, object?>>
        {
            new Dictionary<string, object?>
            {
                ["id"] = Guid.NewGuid(),
                ["email"] = "user1@example.com",
                ["name"] = "User 1",
                ["is_active"] = true
            },
            new Dictionary<string, object?>
            {
                ["id"] = Guid.NewGuid(),
                ["email"] = "user2@example.com",
                ["name"] = "User 2",
                ["is_active"] = false
            },
            new Dictionary<string, object?>
            {
                ["id"] = Guid.NewGuid(),
                ["email"] = "user3@example.com",
                ["name"] = "User 3",
                ["is_active"] = true
            }
        };

        // Act
        var results = await _dataService.InsertBatchAsync(tenantId, table.LogicalName, records);

        // Assert
        results.Should().HaveCount(3);
        results[0]["email"].Should().Be("user1@example.com");
        results[1]["email"].Should().Be("user2@example.com");
        results[2]["email"].Should().Be("user3@example.com");
    }

    [Fact]
    public async Task InsertBatchAsync_WithEmptyList_ShouldReturnEmpty()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var table = await CreateTestTableAsync(tenantId, "batch_empty_" + Guid.NewGuid().ToString("N")[..8]);

        // Act
        var results = await _dataService.InsertBatchAsync(tenantId, table.LogicalName, []);

        // Assert
        results.Should().BeEmpty();
    }

    #endregion

    #region UpsertAsync Tests

    [Fact]
    public async Task UpsertAsync_ShouldInsertNewRecord()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var table = await CreateTestTableAsync(tenantId, "upsert_insert_" + Guid.NewGuid().ToString("N")[..8]);
        var id = Guid.NewGuid();

        var data = new Dictionary<string, object?>
        {
            ["id"] = id,
            ["email"] = "upsert@example.com",
            ["name"] = "Upsert User",
            ["is_active"] = true
        };

        // Act
        var result = await _dataService.UpsertAsync(tenantId, table.LogicalName, data, ["id"]);

        // Assert
        result.Should().NotBeNull();
        result["id"].Should().Be(id);
        result["email"].Should().Be("upsert@example.com");
    }

    [Fact]
    public async Task UpsertAsync_ShouldUpdateExistingRecord()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var table = await CreateTestTableAsync(tenantId, "upsert_update_" + Guid.NewGuid().ToString("N")[..8]);
        var id = Guid.NewGuid();

        // Insert first
        await _dataService.InsertAsync(tenantId, table.LogicalName, new Dictionary<string, object?>
        {
            ["id"] = id,
            ["email"] = "old@example.com",
            ["name"] = "Old Name",
            ["is_active"] = true
        });

        // Upsert with same ID
        var data = new Dictionary<string, object?>
        {
            ["id"] = id,
            ["email"] = "new@example.com",
            ["name"] = "New Name",
            ["is_active"] = false
        };

        // Act
        var result = await _dataService.UpsertAsync(tenantId, table.LogicalName, data, ["id"]);

        // Assert
        result.Should().NotBeNull();
        result["id"].Should().Be(id);
        result["email"].Should().Be("new@example.com");
        result["name"].Should().Be("New Name");
        result["is_active"].Should().Be(false);
    }

    [Fact]
    public async Task UpsertAsync_WithInvalidKeyColumn_ShouldThrow()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var table = await CreateTestTableAsync(tenantId, "upsert_invalid_" + Guid.NewGuid().ToString("N")[..8]);

        var data = new Dictionary<string, object?>
        {
            ["id"] = Guid.NewGuid(),
            ["email"] = "test@example.com",
            ["is_active"] = true
        };

        // Act & Assert
        var act = () => _dataService.UpsertAsync(tenantId, table.LogicalName, data, ["nonexistent_key"]);
        await act.Should().ThrowAsync<ValidationException>();
    }

    #endregion
}
