using Npgsql;
using Testcontainers.PostgreSql;

namespace MorphDB.Tests.Fixtures;

/// <summary>
/// Shared PostgreSQL container for integration tests.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:15-alpine")
        .WithDatabase("morphdb_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public NpgsqlDataSource DataSource { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        // Initialize schema
        await InitializeSchemaAsync();

        // Create data source for tests
        var dataSourceBuilder = new NpgsqlDataSourceBuilder(ConnectionString);
        dataSourceBuilder.EnableDynamicJson();
        DataSource = dataSourceBuilder.Build();
    }

    private async Task InitializeSchemaAsync()
    {
        var initSql = GetInitSql();
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var cmd = new NpgsqlCommand(initSql, connection);
        await cmd.ExecuteNonQueryAsync();
    }

    private static string GetInitSql() => """
        -- Enable required extensions
        CREATE EXTENSION IF NOT EXISTS "uuid-ossp";
        CREATE EXTENSION IF NOT EXISTS "pgcrypto";

        -- Create morphdb schema for system tables
        CREATE SCHEMA IF NOT EXISTS morphdb;

        -- System table: _morph_tables
        CREATE TABLE IF NOT EXISTS morphdb._morph_tables (
            table_id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
            tenant_id UUID NOT NULL,
            logical_name VARCHAR(255) NOT NULL,
            physical_name VARCHAR(63) NOT NULL UNIQUE,
            schema_version INTEGER NOT NULL DEFAULT 1,
            descriptor JSONB,
            is_active BOOLEAN NOT NULL DEFAULT true,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            UNIQUE (tenant_id, logical_name)
        );

        -- System table: _morph_columns
        CREATE TABLE IF NOT EXISTS morphdb._morph_columns (
            column_id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
            table_id UUID NOT NULL REFERENCES morphdb._morph_tables(table_id) ON DELETE CASCADE,
            logical_name VARCHAR(255) NOT NULL,
            physical_name VARCHAR(63) NOT NULL,
            data_type VARCHAR(50) NOT NULL,
            native_type VARCHAR(100) NOT NULL,
            is_nullable BOOLEAN NOT NULL DEFAULT true,
            is_unique BOOLEAN NOT NULL DEFAULT false,
            is_primary_key BOOLEAN NOT NULL DEFAULT false,
            is_indexed BOOLEAN NOT NULL DEFAULT false,
            is_encrypted BOOLEAN NOT NULL DEFAULT false,
            default_value TEXT,
            check_expr TEXT,
            ordinal_position INTEGER NOT NULL,
            descriptor JSONB,
            is_active BOOLEAN NOT NULL DEFAULT true,
            UNIQUE (table_id, logical_name),
            UNIQUE (table_id, physical_name)
        );

        -- System table: _morph_relations
        CREATE TABLE IF NOT EXISTS morphdb._morph_relations (
            relation_id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
            tenant_id UUID NOT NULL,
            logical_name VARCHAR(255) NOT NULL,
            source_table_id UUID NOT NULL REFERENCES morphdb._morph_tables(table_id),
            source_column_id UUID NOT NULL REFERENCES morphdb._morph_columns(column_id),
            target_table_id UUID NOT NULL REFERENCES morphdb._morph_tables(table_id),
            target_column_id UUID NOT NULL REFERENCES morphdb._morph_columns(column_id),
            relation_type VARCHAR(20) NOT NULL,
            on_delete VARCHAR(20) NOT NULL DEFAULT 'NO ACTION',
            on_update VARCHAR(20) NOT NULL DEFAULT 'NO ACTION',
            descriptor JSONB,
            is_active BOOLEAN NOT NULL DEFAULT true,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );

        -- System table: _morph_indexes
        CREATE TABLE IF NOT EXISTS morphdb._morph_indexes (
            index_id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
            table_id UUID NOT NULL REFERENCES morphdb._morph_tables(table_id) ON DELETE CASCADE,
            logical_name VARCHAR(255) NOT NULL,
            physical_name VARCHAR(63) NOT NULL UNIQUE,
            columns JSONB NOT NULL,
            index_type VARCHAR(20) NOT NULL DEFAULT 'btree',
            is_unique BOOLEAN NOT NULL DEFAULT false,
            where_clause TEXT,
            descriptor JSONB,
            is_active BOOLEAN NOT NULL DEFAULT true,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );

        -- System table: _morph_changelog
        CREATE TABLE IF NOT EXISTS morphdb._morph_changelog (
            change_id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
            table_id UUID NOT NULL,
            operation VARCHAR(50) NOT NULL,
            schema_version INTEGER NOT NULL,
            changes JSONB NOT NULL,
            performed_by VARCHAR(255),
            performed_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );

        -- Create indexes for system tables
        CREATE INDEX IF NOT EXISTS idx_morph_tables_tenant ON morphdb._morph_tables(tenant_id);
        CREATE INDEX IF NOT EXISTS idx_morph_columns_table ON morphdb._morph_columns(table_id);
        CREATE INDEX IF NOT EXISTS idx_morph_relations_source ON morphdb._morph_relations(source_table_id);
        CREATE INDEX IF NOT EXISTS idx_morph_relations_target ON morphdb._morph_relations(target_table_id);
        CREATE INDEX IF NOT EXISTS idx_morph_changelog_table ON morphdb._morph_changelog(table_id);
        """;

    public async Task DisposeAsync()
    {
        DataSource.Dispose();
        await _container.DisposeAsync();
    }
}

[CollectionDefinition("PostgreSQL")]
public class PostgresCollection : ICollectionFixture<PostgresFixture>
{
}
