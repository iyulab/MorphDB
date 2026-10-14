using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Exceptions;
using MorphDB.Npgsql;
using MorphDB.Npgsql.Infrastructure;
using MorphDB.Npgsql.Repositories;
using MorphDB.Npgsql.Schema;
using MorphDB.Npgsql.Security;
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
    public async Task A_login_role_that_cannot_switch_to_the_bypass_role_is_refused()
    {
        await ExecuteAsSuperuserAsync("CREATE ROLE lonely LOGIN PASSWORD 'lonely' NOSUPERUSER NOBYPASSRLS");
        await ExecuteAsSuperuserAsync("CREATE DATABASE lonely_db OWNER lonely");
        var connectionString = new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
        {
            Database = "lonely_db",
            Username = "lonely",
            Password = "lonely"
        }.ConnectionString;

        var notAMember = () => BootstrapAsync(connectionString);
        var refusal = await notAMember.Should().ThrowAsync<MissingBypassRoleException>();
        refusal.Which.ErrorCode.Should().Be("MISSING_BYPASS_ROLE");
        refusal.Which.LoginRole.Should().Be("lonely");
        refusal.Which.BypassRoleExists.Should().BeTrue();
        refusal.Which.Message.Should().Contain("GRANT morphdb_bypass TO lonely",
            "the refusal has to say what to run");

        var noSuchRole = () => BootstrapAsync(TestPostgres.ServiceConnectionString(_container), bypassRole: "no_such_role");
        (await noSuchRole.Should().ThrowAsync<MissingBypassRoleException>()).Which.BypassRoleExists.Should().BeFalse();
    }

    [Fact]
    public async Task A_view_an_earlier_version_created_reads_as_its_caller_after_start_up()
    {
        await BootstrapAsync(TestPostgres.ServiceConnectionString(_container));
        await using (var service = new NpgsqlConnection(TestPostgres.ServiceConnectionString(_container)))
        {
            await service.OpenAsync(TestContext.Current.CancellationToken);
            await using var create = new NpgsqlCommand("CREATE VIEW public.older_view AS SELECT 1 AS one", service);
            await create.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        await BootstrapAsync(TestPostgres.ServiceConnectionString(_container));

        await using var connection = new NpgsqlConnection(_container.GetConnectionString());
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT array_to_string(reloptions, ',') FROM pg_class WHERE oid = 'public.older_view'::regclass", connection);
        ((string?)await command.ExecuteScalarAsync(TestContext.Current.CancellationToken)).Should().Be("security_invoker=true");
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

    private static async Task BootstrapAsync(string connectionString, string bypassRole = MorphConnectionFactory.DefaultBypassRole)
    {
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        var connections = new MorphConnectionFactory(dataSource, new SecurityContextAccessor(), bypassRole);
        var resolver = new PostgresSchemaNameResolver();
        ISchemaLayerService schemaLayer = new PostgresSchemaLayerService(
            connections,
            resolver,
            new ProjectRepository(connections, resolver),
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
