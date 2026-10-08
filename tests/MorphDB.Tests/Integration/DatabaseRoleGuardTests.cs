using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Exceptions;
using MorphDB.Npgsql;
using MorphDB.Npgsql.Repositories;
using MorphDB.Npgsql.Schema;
using MorphDB.Tests.Fixtures;
using Npgsql;
using Testcontainers.PostgreSql;

namespace MorphDB.Tests.Integration;

/// <summary>
/// MorphDB refuses to start on a database role PostgreSQL exempts from row-level security — a
/// superuser, or a role with BYPASSRLS. On such a role every policy would be stored and never
/// enforced, with nothing anywhere saying so. These tests use their own container because they
/// create and connect as roles the shared fixture must not see.
/// </summary>
public sealed class DatabaseRoleGuardTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = TestPostgres.Create("morphdb_role_guard_test");

    public async ValueTask InitializeAsync() => await _container.StartAsync();

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    [Fact]
    public async Task A_superuser_connection_is_refused_before_anything_is_created()
    {
        var act = () => BootstrapAsync(_container.GetConnectionString());

        var refusal = await act.Should().ThrowAsync<InsecureDatabaseRoleException>();
        refusal.Which.IsSuperuser.Should().BeTrue();
        refusal.Which.ErrorCode.Should().Be("INSECURE_DATABASE_ROLE");
        refusal.Which.Message.Should().Contain("NOSUPERUSER NOBYPASSRLS",
            "the refusal has to say what role to connect as instead");

        (await GlobalSchemaExistsAsync()).Should().BeFalse(
            "the refusal comes before the bootstrap, so a refused start leaves nothing owned by the wrong role");
    }

    [Fact]
    public async Task A_role_with_bypassrls_is_refused_even_when_it_is_not_a_superuser()
    {
        await ExecuteAsSuperuserAsync(
            "CREATE ROLE bypasser LOGIN PASSWORD 'bypasser' NOSUPERUSER BYPASSRLS");
        var connectionString = new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
        {
            Username = "bypasser",
            Password = "bypasser"
        }.ConnectionString;

        var act = () => BootstrapAsync(connectionString);

        var refusal = await act.Should().ThrowAsync<InsecureDatabaseRoleException>();
        refusal.Which.IsSuperuser.Should().BeFalse();
        refusal.Which.BypassesRowLevelSecurity.Should().BeTrue();
        refusal.Which.RoleName.Should().Be("bypasser");
    }

    [Fact]
    public async Task The_service_role_from_init_sql_is_accepted_and_owns_what_it_creates()
    {
        await BootstrapAsync(TestPostgres.ServiceConnectionString(_container));

        await using var connection = new NpgsqlConnection(_container.GetConnectionString());
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT pg_get_userbyid(nspowner) FROM pg_namespace WHERE nspname = 'morphdb'", connection);
        var owner = (string?)await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);

        owner.Should().Be(TestPostgres.ServiceRole);
    }

    [Fact]
    public async Task Start_up_surfaces_the_refusal_at_once_instead_of_retrying_it_as_an_unreachable_database()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMorphDbNpgsql(_container.GetConnectionString());
        await using var provider = services.BuildServiceProvider();

        var stopwatch = Stopwatch.StartNew();
        var act = () => provider.EnsureMorphDbSchemaAsync(TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InsecureDatabaseRoleException>();
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10),
            "a role problem is a configuration fault — waiting out the 60-second reachability window would only delay it");
    }

    private static async Task BootstrapAsync(string connectionString)
    {
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        var resolver = new PostgresSchemaNameResolver();
        ISchemaLayerService schemaLayer = new PostgresSchemaLayerService(
            dataSource,
            resolver,
            new ProjectRepository(dataSource, resolver),
            NullLogger<PostgresSchemaLayerService>.Instance);

        await schemaLayer.EnsureGlobalSchemaAsync(TestContext.Current.CancellationToken);
    }

    private async Task<bool> GlobalSchemaExistsAsync()
    {
        await using var connection = new NpgsqlConnection(_container.GetConnectionString());
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT EXISTS (SELECT 1 FROM pg_namespace WHERE nspname = 'morphdb')", connection);
        return (bool)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    private async Task ExecuteAsSuperuserAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(_container.GetConnectionString());
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }
}
