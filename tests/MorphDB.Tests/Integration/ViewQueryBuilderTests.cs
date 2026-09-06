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
        var metadataRepository = new MetadataRepository(fixture.DataSource);

        var nameHasher = new Sha256NameHasher();
        var lockManager = new PostgresAdvisoryLockManager(fixture.DataSource, new AdvisoryLockOptions());
        var changeLogger = new ChangeLogger(fixture.DataSource);

        _schemaManager = new PostgresSchemaManager(
            fixture.DataSource,
            metadataRepository,
            lockManager,
            nameHasher,
            changeLogger,
            new ProjectRepository(fixture.DataSource, new PostgresSchemaNameResolver()),
            new SchemaManagerOptions());

        _dataService = fixture.CreateDataService(
            metadataRepository,
            new SecurityPolicyService(fixture.DataSource),
            new SecurityContextAccessor());
    }

    [Fact]
    public async Task BuildSelectStatementAsync_JoinConditionAndExpression_ProduceRunnableTranslatedSql()
    {
        var projectId = Guid.NewGuid();
        var viewBuilder = new ViewQueryBuilder(new MetadataRepository(_fixture.DataSource), projectId);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var customersTable = await _schemaManager.CreateTableAsync(new CreateTableRequest
        {
            ProjectId = projectId,
            LogicalName = "customers_" + suffix,
            Columns = [new CreateColumnRequest { LogicalName = "name", DataType = MorphDataType.Text, IsNullable = false }]
        });

        var ordersTable = await _schemaManager.CreateTableAsync(new CreateTableRequest
        {
            ProjectId = projectId,
            LogicalName = "orders_" + suffix,
            Columns =
            [
                new CreateColumnRequest { LogicalName = "customer_ref", DataType = MorphDataType.Uuid, IsNullable = false },
                new CreateColumnRequest { LogicalName = "amount", DataType = MorphDataType.Decimal, IsNullable = false },
            ]
        });

        var customerId = Guid.NewGuid();
        await _dataService.InsertBatchAsync(projectId, customersTable.LogicalName,
        [
            new Dictionary<string, object?> { ["_id"] = customerId, ["name"] = "Alice" },
        ]);
        await _dataService.InsertBatchAsync(projectId, ordersTable.LogicalName,
        [
            new Dictionary<string, object?> { ["customer_ref"] = customerId, ["amount"] = 100m },
        ]);

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

        var sql = await viewBuilder.BuildSelectStatementAsync(definition);

        await using var connection = await _fixture.DataSource.OpenConnectionAsync();
        var row = await connection.QuerySingleAsync<(decimal Amount, string CustomerName, decimal Doubled)>(sql);

        row.Amount.Should().Be(100m);
        row.CustomerName.Should().Be("Alice",
            "the join condition and the joined-table Source column both had to translate to physical names AND resolve to a table the query actually introduces");
        row.Doubled.Should().Be(200m, "the computed Expression's bare column reference had to translate too");
    }
}
