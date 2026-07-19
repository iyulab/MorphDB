using Microsoft.Extensions.Logging;
using MorphDB.Core.Models;
using MorphDB.Npgsql.Ddl;
using MorphDB.Npgsql.Schema;
using Moq;
using Npgsql;
using Testcontainers.PostgreSql;

namespace MorphDB.Tests.Integration;

/// <summary>
/// Guards that the control plane boots on a PostgreSQL where the caller may not create extensions.
///
/// Managed PostgreSQL (Azure Flexible Server, Cloud SQL, RDS) gates CREATE EXTENSION behind a
/// server-parameter allow-list, so a bootstrap that creates extensions cannot start there at all —
/// it crash-loops before serving a single request. These tests use their own container rather than
/// the shared fixture because they need a database whose extension set nobody has touched.
/// </summary>
public class ExtensionFreeBootstrapTests
{
    [Fact]
    public async Task Global_bootstrap_runs_without_creating_any_extension()
    {
        var container = new PostgreSqlBuilder("postgres:15-alpine")
            .WithDatabase("morphdb_bootstrap_test")
            .WithUsername("test")
            .WithPassword("test")
            .Build();

        await container.StartAsync();
        try
        {
            await using var connection = new NpgsqlConnection(container.GetConnectionString());
            await connection.OpenAsync();

            var before = await ReadExtensionsAsync(connection);

            await ExecuteAsync(connection, DdlBuilder.BuildGlobalSystemSchemaDdl());

            var after = await ReadExtensionsAsync(connection);
            after.Should().BeEquivalentTo(
                before,
                "the control plane must not require an extension — CREATE EXTENSION is not grantable on managed PostgreSQL");

            // The bootstrap succeeding is not enough: the UUID defaults have to work without uuid-ossp.
            await using var insert = new NpgsqlCommand(
                """
                INSERT INTO morphdb._morph_tables (tenant_id, logical_name, physical_name)
                VALUES (gen_random_uuid(), 'customers', 't_customers')
                RETURNING table_id
                """,
                connection);
            var generated = (Guid)(await insert.ExecuteScalarAsync())!;

            generated.Should().NotBe(Guid.Empty, "the primary key default must generate a real UUID");
        }
        finally
        {
            await container.DisposeAsync();
        }
    }

    [Fact]
    public async Task Provisioning_a_project_creates_no_extension_either()
    {
        var container = new PostgreSqlBuilder("postgres:15-alpine")
            .WithDatabase("morphdb_provision_test")
            .WithUsername("test")
            .WithPassword("test")
            .Build();

        await container.StartAsync();
        try
        {
            await using var dataSource = NpgsqlDataSource.Create(container.GetConnectionString());
            var service = new PostgresSchemaLayerService(
                dataSource,
                new PostgresSchemaNameResolver(),
                new Mock<ILogger<PostgresSchemaLayerService>>().Object);

            await service.EnsureGlobalSchemaAsync();

            await using var connection = await dataSource.OpenConnectionAsync();
            var before = await ReadExtensionsAsync(connection);

            var names = await service.ProvisionProjectSchemasAsync(Guid.NewGuid());

            var after = await ReadExtensionsAsync(connection);
            after.Should().BeEquivalentTo(
                before,
                "creating a tenant must work where the caller cannot create extensions — otherwise the service starts but no project can ever be provisioned");

            names.SystemSchema.Should().NotBeNullOrEmpty();
        }
        finally
        {
            await container.DisposeAsync();
        }
    }

    [Fact]
    public async Task Every_allowed_function_default_works_without_an_extension()
    {
        // The allow-list is a published contract: a caller told "these are supported" must not then
        // meet a database error. Each entry is exercised against a database with nothing installed.
        string[] allowed =
        [
            "gen_random_uuid()",
            "now()",
            "transaction_timestamp()",
            "statement_timestamp()",
            "clock_timestamp()"
        ];

        var container = new PostgreSqlBuilder("postgres:15-alpine")
            .WithDatabase("morphdb_defaults_test")
            .WithUsername("test")
            .WithPassword("test")
            .Build();

        await container.StartAsync();
        try
        {
            await using var connection = new NpgsqlConnection(container.GetConnectionString());
            await connection.OpenAsync();

            foreach (var declared in allowed)
            {
                var isUuid = declared.Contains("uuid", StringComparison.Ordinal);
                var column = ColumnDefinition.FromMetadata(new ColumnMetadata
                {
                    LogicalName = "c",
                    PhysicalName = "c",
                    DataType = isUuid ? MorphDataType.Uuid : MorphDataType.DateTime,
                    NativeType = isUuid ? "UUID" : "TIMESTAMPTZ",
                    DefaultValue = declared
                });

                var table = $"t_{allowed.ToList().IndexOf(declared)}";
                await ExecuteAsync(connection, DdlBuilder.BuildCreateTable(table, [column]));
                await ExecuteAsync(connection, $"INSERT INTO \"{table}\" DEFAULT VALUES");

                await using var read = new NpgsqlCommand($"SELECT c FROM \"{table}\"", connection);
                var value = await read.ExecuteScalarAsync();

                value.Should().NotBeNull($"the default {declared} must produce a value");
            }

            var extensions = await ReadExtensionsAsync(connection);
            extensions.Should().NotContain("uuid-ossp").And.NotContain("pgcrypto");
        }
        finally
        {
            await container.DisposeAsync();
        }
    }

    private static async Task<List<string>> ReadExtensionsAsync(NpgsqlConnection connection)
    {
        await using var command = new NpgsqlCommand("SELECT extname FROM pg_extension ORDER BY extname", connection);
        await using var reader = await command.ExecuteReaderAsync();

        var names = new List<string>();
        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
