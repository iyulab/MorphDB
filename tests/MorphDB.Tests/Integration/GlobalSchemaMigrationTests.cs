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
