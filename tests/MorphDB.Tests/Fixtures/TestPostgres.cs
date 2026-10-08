using Npgsql;
using Testcontainers.PostgreSql;

namespace MorphDB.Tests.Fixtures;

/// <summary>
/// Builds the PostgreSQL containers the suite runs against. Every one is initialised by the
/// repository's <c>scripts/init.sql</c> — the script the compose files mount — so the code under test
/// connects as the role a deployment uses: <c>NOSUPERUSER NOBYPASSRLS</c>, owner of the database.
/// Connecting as the image's bootstrap superuser instead would hide any path that only works with
/// superuser rights, and MorphDB refuses such a role at start-up anyway.
/// </summary>
public static class TestPostgres
{
    /// <summary>The service role <c>scripts/init.sql</c> creates.</summary>
    public const string ServiceRole = "morph";

    private const string ServicePassword = "morph";

    public static PostgreSqlContainer Create(string database) =>
        new PostgreSqlBuilder("postgres:15-alpine")
            .WithDatabase(database)
            .WithUsername("test")
            .WithPassword("test")
            .WithResourceMapping(
                new FileInfo(Path.Combine(AppContext.BaseDirectory, "init.sql")),
                "/docker-entrypoint-initdb.d/")
            .Build();

    /// <summary>The connection string for the service role.</summary>
    public static string ServiceConnectionString(PostgreSqlContainer container) =>
        new NpgsqlConnectionStringBuilder(container.GetConnectionString())
        {
            Username = ServiceRole,
            Password = ServicePassword
        }.ConnectionString;
}
