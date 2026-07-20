namespace MorphDB.Tests.Integration;

/// <summary>
/// The global bootstrap DDL is the only thing that runs against a real deployment's control plane
/// (PostgresSchemaLayerService.EnsureGlobalSchemaAsync). Every table and column the production
/// repositories name in SQL must therefore exist after it runs — on a database nobody else touched.
///
/// The shared PostgresFixture could not prove this while it hand-copied the schema instead of
/// calling the builder: it stayed green no matter what the builder emitted. That divergence is how
/// view metadata and security policies came to be queried by code that shipped, against tables the
/// bootstrap never created.
/// </summary>
public class GlobalSchemaContractTests
{
    /// <summary>
    /// Table to the columns production SQL names on it. Sources are cited so a future reader can
    /// re-derive the list rather than trust it.
    /// </summary>
    public static readonly (string Table, string[] Columns)[] RequiredColumns =
    [
        // MetadataRepository / DdlBuilder
        ("_morph_tables", ["table_id", "project_id", "logical_name", "physical_name", "schema_version", "descriptor", "is_active", "created_at", "updated_at"]),
        ("_morph_columns", ["column_id", "table_id", "logical_name", "physical_name", "data_type", "native_type", "is_nullable", "ordinal_position"]),
        // ProjectRepository — org_id is deliberately absent: organizations were removed as a use assumption.
        ("_morph_projects", ["project_id", "name", "slug", "system_schema", "data_schema", "settings", "status", "created_at", "updated_at"]),
        // ApiKeyService
        ("_morph_api_keys", ["id", "project_id", "key_type", "key_hash", "key_prefix", "name", "description", "is_active", "created_at", "expires_at", "last_used_at"]),
        // SecurityPolicyService — INSERT and SELECT column lists
        ("_morph_security_policies", ["id", "project_id", "table_id", "name", "description", "policy_type", "expression", "is_active", "ordinal_position", "created_at", "updated_at"]),
        // ViewMetadataRepository — INSERT ... RETURNING column list
        ("_morph_views", ["view_id", "project_id", "logical_name", "physical_name", "definition", "is_materialized", "refresh_policy", "refresh_schedule", "last_refreshed_at", "is_stale", "descriptor", "is_active", "created_at", "updated_at"]),
        ("_morph_view_columns", ["column_id", "view_id", "logical_name", "data_type", "is_computed", "expression", "ordinal_position"]),
    ];

    public static TheoryData<string, string[]> RequiredColumnCases()
    {
        var data = new TheoryData<string, string[]>();
        foreach (var (table, columns) in RequiredColumns)
        {
            data.Add(table, columns);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(RequiredColumnCases))]
    public async Task Global_bootstrap_creates_every_table_production_queries(string table, string[] columns)
    {
        await using var db = await CleanDatabase.BootstrapAsync($"morphdb_contract_{table}");

        var actual = await db.ReadColumnsAsync(table);

        actual.Should().NotBeEmpty(
            $"morphdb.{table} is queried by production code, so the global bootstrap must create it");
        actual.Should().Contain(
            columns,
            $"production SQL names these columns on morphdb.{table}");
    }

    [Fact]
    public async Task The_container_init_script_does_not_keep_a_second_copy_of_the_schema()
    {
        // scripts/init.sql seeds the compose Postgres before the service starts. It used to repeat
        // every CREATE TABLE, which is a copy that can only drift -- and did. The service bootstraps
        // its own schema, so the script's only remaining job is granting privileges to the container
        // role, which the service cannot grant itself.
        var statements = await ReadStatementsAsync(LocateRepoFile("scripts/init.sql"));

        statements.Should().NotContain(
            "CREATE TABLE",
            "the schema has one source: DdlBuilder.BuildGlobalSystemSchemaDdl()");
        statements.Should().Contain(
            "GRANT",
            "granting to the container role is the one thing only this script can do");
        statements.Should().NotContain(
            "CREATE EXTENSION",
            "morphdb must boot on a managed PostgreSQL that forbids extensions");
    }

    /// <summary>
    /// Reads a SQL file with the comment lines stripped, so assertions see what the database will
    /// execute rather than what the file says about itself.
    /// </summary>
    private static async Task<string> ReadStatementsAsync(string path)
    {
        var lines = await File.ReadAllLinesAsync(path);

        return string.Join(
            '\n',
            lines.Where(line => !line.TrimStart().StartsWith("--", StringComparison.Ordinal)));
    }

    private static string LocateRepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "scripts")))
        {
            dir = dir.Parent;
        }

        dir.Should().NotBeNull("the test must be able to find the repository root");
        return Path.Combine(dir!.FullName, relativePath);
    }

    [Fact]
    public async Task Global_bootstrap_does_not_create_the_removed_control_plane_tables()
    {
        // These belonged to organizations, membership, invitations, SSO and backups — assumptions
        // about what this database is used for. Nothing should quietly bring them back.
        string[] removed =
        [
            "_morph_organizations",
            "_morph_organization_members",
            "_morph_project_members",
            "_morph_organization_invitations",
            "_morph_sso_configurations",
            "_morph_backups"
        ];

        await using var db = await CleanDatabase.BootstrapAsync("morphdb_contract_removed");

        var tables = await db.ReadTablesAsync();

        tables.Should().NotIntersectWith(
            removed,
            "the SaaS control plane was removed as a violation of use-agnostic scope");
    }
}
