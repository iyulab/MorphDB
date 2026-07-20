using Microsoft.Extensions.Logging;
using Moq;
using MorphDB.Npgsql.Schema;
using Npgsql;

namespace MorphDB.Tests.Integration;

/// <summary>
/// A database that already booted an older MorphDB keeps whatever that version created. The
/// canonical DDL is IF NOT EXISTS throughout, so it would step over the remnants forever; the fix
/// has to reach existing deployments explicitly.
/// </summary>
public class GlobalSchemaMigrationTests
{
    /// <summary>The pre-0.7 control-plane shape, reduced to what the migration has to handle.</summary>
    private const string LegacyControlPlane = """
        CREATE SCHEMA IF NOT EXISTS morphdb;

        CREATE TABLE morphdb._morph_organizations (
            org_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
            name VARCHAR(100) NOT NULL,
            slug VARCHAR(100) NOT NULL UNIQUE
        );

        CREATE TABLE morphdb._morph_projects (
            project_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
            org_id UUID REFERENCES morphdb._morph_organizations(org_id) ON DELETE CASCADE,
            name VARCHAR(100) NOT NULL,
            slug VARCHAR(100) NOT NULL UNIQUE,
            system_schema VARCHAR(63) NOT NULL UNIQUE,
            data_schema VARCHAR(63) NOT NULL UNIQUE,
            settings JSONB,
            status INTEGER NOT NULL DEFAULT 0,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );

        CREATE TABLE morphdb._morph_organization_members (
            member_id UUID PRIMARY KEY,
            organization_id UUID NOT NULL,
            user_id VARCHAR(255) NOT NULL
        );
        """;

    /// <summary>
    /// A pre-0.7 database named the scope column tenant_id, in the control plane and in every
    /// per-project system schema, with indexes to match.
    /// </summary>
    private const string LegacyTenantColumns = """
        CREATE SCHEMA IF NOT EXISTS morphdb;

        CREATE TABLE morphdb._morph_tables (
            table_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
            tenant_id UUID NOT NULL,
            logical_name VARCHAR(63) NOT NULL,
            physical_name VARCHAR(63) NOT NULL
        );
        CREATE INDEX idx_morph_tables_tenant ON morphdb._morph_tables(tenant_id);

        CREATE SCHEMA IF NOT EXISTS p_deadbeef_sys;
        CREATE TABLE p_deadbeef_sys._views (
            view_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
            tenant_id UUID NOT NULL,
            logical_name VARCHAR(63) NOT NULL
        );

        CREATE SCHEMA IF NOT EXISTS p_deadbeef_dat;
        CREATE TABLE p_deadbeef_dat.tbl_user_data (
            id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
            tenant_id UUID
        );
        """;

    /// <summary>
    /// Dapper maps columns to properties by convention, so a database still carrying tenant_id would
    /// hand every read an empty id and raise nothing. The rename has to reach existing deployments.
    /// </summary>
    [Fact]
    public async Task Bootstrapping_over_a_pre_0_7_database_renames_the_scope_column()
    {
        await using var db = await CleanDatabase.EmptyAsync("morphdb_legacy_rename");
        await db.ExecuteAsync(LegacyTenantColumns);

        await EnsureGlobalSchemaAsync(db);

        var controlPlane = await db.ReadColumnsAsync("_morph_tables");
        controlPlane.Should().Contain("project_id");
        controlPlane.Should().NotContain("tenant_id");

        var perProject = await db.ReadColumnsAsync("_views", schema: "p_deadbeef_sys");
        perProject.Should().Contain("project_id", "per-project system schemas carry the column too");
        perProject.Should().NotContain("tenant_id");
    }

    /// <summary>
    /// The data schema holds user tables. A column a user chose to call tenant_id is their column, and
    /// renaming it would be this layer reaching into data it does not own.
    /// </summary>
    [Fact]
    public async Task The_rename_does_not_reach_into_user_data_schemas()
    {
        await using var db = await CleanDatabase.EmptyAsync("morphdb_legacy_rename_scope");
        await db.ExecuteAsync(LegacyTenantColumns);

        await EnsureGlobalSchemaAsync(db);

        var userTable = await db.ReadColumnsAsync("tbl_user_data", schema: "p_deadbeef_dat");
        userTable.Should().Contain("tenant_id", "a user's own column must be left alone");
    }

    /// <summary>
    /// RENAME COLUMN fails outright if the old name is gone, and the bootstrap runs on every start —
    /// so a rename that only works once would be a crash loop rather than a migration.
    /// </summary>
    [Fact]
    public async Task Renaming_twice_is_a_no_op()
    {
        await using var db = await CleanDatabase.EmptyAsync("morphdb_legacy_rename_twice");
        await db.ExecuteAsync(LegacyTenantColumns);

        await EnsureGlobalSchemaAsync(db);
        await EnsureGlobalSchemaAsync(db);

        var columns = await db.ReadColumnsAsync("_morph_tables");
        columns.Should().Contain("project_id");
    }

    /// <summary>
    /// The canonical DDL creates its indexes with IF NOT EXISTS under the new names. An index left
    /// under the old name is not seen, so a second identical index gets built beside it.
    /// </summary>
    [Fact]
    public async Task Bootstrapping_renames_the_indexes_rather_than_duplicating_them()
    {
        await using var db = await CleanDatabase.EmptyAsync("morphdb_legacy_rename_indexes");
        await db.ExecuteAsync(LegacyTenantColumns);

        await EnsureGlobalSchemaAsync(db);

        var indexes = await db.ReadIndexesAsync("_morph_tables");
        indexes.Should().NotContain(i => i.Contains("tenant"), "the old name must not survive");
        indexes.Should().Contain(i => i.Contains("project"));
        indexes.Where(i => i.Contains("project")).Should().HaveCount(1, "renaming must not leave a twin");
    }

    [Fact]
    public async Task Bootstrapping_over_a_pre_0_7_database_removes_the_control_plane_remnants()
    {
        await using var db = await CleanDatabase.EmptyAsync("morphdb_legacy_cleanup");
        await db.ExecuteAsync(LegacyControlPlane);

        await EnsureGlobalSchemaAsync(db);

        var tables = await db.ReadTablesAsync();
        tables.Should().NotContain("_morph_organizations");
        tables.Should().NotContain("_morph_organization_members");

        var projectColumns = await db.ReadColumnsAsync("_morph_projects");
        projectColumns.Should().NotContain("org_id", "the project -> organization link is gone");
        projectColumns.Should().Contain("system_schema", "schema isolation must survive the cleanup");
    }

    [Fact]
    public async Task Bootstrapping_twice_over_a_legacy_database_is_a_no_op()
    {
        // The bootstrap runs on every start, so a migration that only works once is a crash loop.
        await using var db = await CleanDatabase.EmptyAsync("morphdb_legacy_twice");
        await db.ExecuteAsync(LegacyControlPlane);

        await EnsureGlobalSchemaAsync(db);
        await EnsureGlobalSchemaAsync(db);

        var tables = await db.ReadTablesAsync();
        tables.Should().NotContain("_morph_organizations");
        tables.Should().Contain("_morph_projects");
    }

    [Fact]
    public async Task Bootstrapping_a_brand_new_database_is_unaffected_by_the_migration()
    {
        await using var db = await CleanDatabase.EmptyAsync("morphdb_fresh_with_migration");

        await EnsureGlobalSchemaAsync(db);

        var tables = await db.ReadTablesAsync();
        tables.Should().Contain("_morph_projects");
        tables.Should().Contain("_morph_views");
        tables.Should().Contain("_morph_security_policies");
    }

    private static async Task EnsureGlobalSchemaAsync(CleanDatabase db)
    {
        await using var dataSource = NpgsqlDataSource.Create(db.ConnectionString);
        var service = new PostgresSchemaLayerService(
            dataSource,
            new PostgresSchemaNameResolver(),
            new Mock<ILogger<PostgresSchemaLayerService>>().Object);

        await service.EnsureGlobalSchemaAsync();
    }
}
