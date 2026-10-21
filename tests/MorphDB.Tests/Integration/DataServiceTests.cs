using System.Diagnostics;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Exceptions;
using MorphDB.Core.Models;
using MorphDB.Npgsql.Infrastructure;
using MorphDB.Npgsql.Repositories;
using MorphDB.Npgsql.Schema;
using MorphDB.Npgsql.Security;
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
        _metadataRepository = new MetadataRepository(fixture.Connections);

        var nameHasher = new Sha256NameHasher();
        var lockOptions = new AdvisoryLockOptions();
        var lockManager = new PostgresAdvisoryLockManager(fixture.Connections, lockOptions);
        var changeLogger = new ChangeLogger(fixture.Connections);
        var schemaOptions = new SchemaManagerOptions();

        _schemaManager = new PostgresSchemaManager(
            fixture.Connections,
            _metadataRepository,
            lockManager,
            nameHasher,
            changeLogger,
            new ProjectRepository(fixture.Connections, new PostgresSchemaNameResolver()),
            schemaOptions);

        _dataService = fixture.CreateDataService(_metadataRepository, new SecurityContextAccessor());
    }

    /// <summary>
    /// Creates a test table with user-defined columns.
    /// Note: System columns (id, project_id, created_at, updated_at) are auto-added by SchemaManager.
    /// </summary>
    private async Task<TableMetadata> CreateTestTableAsync(Guid projectId, string logicalName)
    {
        return await _schemaManager.CreateTableAsync(new CreateTableRequest
        {
            ProjectId = projectId,
            LogicalName = logicalName,
            Columns =
            [
                // Only user-defined columns - system columns (id, project_id, created_at, updated_at) are auto-added
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

    #region Unit of work

    private static Dictionary<string, object?> Row(Guid id) => new()
    {
        ["_id"] = id,
        ["email"] = $"{id:N}@example.com",
        ["is_active"] = true,
    };

    [Fact]
    public async Task A_read_inside_a_unit_of_work_sees_a_row_the_unit_wrote_and_nothing_stays_without_a_commit()
    {
        var ct = TestContext.Current.CancellationToken;
        var projectId = Guid.NewGuid();
        var table = await CreateTestTableAsync(projectId, "unit_read_" + Guid.NewGuid().ToString("N")[..8]);
        var id = Guid.NewGuid();
        var units = new PostgresUnitOfWorkFactory(_fixture.Connections);

        await using (var unit = units.Begin())
        {
            await _dataService.InsertAsync(projectId, table.LogicalName, Row(id), ct);

            (await _dataService.GetByIdAsync(projectId, table.LogicalName, id, ct)).Should().NotBeNull("a read in the unit runs in its transaction");
            (await _dataService.Query(projectId).From(table.LogicalName).CountAsync(ct)).Should().Be(1);
        }

        (await _dataService.GetByIdAsync(projectId, table.LogicalName, id, ct)).Should().BeNull("ending a unit without a commit rolls it back");
    }

    /// <summary>
    /// A record read costs the table's definition once and the row once. It used to read the definition
    /// twice — once to find the key column, again inside the query — and each part of it on a connection
    /// of its own, each open writing the session context: 18 statements for one row, which a realtime
    /// change pays once per distinct subscriber.
    /// </summary>
    [Fact]
    public async Task Reading_a_record_reads_its_table_definition_once_in_one_round_trip()
    {
        var ct = TestContext.Current.CancellationToken;
        var projectId = Guid.NewGuid();
        var table = await CreateTestTableAsync(projectId, "read_cost_" + Guid.NewGuid().ToString("N")[..8]);
        var id = Guid.NewGuid();
        await _dataService.InsertAsync(projectId, table.LogicalName, Row(id), ct);

        var statements = await StatementsOfAsync(() => _dataService.GetByIdAsync(projectId, table.LogicalName, id, ct));

        // The table row, its columns, relations and indexes are sent together: one command.
        var definition = statements.Where(s => s.Contains("FROM morphdb._morph_columns", StringComparison.Ordinal)).Should().ContainSingle(string.Join("\n---\n", statements)).Subject;
        definition.Should().Contain("FROM morphdb._morph_relations").And.Contain("FROM morphdb._morph_indexes").And.Contain("FROM morphdb._morph_tables");
        statements.Count(s => s.Contains("FROM morphdb._morph_tables", StringComparison.Ordinal)).Should().Be(1, "the definition is read once");
        statements.Count(s => s.Contains("set_config", StringComparison.Ordinal)).Should().BeLessThanOrEqualTo(2,
            "one connection for the definition and one for the row");
    }

    /// <summary>
    /// An opened table reads its definition once, however many of its records are read against it — what
    /// a realtime change needs, which reads one row once per distinct subscriber.
    /// </summary>
    [Fact]
    public async Task An_opened_table_reads_its_definition_once_for_every_record_read_against_it()
    {
        var ct = TestContext.Current.CancellationToken;
        var projectId = Guid.NewGuid();
        var table = await CreateTestTableAsync(projectId, "open_cost_" + Guid.NewGuid().ToString("N")[..8]);
        var id = Guid.NewGuid();
        await _dataService.InsertAsync(projectId, table.LogicalName, Row(id), ct);

        IDictionary<string, object?>? last = null;
        var statements = await StatementsOfAsync(async () =>
        {
            var opened = await _dataService.OpenTableAsync(projectId, table.LogicalName, ct);
            for (var i = 0; i < 3; i++)
            {
                last = await opened.GetByIdAsync(id, ct);
            }
        });

        last.Should().NotBeNull();
        last!["email"].Should().Be($"{id:N}@example.com", "the same row a read by id serves");
        statements.Count(s => s.Contains("FROM morphdb._morph_columns", StringComparison.Ordinal)).Should().Be(1, string.Join("\n---\n", statements));
        statements.Count(s => s.Contains("FROM morphdb._morph_indexes", StringComparison.Ordinal)).Should().Be(1);
    }

    /// <summary>The SQL text of every statement <paramref name="action"/> sends, and nothing another test sends meanwhile.</summary>
    private static async Task<IReadOnlyList<string>> StatementsOfAsync(Func<Task> action)
    {
        using var source = new ActivitySource("MorphDB.Tests.Statements");
        var statements = new System.Collections.Concurrent.ConcurrentQueue<(ActivityTraceId Trace, string Text)>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == "Npgsql" || s.Name == source.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                if (activity.Source.Name == "Npgsql"
                    && (activity.GetTagItem("db.query.text") ?? activity.GetTagItem("db.statement")) is string text)
                {
                    statements.Enqueue((activity.TraceId, text));
                }
            },
        };
        ActivitySource.AddActivityListener(listener);

        ActivityTraceId trace;
        using (var root = source.StartActivity("measured"))
        {
            trace = root!.TraceId;
            await action();
        }

        return [.. statements.Where(s => s.Trace == trace).Select(s => s.Text)];
    }


    [Fact]
    public async Task A_committed_unit_of_work_keeps_its_rows()
    {
        var ct = TestContext.Current.CancellationToken;
        var projectId = Guid.NewGuid();
        var table = await CreateTestTableAsync(projectId, "unit_commit_" + Guid.NewGuid().ToString("N")[..8]);
        var id = Guid.NewGuid();
        var units = new PostgresUnitOfWorkFactory(_fixture.Connections);

        await using (var unit = units.Begin())
        {
            await _dataService.InsertAsync(projectId, table.LogicalName, Row(id), ct);
            await unit.CommitAsync(ct);
        }

        (await _dataService.GetByIdAsync(projectId, table.LogicalName, id, ct)).Should().NotBeNull();
    }

    [Fact]
    public async Task A_unit_begun_inside_another_joins_it_and_its_commit_keeps_nothing_on_its_own()
    {
        var ct = TestContext.Current.CancellationToken;
        var projectId = Guid.NewGuid();
        var table = await CreateTestTableAsync(projectId, "unit_nested_" + Guid.NewGuid().ToString("N")[..8]);
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var units = new PostgresUnitOfWorkFactory(_fixture.Connections);

        await using (var outer = units.Begin())
        {
            // A batch insert begins its own unit — inside the outer one it joins instead.
            await _dataService.InsertBatchAsync(projectId, table.LogicalName, [Row(ids[0]), Row(ids[1])], ct);
            await using (var inner = units.Begin())
            {
                await inner.CommitAsync(ct);
            }
        }

        (await _dataService.Query(projectId).From(table.LogicalName).CountAsync(ct)).Should().Be(0, "the outer unit was not committed");
    }

    #endregion

    #region InsertAsync Tests

    [Fact]
    public async Task InsertAsync_ShouldInsertAndReturnRecord()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var table = await CreateTestTableAsync(projectId, "insert_test_" + Guid.NewGuid().ToString("N")[..8]);
        var id = Guid.NewGuid();

        var data = new Dictionary<string, object?>
        {
            ["_id"] = id,
            ["email"] = "test@example.com",
            ["name"] = "Test User",
            ["age"] = 25,
            ["is_active"] = true
        };

        // Act
        var result = await _dataService.InsertAsync(projectId, table.LogicalName, data, TestContext.Current.CancellationToken);

        // Assert
        result.Should().NotBeNull();
        result["_id"].Should().Be(id);
        result["email"].Should().Be("test@example.com");
        result["name"].Should().Be("Test User");
        result["age"].Should().Be(25);
        result["is_active"].Should().Be(true);
    }

    [Fact]
    public async Task InsertAsync_WithNullValues_ShouldInsertSuccessfully()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var table = await CreateTestTableAsync(projectId, "insert_null_" + Guid.NewGuid().ToString("N")[..8]);
        var id = Guid.NewGuid();

        var data = new Dictionary<string, object?>
        {
            ["_id"] = id,
            ["email"] = "test@example.com",
            ["name"] = null, // Nullable
            ["age"] = null,  // Nullable
            ["is_active"] = false
        };

        // Act
        var result = await _dataService.InsertAsync(projectId, table.LogicalName, data, TestContext.Current.CancellationToken);

        // Assert
        result["_id"].Should().Be(id);
        result["name"].Should().BeNull();
        result["age"].Should().BeNull();
    }

    [Fact]
    public async Task InsertAsync_WithInvalidColumn_ShouldThrow()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var table = await CreateTestTableAsync(projectId, "insert_invalid_" + Guid.NewGuid().ToString("N")[..8]);

        var data = new Dictionary<string, object?>
        {
            ["_id"] = Guid.NewGuid(),
            ["email"] = "test@example.com",
            ["nonexistent_column"] = "value", // This column doesn't exist
            ["is_active"] = true
        };

        // Act & Assert
        var act = () => _dataService.InsertAsync(projectId, table.LogicalName, data);
        await act.Should().ThrowAsync<ValidationException>();
    }

    #endregion

    #region GetByIdAsync Tests

    [Fact]
    public async Task GetByIdAsync_ShouldReturnRecord()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var table = await CreateTestTableAsync(projectId, "getbyid_test_" + Guid.NewGuid().ToString("N")[..8]);
        var id = Guid.NewGuid();

        await _dataService.InsertAsync(projectId, table.LogicalName, new Dictionary<string, object?>
        {
            ["_id"] = id,
            ["email"] = "find@example.com",
            ["name"] = "Find Me",
            ["is_active"] = true
        }, TestContext.Current.CancellationToken);

        // Act
        var result = await _dataService.GetByIdAsync(projectId, table.LogicalName, id, TestContext.Current.CancellationToken);

        // Assert
        result.Should().NotBeNull();
        result!["_id"].Should().Be(id);
        result["email"].Should().Be("find@example.com");
        result["name"].Should().Be("Find Me");
    }

    [Fact]
    public async Task GetByIdAsync_WithNonExistentId_ShouldReturnNull()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var table = await CreateTestTableAsync(projectId, "getbyid_none_" + Guid.NewGuid().ToString("N")[..8]);
        var nonExistentId = Guid.NewGuid();

        // Act
        var result = await _dataService.GetByIdAsync(projectId, table.LogicalName, nonExistentId, TestContext.Current.CancellationToken);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByIdAsync_WithNonExistentTable_ShouldThrow()
    {
        // Arrange
        var projectId = Guid.NewGuid();

        // Act & Assert
        var act = () => _dataService.GetByIdAsync(projectId, "nonexistent_table", Guid.NewGuid());
        // The domain type (not a message-bearing generic NotFoundException): it maps to the same
        // 404, and its message no longer embeds the project GUID (hidden-layer sweep).
        await act.Should().ThrowAsync<TableNotFoundException>();
    }

    #endregion

    #region UpdateAsync Tests

    [Fact]
    public async Task UpdateAsync_ShouldUpdateAndReturnRecord()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var table = await CreateTestTableAsync(projectId, "update_test_" + Guid.NewGuid().ToString("N")[..8]);
        var id = Guid.NewGuid();

        await _dataService.InsertAsync(projectId, table.LogicalName, new Dictionary<string, object?>
        {
            ["_id"] = id,
            ["email"] = "old@example.com",
            ["name"] = "Old Name",
            ["is_active"] = true
        }, TestContext.Current.CancellationToken);

        // Act
        var result = await _dataService.UpdateAsync(projectId, table.LogicalName, id, new Dictionary<string, object?>
        {
            ["email"] = "new@example.com",
            ["name"] = "New Name"
        }, TestContext.Current.CancellationToken);

        // Assert
        result.Should().NotBeNull();
        result["_id"].Should().Be(id);
        result["email"].Should().Be("new@example.com");
        result["name"].Should().Be("New Name");
        result["is_active"].Should().Be(true); // Unchanged
    }

    [Fact]
    public async Task UpdateAsync_WithNonExistentId_ShouldThrow()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var table = await CreateTestTableAsync(projectId, "update_none_" + Guid.NewGuid().ToString("N")[..8]);
        var nonExistentId = Guid.NewGuid();

        // Act & Assert
        var act = () => _dataService.UpdateAsync(projectId, table.LogicalName, nonExistentId, new Dictionary<string, object?>
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
        var projectId = Guid.NewGuid();
        var table = await CreateTestTableAsync(projectId, "delete_test_" + Guid.NewGuid().ToString("N")[..8]);
        var id = Guid.NewGuid();

        await _dataService.InsertAsync(projectId, table.LogicalName, new Dictionary<string, object?>
        {
            ["_id"] = id,
            ["email"] = "delete@example.com",
            ["is_active"] = true
        }, TestContext.Current.CancellationToken);

        // Act
        var result = await _dataService.DeleteAsync(projectId, table.LogicalName, id, TestContext.Current.CancellationToken);

        // Assert
        result.Should().BeTrue();

        // Verify deletion
        var getResult = await _dataService.GetByIdAsync(projectId, table.LogicalName, id, TestContext.Current.CancellationToken);
        getResult.Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_WithNonExistentId_ShouldReturnFalse()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var table = await CreateTestTableAsync(projectId, "delete_none_" + Guid.NewGuid().ToString("N")[..8]);

        // Act
        var result = await _dataService.DeleteAsync(projectId, table.LogicalName, Guid.NewGuid(), TestContext.Current.CancellationToken);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region InsertBatchAsync Tests

    [Fact]
    public async Task InsertBatchAsync_ShouldInsertMultipleRecords()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var table = await CreateTestTableAsync(projectId, "batch_insert_" + Guid.NewGuid().ToString("N")[..8]);

        var records = new List<IDictionary<string, object?>>
        {
            new Dictionary<string, object?>
            {
                ["_id"] = Guid.NewGuid(),
                ["email"] = "user1@example.com",
                ["name"] = "User 1",
                ["is_active"] = true
            },
            new Dictionary<string, object?>
            {
                ["_id"] = Guid.NewGuid(),
                ["email"] = "user2@example.com",
                ["name"] = "User 2",
                ["is_active"] = false
            },
            new Dictionary<string, object?>
            {
                ["_id"] = Guid.NewGuid(),
                ["email"] = "user3@example.com",
                ["name"] = "User 3",
                ["is_active"] = true
            }
        };

        // Act
        var results = await _dataService.InsertBatchAsync(projectId, table.LogicalName, records, TestContext.Current.CancellationToken);

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
        var projectId = Guid.NewGuid();
        var table = await CreateTestTableAsync(projectId, "batch_empty_" + Guid.NewGuid().ToString("N")[..8]);

        // Act
        var results = await _dataService.InsertBatchAsync(projectId, table.LogicalName, [], TestContext.Current.CancellationToken);

        // Assert
        results.Should().BeEmpty();
    }

    /// <summary>
    /// A batch insert sends its rows together: each row is its own statement, as a single insert sends,
    /// but the statements share round trips — not one round trip per row.
    /// </summary>
    [Fact]
    public async Task A_batch_insert_sends_its_rows_in_one_round_trip_and_answers_them_in_order()
    {
        var ct = TestContext.Current.CancellationToken;
        var projectId = Guid.NewGuid();
        var table = await CreateTestTableAsync(projectId, "batch_trips_" + Guid.NewGuid().ToString("N")[..8]);
        var ids = Enumerable.Range(0, 50).Select(_ => Guid.NewGuid()).ToList();

        IReadOnlyList<IDictionary<string, object?>> results = [];
        var statements = await StatementsOfAsync(async () =>
            results = await _dataService.InsertBatchAsync(projectId, table.LogicalName, ids.Select(id => (IDictionary<string, object?>)Row(id)).ToList(), ct));

        results.Select(r => r["_id"]).Should().Equal(ids.Cast<object?>(), "results come back in input order");
        results.Should().OnlyContain(r => r["_created_at"] != null, "the row the database stored comes back, defaults included");
        statements.Count(s => s.Contains("INSERT INTO", StringComparison.Ordinal)).Should().Be(1, string.Join("\n---\n", statements.Take(5)));
        (await _dataService.Query(projectId).From(table.LogicalName).CountAsync(ct)).Should().Be(50);
    }

    [Fact]
    public async Task A_batch_larger_than_one_round_trip_keeps_every_row_in_order()
    {
        var ct = TestContext.Current.CancellationToken;
        var projectId = Guid.NewGuid();
        var table = await CreateTestTableAsync(projectId, "batch_large_" + Guid.NewGuid().ToString("N")[..8]);
        var ids = Enumerable.Range(0, 2_500).Select(_ => Guid.NewGuid()).ToList();

        var results = await _dataService.InsertBatchAsync(projectId, table.LogicalName, ids.Select(id => (IDictionary<string, object?>)Row(id)).ToList(), ct);

        results.Select(r => r["_id"]).Should().Equal(ids.Cast<object?>());
        (await _dataService.Query(projectId).From(table.LogicalName).CountAsync(ct)).Should().Be(2_500);
    }

    /// <summary>
    /// A row the database refuses in the middle of a batch — here a duplicate primary key, which no
    /// validator reads — is the caller's mistake, and the rows before it are undone with it.
    /// </summary>
    [Fact]
    public async Task A_row_the_database_refuses_mid_batch_is_a_validation_error_and_leaves_nothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var projectId = Guid.NewGuid();
        var table = await CreateTestTableAsync(projectId, "batch_refused_" + Guid.NewGuid().ToString("N")[..8]);
        var id = Guid.NewGuid();

        var act = () => _dataService.InsertBatchAsync(projectId, table.LogicalName, [Row(Guid.NewGuid()), Row(id), Row(Guid.NewGuid()), Row(id)], ct);

        // The four rows write the same columns and go in one statement, which PostgreSQL refuses as a
        // whole: the error names the rows that statement carried.
        var refused = (await act.Should().ThrowAsync<ValidationException>()).Which;
        refused.Message.Should().Contain("unique constraint");
        refused.Field.Should().Be("[0]–[3]");

        // Rows that write different columns go in separate statements; the refused one is named exactly.
        var other = Guid.NewGuid();
        var second = () => _dataService.InsertBatchAsync(projectId, table.LogicalName,
            [Row(other), new Dictionary<string, object?>(Row(other)) { ["name"] = "second shape" }], ct);
        (await second.Should().ThrowAsync<ValidationException>()).Which.Field.Should().Be("[1]");
        (await _dataService.Query(projectId).From(table.LogicalName).CountAsync(ct)).Should().Be(0);
    }

    /// <summary>
    /// A value bound from JSON is stored the same through a batch as through a single insert — the
    /// batch binds its parameters itself, so their types are its own to get right.
    /// </summary>
    [Fact]
    public async Task A_batch_stores_values_as_a_single_insert_stores_them()
    {
        var ct = TestContext.Current.CancellationToken;
        var projectId = Guid.NewGuid();
        var table = await _schemaManager.CreateTableAsync(new CreateTableRequest
        {
            ProjectId = projectId,
            LogicalName = "batch_types_" + Guid.NewGuid().ToString("N")[..8],
            Columns =
            [
                new CreateColumnRequest { LogicalName = "at", DataType = MorphDataType.DateTime },
                new CreateColumnRequest { LogicalName = "day", DataType = MorphDataType.Date },
                new CreateColumnRequest { LogicalName = "amount", DataType = MorphDataType.Decimal },
                new CreateColumnRequest { LogicalName = "tags", DataType = MorphDataType.Json },
                new CreateColumnRequest { LogicalName = "ref", DataType = MorphDataType.Uuid },
                new CreateColumnRequest { LogicalName = "note", DataType = MorphDataType.Text },
            ]
        }, ct);
        var json = System.Text.Json.JsonDocument.Parse(
            """{"at":"2026-01-15T09:30:00+09:00","day":"2026-01-15","amount":12.50,"tags":{"a":[1,2]},"ref":"0199e2a0-0000-7000-8000-000000000001","note":null}""").RootElement;
        Dictionary<string, object?> Payload() => json.EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value.Clone());

        var single = await _dataService.InsertAsync(projectId, table.LogicalName, Payload(), ct);
        var batched = (await _dataService.InsertBatchAsync(projectId, table.LogicalName, [Payload()], ct)).Single();

        foreach (var column in new[] { "at", "day", "amount", "tags", "ref", "note" })
        {
            System.Text.Json.JsonSerializer.Serialize(batched[column]).Should().Be(System.Text.Json.JsonSerializer.Serialize(single[column]), column);
        }
    }

    /// <summary>
    /// Rows of one batch need not write the same columns: neighbours that do share a statement, and a
    /// row that differs starts the next — every row comes back as stored, in input order.
    /// </summary>
    [Fact]
    public async Task A_batch_of_rows_writing_different_columns_stores_each_as_given()
    {
        var ct = TestContext.Current.CancellationToken;
        var projectId = Guid.NewGuid();
        var table = await _schemaManager.CreateTableAsync(new CreateTableRequest
        {
            ProjectId = projectId,
            LogicalName = "batch_shapes_" + Guid.NewGuid().ToString("N")[..8],
            Columns =
            [
                new CreateColumnRequest { LogicalName = "note", DataType = MorphDataType.Text },
                new CreateColumnRequest { LogicalName = "tags", DataType = MorphDataType.Json },
                new CreateColumnRequest { LogicalName = "n", DataType = MorphDataType.Integer },
            ]
        }, ct);
        var json = System.Text.Json.JsonDocument.Parse("""{"k":[1,2]}""").RootElement;
        var ids = Enumerable.Range(0, 6).Select(_ => Guid.NewGuid()).ToList();
        // Ids as the REST path delivers them.
        object Id(int i) => System.Text.Json.JsonDocument.Parse($"\"{ids[i]}\"").RootElement.Clone();
        IDictionary<string, object?>[] rows =
        [
            new Dictionary<string, object?> { ["_id"] = Id(0), ["note"] = "a", ["n"] = 1 },
            new Dictionary<string, object?> { ["_id"] = Id(1), ["note"] = "b", ["n"] = 2 },
            new Dictionary<string, object?> { ["_id"] = Id(2), ["tags"] = json.Clone() },
            new Dictionary<string, object?> { ["_id"] = Id(3), ["note"] = null, ["n"] = 4 },
            new Dictionary<string, object?> { ["_id"] = Id(4), ["note"] = "e", ["n"] = 5, ["tags"] = json.Clone() },
            new Dictionary<string, object?> { ["_id"] = Id(5), ["note"] = "f", ["n"] = 6 },
        ];

        var results = await _dataService.InsertBatchAsync(projectId, table.LogicalName, rows, ct);

        results.Select(r => r["_id"]).Should().Equal(ids.Cast<object?>());
        results.Select(r => r["note"]).Should().Equal("a", "b", null, null, "e", "f");
        results.Select(r => r["n"]).Should().Equal(1, 2, null, 4, 5, 6);
        System.Text.Json.JsonSerializer.Serialize(results[2]["tags"]).Should().Be(System.Text.Json.JsonSerializer.Serialize(results[4]["tags"]));
        results[0]["tags"].Should().BeNull();
    }

    /// <summary>
    /// A step that reads stored rows sees every earlier row of the batch — numbering in a hierarchy
    /// continues within the batch instead of handing every row the same next number.
    /// </summary>
    [Fact]
    public async Task Rows_of_one_batch_are_numbered_in_their_hierarchy_one_after_another()
    {
        var ct = TestContext.Current.CancellationToken;
        var projectId = Guid.NewGuid();
        var table = await _schemaManager.CreateTableAsync(new CreateTableRequest
        {
            ProjectId = projectId,
            LogicalName = "batch_tree_" + Guid.NewGuid().ToString("N")[..8],
            SystemColumns = new SystemColumnOptions { HierarchyEnabled = true },
            Columns = [new CreateColumnRequest { LogicalName = "title", DataType = MorphDataType.Text }]
        }, ct);
        var parent = await _dataService.InsertAsync(projectId, table.LogicalName, new Dictionary<string, object?> { ["title"] = "root" }, ct);

        var children = await _dataService.InsertBatchAsync(projectId, table.LogicalName,
            Enumerable.Range(0, 4).Select(i => (IDictionary<string, object?>)new Dictionary<string, object?>
            {
                ["title"] = $"child {i}",
                ["_parent_id"] = parent["_id"],
            }).ToList(), ct);

        children.Select(c => Convert.ToInt32(c["_sort_order"], System.Globalization.CultureInfo.InvariantCulture)).Should().Equal(1, 2, 3, 4);
    }

    /// <summary>
    /// A reference to a row an earlier write of the same batch inserted is a reference to a row that
    /// exists — the batch means what the same inserts one by one mean.
    /// </summary>
    [Fact]
    public async Task A_row_may_reference_a_row_inserted_earlier_in_the_same_batch()
    {
        var ct = TestContext.Current.CancellationToken;
        var projectId = Guid.NewGuid();
        var table = await _schemaManager.CreateTableAsync(new CreateTableRequest
        {
            ProjectId = projectId,
            LogicalName = "batch_ref_" + Guid.NewGuid().ToString("N")[..8],
            Columns =
            [
                new CreateColumnRequest { LogicalName = "code", DataType = MorphDataType.Text, IsUnique = true },
                new CreateColumnRequest { LogicalName = "parent_code", DataType = MorphDataType.Text }
            ]
        }, ct);
        await _schemaManager.CreateRelationAsync(new CreateRelationRequest
        {
            ProjectId = projectId,
            LogicalName = "rel_" + Guid.NewGuid().ToString("N")[..8],
            SourceTableId = table.TableId,
            SourceColumnId = table.Columns.First(c => c.LogicalName == "parent_code").ColumnId,
            TargetTableId = table.TableId,
            TargetColumnId = table.Columns.First(c => c.LogicalName == "code").ColumnId,
            RelationType = RelationType.OneToMany
        }, ct);

        var rows = await _dataService.InsertBatchAsync(projectId, table.LogicalName,
        [
            new Dictionary<string, object?> { ["code"] = "A" },
            new Dictionary<string, object?> { ["code"] = "B", ["parent_code"] = "A" },
        ], ct);
        rows.Should().HaveCount(2);

        // And a duplicate within one batch is seen by the uniqueness check, as row by row.
        var duplicate = () => _dataService.InsertBatchAsync(projectId, table.LogicalName,
        [
            new Dictionary<string, object?> { ["code"] = "C" },
            new Dictionary<string, object?> { ["code"] = "C" },
        ], ct);
        var refused = (await duplicate.Should().ThrowAsync<ValidationException>()).Which;
        refused.Message.Should().Contain("already exists");
        refused.Field.Should().Be("[1].code", "the second row of the request is the one refused");
        (await _dataService.Query(projectId).From(table.LogicalName).CountAsync(ct)).Should().Be(2, "the failed batch left nothing");
    }

    #endregion

    #region UpsertAsync Tests

    [Fact]
    public async Task UpsertAsync_ShouldInsertNewRecord()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var table = await CreateTestTableAsync(projectId, "upsert_insert_" + Guid.NewGuid().ToString("N")[..8]);
        var id = Guid.NewGuid();

        var data = new Dictionary<string, object?>
        {
            ["_id"] = id,
            ["email"] = "upsert@example.com",
            ["name"] = "Upsert User",
            ["is_active"] = true
        };

        // Act
        var (result, inserted) = await _dataService.UpsertAsync(projectId, table.LogicalName, data, ["_id"], TestContext.Current.CancellationToken);

        // Assert
        inserted.Should().BeTrue();
        result.Should().NotBeNull();
        result.Should().NotContainKey(MorphDB.Npgsql.Dml.DmlBuilder.UpsertInsertedColumn);
        result["_id"].Should().Be(id);
        result["email"].Should().Be("upsert@example.com");
    }

    [Fact]
    public async Task UpsertAsync_ShouldUpdateExistingRecord()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var table = await CreateTestTableAsync(projectId, "upsert_update_" + Guid.NewGuid().ToString("N")[..8]);
        var id = Guid.NewGuid();

        // Insert first
        await _dataService.InsertAsync(projectId, table.LogicalName, new Dictionary<string, object?>
        {
            ["_id"] = id,
            ["email"] = "old@example.com",
            ["name"] = "Old Name",
            ["is_active"] = true
        }, TestContext.Current.CancellationToken);

        // Upsert with same ID
        var data = new Dictionary<string, object?>
        {
            ["_id"] = id,
            ["email"] = "new@example.com",
            ["name"] = "New Name",
            ["is_active"] = false
        };

        // Act
        var (result, inserted) = await _dataService.UpsertAsync(projectId, table.LogicalName, data, ["_id"], TestContext.Current.CancellationToken);

        // Assert
        inserted.Should().BeFalse();
        result.Should().NotBeNull();
        result["_id"].Should().Be(id);
        result["email"].Should().Be("new@example.com");
        result["name"].Should().Be("New Name");
        result["is_active"].Should().Be(false);
    }

    [Fact]
    public async Task UpsertAsync_WithInvalidKeyColumn_ShouldThrow()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var table = await CreateTestTableAsync(projectId, "upsert_invalid_" + Guid.NewGuid().ToString("N")[..8]);

        var data = new Dictionary<string, object?>
        {
            ["_id"] = Guid.NewGuid(),
            ["email"] = "test@example.com",
            ["is_active"] = true
        };

        // Act & Assert
        var act = () => _dataService.UpsertAsync(projectId, table.LogicalName, data, ["nonexistent_key"]);
        await act.Should().ThrowAsync<ValidationException>();
    }

    #endregion
}
