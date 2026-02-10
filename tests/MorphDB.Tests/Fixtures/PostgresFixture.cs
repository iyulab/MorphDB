using Npgsql;
using Testcontainers.PostgreSql;

namespace MorphDB.Tests.Fixtures;

/// <summary>
/// Shared PostgreSQL container for integration tests.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:15-alpine")
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
            -- Virtual/Derived column configurations (Phase 11+)
            lookup_config JSONB,
            rollup_config JSONB,
            formula_config JSONB,
            computed_config JSONB,
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

        -- System table: _morph_webhooks
        CREATE TABLE IF NOT EXISTS morphdb._morph_webhooks (
            webhook_id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
            tenant_id UUID NOT NULL,
            table_id UUID NOT NULL REFERENCES morphdb._morph_tables(table_id) ON DELETE CASCADE,
            logical_name VARCHAR(255) NOT NULL,
            url VARCHAR(2048) NOT NULL,
            secret VARCHAR(64) NOT NULL,
            events VARCHAR(20)[] NOT NULL DEFAULT ARRAY['insert', 'update', 'delete'],
            filter JSONB,
            headers JSONB,
            is_active BOOLEAN NOT NULL DEFAULT true,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            UNIQUE (tenant_id, logical_name)
        );

        -- System table: _morph_webhook_deliveries
        CREATE TABLE IF NOT EXISTS morphdb._morph_webhook_deliveries (
            delivery_id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
            webhook_id UUID NOT NULL REFERENCES morphdb._morph_webhooks(webhook_id) ON DELETE CASCADE,
            record_id UUID,
            event VARCHAR(20) NOT NULL,
            payload JSONB NOT NULL,
            status VARCHAR(20) NOT NULL DEFAULT 'pending',
            attempt_count INTEGER NOT NULL DEFAULT 0,
            http_status_code INTEGER,
            response_body TEXT,
            error_message TEXT,
            next_retry_at TIMESTAMPTZ,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            delivered_at TIMESTAMPTZ
        );

        -- Create indexes for system tables
        CREATE INDEX IF NOT EXISTS idx_morph_tables_tenant ON morphdb._morph_tables(tenant_id);
        CREATE INDEX IF NOT EXISTS idx_morph_columns_table ON morphdb._morph_columns(table_id);
        CREATE INDEX IF NOT EXISTS idx_morph_relations_source ON morphdb._morph_relations(source_table_id);
        CREATE INDEX IF NOT EXISTS idx_morph_relations_target ON morphdb._morph_relations(target_table_id);
        CREATE INDEX IF NOT EXISTS idx_morph_changelog_table ON morphdb._morph_changelog(table_id);
        CREATE INDEX IF NOT EXISTS idx_morph_webhooks_tenant ON morphdb._morph_webhooks(tenant_id);
        CREATE INDEX IF NOT EXISTS idx_morph_webhooks_table ON morphdb._morph_webhooks(table_id);
        CREATE INDEX IF NOT EXISTS idx_morph_webhook_deliveries_webhook ON morphdb._morph_webhook_deliveries(webhook_id);
        CREATE INDEX IF NOT EXISTS idx_morph_webhook_deliveries_status ON morphdb._morph_webhook_deliveries(status) WHERE status IN ('pending', 'retrying');

        -- System table: _morph_import_jobs
        CREATE TABLE IF NOT EXISTS morphdb._morph_import_jobs (
            job_id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
            tenant_id UUID NOT NULL,
            table_id UUID NOT NULL REFERENCES morphdb._morph_tables(table_id) ON DELETE CASCADE,
            table_name VARCHAR(255) NOT NULL,
            format VARCHAR(20) NOT NULL,
            status VARCHAR(20) NOT NULL DEFAULT 'pending',
            total_rows BIGINT NOT NULL DEFAULT 0,
            processed_rows BIGINT NOT NULL DEFAULT 0,
            success_count BIGINT NOT NULL DEFAULT 0,
            error_count BIGINT NOT NULL DEFAULT 0,
            error_message TEXT,
            options JSONB,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            started_at TIMESTAMPTZ,
            completed_at TIMESTAMPTZ
        );

        -- System table: _morph_export_jobs
        CREATE TABLE IF NOT EXISTS morphdb._morph_export_jobs (
            job_id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
            tenant_id UUID NOT NULL,
            table_id UUID NOT NULL REFERENCES morphdb._morph_tables(table_id) ON DELETE CASCADE,
            table_name VARCHAR(255) NOT NULL,
            format VARCHAR(20) NOT NULL,
            status VARCHAR(20) NOT NULL DEFAULT 'pending',
            total_rows BIGINT NOT NULL DEFAULT 0,
            processed_rows BIGINT NOT NULL DEFAULT 0,
            file_path VARCHAR(1024),
            file_size BIGINT,
            error_message TEXT,
            options JSONB,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            started_at TIMESTAMPTZ,
            completed_at TIMESTAMPTZ,
            expires_at TIMESTAMPTZ
        );

        -- System table: _morph_import_data (temporary storage for import data)
        CREATE TABLE IF NOT EXISTS morphdb._morph_import_data (
            job_id UUID PRIMARY KEY REFERENCES morphdb._morph_import_jobs(job_id) ON DELETE CASCADE,
            data BYTEA NOT NULL,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );

        CREATE INDEX IF NOT EXISTS idx_morph_import_jobs_tenant ON morphdb._morph_import_jobs(tenant_id);
        CREATE INDEX IF NOT EXISTS idx_morph_import_jobs_status ON morphdb._morph_import_jobs(status) WHERE status IN ('pending', 'processing');
        CREATE INDEX IF NOT EXISTS idx_morph_export_jobs_tenant ON morphdb._morph_export_jobs(tenant_id);
        CREATE INDEX IF NOT EXISTS idx_morph_export_jobs_status ON morphdb._morph_export_jobs(status) WHERE status IN ('pending', 'processing');
        CREATE INDEX IF NOT EXISTS idx_morph_export_jobs_expires ON morphdb._morph_export_jobs(expires_at) WHERE expires_at IS NOT NULL;

        -- System table: _morph_projects (Phase 17-18: Project-based multi-tenancy)
        CREATE TABLE IF NOT EXISTS morphdb._morph_projects (
            project_id UUID PRIMARY KEY,
            org_id UUID,
            name VARCHAR(255) NOT NULL,
            slug VARCHAR(255) NOT NULL UNIQUE,
            system_schema VARCHAR(63) NOT NULL,
            data_schema VARCHAR(63) NOT NULL,
            settings JSONB,
            status INTEGER NOT NULL DEFAULT 0,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );

        CREATE INDEX IF NOT EXISTS idx_morph_projects_org ON morphdb._morph_projects(org_id) WHERE org_id IS NOT NULL;
        CREATE INDEX IF NOT EXISTS idx_morph_projects_slug ON morphdb._morph_projects(slug);
        CREATE INDEX IF NOT EXISTS idx_morph_projects_status ON morphdb._morph_projects(status) WHERE status NOT IN (6);

        -- System table: _morph_organizations (Phase 21)
        CREATE TABLE IF NOT EXISTS morphdb._morph_organizations (
            organization_id UUID PRIMARY KEY,
            name VARCHAR(255) NOT NULL,
            slug VARCHAR(255) NOT NULL UNIQUE,
            description TEXT,
            settings JSONB,
            status INTEGER NOT NULL DEFAULT 1,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );

        CREATE INDEX IF NOT EXISTS idx_organizations_slug ON morphdb._morph_organizations(slug);
        CREATE INDEX IF NOT EXISTS idx_organizations_status ON morphdb._morph_organizations(status) WHERE status != 3;

        -- System table: _morph_organization_members (Phase 21)
        CREATE TABLE IF NOT EXISTS morphdb._morph_organization_members (
            member_id UUID PRIMARY KEY,
            organization_id UUID NOT NULL REFERENCES morphdb._morph_organizations(organization_id) ON DELETE CASCADE,
            user_id VARCHAR(255) NOT NULL,
            email VARCHAR(255) NOT NULL,
            display_name VARCHAR(255),
            role INTEGER NOT NULL DEFAULT 10,
            status INTEGER NOT NULL DEFAULT 1,
            joined_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            invited_by VARCHAR(255),
            UNIQUE (organization_id, user_id)
        );

        CREATE INDEX IF NOT EXISTS idx_org_members_org_id ON morphdb._morph_organization_members (organization_id);
        CREATE INDEX IF NOT EXISTS idx_org_members_user_id ON morphdb._morph_organization_members (user_id);
        CREATE INDEX IF NOT EXISTS idx_org_members_email ON morphdb._morph_organization_members (email);

        -- System table: _morph_project_members (Phase 21)
        CREATE TABLE IF NOT EXISTS morphdb._morph_project_members (
            member_id UUID PRIMARY KEY,
            project_id UUID NOT NULL REFERENCES morphdb._morph_projects(project_id) ON DELETE CASCADE,
            user_id VARCHAR(255) NOT NULL,
            email VARCHAR(255) NOT NULL,
            display_name VARCHAR(255),
            role INTEGER NOT NULL DEFAULT 10,
            status INTEGER NOT NULL DEFAULT 1,
            joined_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            UNIQUE (project_id, user_id)
        );

        CREATE INDEX IF NOT EXISTS idx_proj_members_project_id ON morphdb._morph_project_members (project_id);
        CREATE INDEX IF NOT EXISTS idx_proj_members_user_id ON morphdb._morph_project_members (user_id);

        -- System table: _morph_organization_invitations (Phase 21)
        CREATE TABLE IF NOT EXISTS morphdb._morph_organization_invitations (
            invitation_id UUID PRIMARY KEY,
            organization_id UUID NOT NULL REFERENCES morphdb._morph_organizations(organization_id) ON DELETE CASCADE,
            email VARCHAR(255) NOT NULL,
            role INTEGER NOT NULL DEFAULT 10,
            token VARCHAR(255) NOT NULL UNIQUE,
            status INTEGER NOT NULL DEFAULT 0,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            expires_at TIMESTAMPTZ NOT NULL,
            invited_by VARCHAR(255) NOT NULL
        );

        CREATE INDEX IF NOT EXISTS idx_org_invitations_token ON morphdb._morph_organization_invitations (token) WHERE status = 0;
        CREATE INDEX IF NOT EXISTS idx_org_invitations_org ON morphdb._morph_organization_invitations (organization_id);

        -- System table: _morph_sso_configurations (Phase 22)
        CREATE TABLE IF NOT EXISTS morphdb._morph_sso_configurations (
            sso_config_id UUID PRIMARY KEY,
            organization_id UUID NOT NULL REFERENCES morphdb._morph_organizations(organization_id) ON DELETE CASCADE,
            name VARCHAR(255) NOT NULL,
            provider_type INTEGER NOT NULL DEFAULT 0,
            authority VARCHAR(1024) NOT NULL,
            client_id VARCHAR(255) NOT NULL,
            client_secret_encrypted TEXT,
            scopes TEXT[] NOT NULL DEFAULT ARRAY['openid', 'profile', 'email'],
            allowed_domains TEXT[],
            claim_mappings JSONB,
            auto_provision_users BOOLEAN NOT NULL DEFAULT TRUE,
            default_role INTEGER NOT NULL DEFAULT 10,
            status INTEGER NOT NULL DEFAULT 0,
            last_error TEXT,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            last_used_at TIMESTAMPTZ
        );

        CREATE INDEX IF NOT EXISTS idx_sso_configs_org ON morphdb._morph_sso_configurations (organization_id);

        -- System table: _morph_backups (Phase 23)
        CREATE TABLE IF NOT EXISTS morphdb._morph_backups (
            backup_id UUID PRIMARY KEY,
            project_id UUID NOT NULL REFERENCES morphdb._morph_projects(project_id) ON DELETE CASCADE,
            name VARCHAR(255) NOT NULL,
            description TEXT,
            backup_type INTEGER NOT NULL DEFAULT 0,
            status INTEGER NOT NULL DEFAULT 0,
            size_bytes BIGINT NOT NULL DEFAULT 0,
            storage_path TEXT,
            storage_type INTEGER NOT NULL DEFAULT 0,
            compression INTEGER NOT NULL DEFAULT 1,
            checksum VARCHAR(128),
            error_message TEXT,
            initiated_by VARCHAR(255),
            metadata JSONB,
            started_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            completed_at TIMESTAMPTZ,
            expires_at TIMESTAMPTZ
        );

        CREATE INDEX IF NOT EXISTS idx_backups_project ON morphdb._morph_backups (project_id);

        -- System table: _morph_api_keys (Phase 11: Security)
        CREATE TABLE IF NOT EXISTS morphdb._morph_api_keys (
            key_id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
            tenant_id UUID NOT NULL,
            key_type INTEGER NOT NULL DEFAULT 0,
            key_hash VARCHAR(255) NOT NULL UNIQUE,
            key_prefix VARCHAR(8) NOT NULL,
            name VARCHAR(255) NOT NULL,
            description TEXT,
            scopes TEXT[],
            allowed_ips TEXT[],
            rate_limit INTEGER,
            expires_at TIMESTAMPTZ,
            is_active BOOLEAN NOT NULL DEFAULT true,
            last_used_at TIMESTAMPTZ,
            usage_count BIGINT NOT NULL DEFAULT 0,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );

        CREATE INDEX IF NOT EXISTS idx_api_keys_tenant ON morphdb._morph_api_keys(tenant_id);
        CREATE INDEX IF NOT EXISTS idx_api_keys_hash ON morphdb._morph_api_keys(key_hash);
        CREATE INDEX IF NOT EXISTS idx_api_keys_prefix ON morphdb._morph_api_keys(key_prefix);

        -- System table: _morph_views (Phase 10: Views)
        CREATE TABLE IF NOT EXISTS morphdb._morph_views (
            view_id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
            tenant_id UUID NOT NULL,
            logical_name VARCHAR(255) NOT NULL,
            physical_name VARCHAR(63) NOT NULL UNIQUE,
            definition JSONB NOT NULL,
            is_materialized BOOLEAN NOT NULL DEFAULT false,
            refresh_policy VARCHAR(20) NOT NULL DEFAULT 'OnDemand',
            refresh_schedule TEXT,
            last_refreshed_at TIMESTAMPTZ,
            is_stale BOOLEAN NOT NULL DEFAULT false,
            descriptor JSONB,
            is_active BOOLEAN NOT NULL DEFAULT true,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            UNIQUE (tenant_id, logical_name)
        );

        -- System table: _morph_view_columns (Phase 10: Views)
        CREATE TABLE IF NOT EXISTS morphdb._morph_view_columns (
            column_id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
            view_id UUID NOT NULL REFERENCES morphdb._morph_views(view_id) ON DELETE CASCADE,
            logical_name VARCHAR(255) NOT NULL,
            data_type VARCHAR(50) NOT NULL,
            is_computed BOOLEAN NOT NULL DEFAULT false,
            expression TEXT,
            ordinal_position INTEGER NOT NULL
        );

        CREATE INDEX IF NOT EXISTS idx_morph_views_tenant ON morphdb._morph_views(tenant_id);
        CREATE INDEX IF NOT EXISTS idx_morph_view_columns_view ON morphdb._morph_view_columns(view_id);
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
