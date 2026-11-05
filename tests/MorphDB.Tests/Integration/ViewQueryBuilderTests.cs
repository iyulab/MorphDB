using Dapper;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Models;
using MorphDB.Npgsql.Infrastructure;
using MorphDB.Npgsql.Query;
using MorphDB.Npgsql.Repositories;
using MorphDB.Npgsql.Schema;
using MorphDB.Npgsql.Security;
using MorphDB.Npgsql.Services;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration;

/// <summary>
/// Exercises <see cref="ViewQueryBuilder"/> against a real Postgres to pin a fixed defect: the JOIN
/// condition and computed-expression translators used to be verbatim pass-throughs
/// ("Simple translation ... assume conditions use physical names"), so any view with a join
/// condition or expression written in logical column names -- the vocabulary every other view
/// field uses -- produced SQL that either referenced the wrong table (the logical name, not the
/// alias/physical name the FROM/JOIN clause actually introduces) or leaked physical column names.
/// A round-trip that only inspects the generated SQL string couldn't tell the difference between
/// "looks translated" and "actually resolves against Postgres" -- so this runs the generated SQL.
/// </summary>
[Collection("PostgreSQL")]
public class ViewQueryBuilderTests
{
    private readonly PostgresFixture _fixture;
    private readonly PostgresSchemaManager _schemaManager;
    private readonly PostgresDataService _dataService;

    public ViewQueryBuilderTests(PostgresFixture fixture)
    {
        _fixture = fixture;
        var metadataRepository = new MetadataRepository(fixture.Connections);

        var nameHasher = new Sha256NameHasher();
        var lockManager = new PostgresAdvisoryLockManager(fixture.Connections, new AdvisoryLockOptions());
        var changeLogger = new ChangeLogger(fixture.Connections);

        _schemaManager = new PostgresSchemaManager(
            fixture.Connections,
            metadataRepository,
            lockManager,
            nameHasher,
            changeLogger,
            new ProjectRepository(fixture.Connections, new PostgresSchemaNameResolver()),
            new SchemaManagerOptions());

        _dataService = fixture.CreateDataService(metadataRepository, new SecurityContextAccessor());
    }

    [Fact]
    public async Task BuildSelectStatementAsync_JoinConditionAndExpression_ProduceRunnableTranslatedSql()
    {
        var projectId = Guid.NewGuid();
        var viewBuilder = new ViewQueryBuilder(new MetadataRepository(_fixture.Connections), projectId);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var customersTable = await _schemaManager.CreateTableAsync(new CreateTableRequest
        {
            ProjectId = projectId,
            LogicalName = "customers_" + suffix,
            Columns = [new CreateColumnRequest { LogicalName = "name", DataType = MorphDataType.Text, IsNullable = false }]
        }, TestContext.Current.CancellationToken);

        var ordersTable = await _schemaManager.CreateTableAsync(new CreateTableRequest
        {
            ProjectId = projectId,
            LogicalName = "orders_" + suffix,
            Columns =
            [
                new CreateColumnRequest { LogicalName = "customer_ref", DataType = MorphDataType.Uuid, IsNullable = false },
                new CreateColumnRequest { LogicalName = "amount", DataType = MorphDataType.Decimal, IsNullable = false },
            ]
        }, TestContext.Current.CancellationToken);

        var customerId = Guid.NewGuid();
        await _dataService.InsertBatchAsync(projectId, customersTable.LogicalName,
        [
            new Dictionary<string, object?> { ["_id"] = customerId, ["name"] = "Alice" },
        ], TestContext.Current.CancellationToken);
        await _dataService.InsertBatchAsync(projectId, ordersTable.LogicalName,
        [
            new Dictionary<string, object?> { ["customer_ref"] = customerId, ["amount"] = 100m },
        ], TestContext.Current.CancellationToken);

        var definition = new ViewDefinition
        {
            BaseTable = ordersTable.LogicalName,
            Joins =
            [
                new ViewJoinSpec
                {
                    Table = customersTable.LogicalName,
                    JoinType = ViewJoinType.Inner,
                    // Deliberately no Alias -- the join is reachable only by its physical name in
                    // the FROM/JOIN clause, which is exactly the case the old pass-through broke:
                    // it would have quoted the *logical* name "customers_xxx" as the table
                    // qualifier, a name the query never introduces.
                    Condition = $"{ordersTable.LogicalName}.customer_ref = {customersTable.LogicalName}._id",
                },
            ],
            Columns =
            [
                new ViewColumnSpec { Source = "amount", Alias = "amount" },
                new ViewColumnSpec { Source = $"{customersTable.LogicalName}.name", Alias = "customer_name" },
                new ViewColumnSpec { Expression = "amount * 2", Alias = "doubled" },
            ],
        };

        var sql = await viewBuilder.BuildSelectStatementAsync(definition, TestContext.Current.CancellationToken);

        await using var connection = await _fixture.DataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        var row = await connection.QuerySingleAsync<(decimal Amount, string CustomerName, decimal Doubled)>(sql);

        row.Amount.Should().Be(100m);
        row.CustomerName.Should().Be("Alice",
            "the join condition and the joined-table Source column both had to translate to physical names AND resolve to a table the query actually introduces");
        row.Doubled.Should().Be(200m, "the computed Expression's bare column reference had to translate too");
    }

    /// <summary>
    /// A computed column may read columns of <i>both</i> tables in one expression -- here the time between a
    /// timestamp on the base table and one on a joined table -- and a left join keeps the base rows that
    /// have no partner, whose computed value is then NULL rather than a wrong number or a dropped row.
    /// This is the contract that "a delta between two records is a view's job" rests on: an expression that
    /// reaches across the join, qualified by logical table names, runs against Postgres as written.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BuildSelectStatementAsync_ExpressionAcrossJoinedTables_ComputesPerRowDeltaAndLeavesUnmatchedRowsNull(bool quoted)
    {
        var projectId = Guid.NewGuid();
        var viewBuilder = new ViewQueryBuilder(new MetadataRepository(_fixture.Connections), projectId);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var dispatches = await _schemaManager.CreateTableAsync(new CreateTableRequest
        {
            ProjectId = projectId,
            LogicalName = "dispatches_" + suffix,
            Columns =
            [
                new CreateColumnRequest { LogicalName = "lot", DataType = MorphDataType.Text, IsNullable = false },
                new CreateColumnRequest { LogicalName = "sent_at", DataType = MorphDataType.DateTime, IsNullable = false },
            ]
        }, TestContext.Current.CancellationToken);

        var arrivals = await _schemaManager.CreateTableAsync(new CreateTableRequest
        {
            ProjectId = projectId,
            LogicalName = "arrivals_" + suffix,
            Columns =
            [
                new CreateColumnRequest { LogicalName = "lot", DataType = MorphDataType.Text, IsNullable = false },
                new CreateColumnRequest { LogicalName = "arrived_at", DataType = MorphDataType.DateTime, IsNullable = false },
            ]
        }, TestContext.Current.CancellationToken);

        await _dataService.InsertBatchAsync(projectId, dispatches.LogicalName,
        [
            new Dictionary<string, object?> { ["lot"] = "L1", ["sent_at"] = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero) },
            new Dictionary<string, object?> { ["lot"] = "L2", ["sent_at"] = new DateTimeOffset(2026, 11, 1, 6, 0, 0, TimeSpan.Zero) },
            new Dictionary<string, object?> { ["lot"] = "L3", ["sent_at"] = new DateTimeOffset(2026, 11, 2, 0, 0, 0, TimeSpan.Zero) },
        ], TestContext.Current.CancellationToken);
        await _dataService.InsertBatchAsync(projectId, arrivals.LogicalName,
        [
            new Dictionary<string, object?> { ["lot"] = "L1", ["arrived_at"] = new DateTimeOffset(2026, 11, 3, 12, 0, 0, TimeSpan.Zero) },
            new Dictionary<string, object?> { ["lot"] = "L2", ["arrived_at"] = new DateTimeOffset(2026, 11, 2, 6, 0, 0, TimeSpan.Zero) },
        ], TestContext.Current.CancellationToken);

        var definition = new ViewDefinition
        {
            BaseTable = dispatches.LogicalName,
            Joins =
            [
                new ViewJoinSpec
                {
                    Table = arrivals.LogicalName,
                    JoinType = ViewJoinType.Left,
                    Condition = $"{dispatches.LogicalName}.lot = {arrivals.LogicalName}.lot",
                },
            ],
            Columns =
            [
                new ViewColumnSpec { Source = "lot", Alias = "lot" },
                new ViewColumnSpec
                {
                    // Written the way a PostgreSQL user writes a camelCase name -- quoted -- or not: the same reference.
                    Expression = quoted
                        ? $"EXTRACT(EPOCH FROM ({arrivals.LogicalName}.\"arrived_at\" - \"{dispatches.LogicalName}\".\"sent_at\")) / 3600"
                        : $"EXTRACT(EPOCH FROM ({arrivals.LogicalName}.arrived_at - {dispatches.LogicalName}.sent_at)) / 3600",
                    Alias = "lead_hours",
                },
            ],
        };

        var sql = await viewBuilder.BuildSelectStatementAsync(definition, TestContext.Current.CancellationToken);

        await using var connection = await _fixture.DataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        var rows = (await connection.QueryAsync<(string Lot, decimal? LeadHours)>($"SELECT * FROM ({sql}) AS v ORDER BY 1"))
            .ToList();

        rows.Select(r => r.Lot).Should().Equal("L1", "L2", "L3");
        rows[0].LeadHours.Should().Be(60m, "L1 arrived 60 hours after it was sent");
        rows[1].LeadHours.Should().Be(24m, "L2 arrived 24 hours after it was sent");
        rows[2].LeadHours.Should().BeNull("a left join keeps the unmatched row, and a delta with no partner is NULL");
    }
}
