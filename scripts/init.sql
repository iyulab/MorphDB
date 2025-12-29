-- MorphDB Initial Database Setup
-- This script runs when the PostgreSQL container is first created

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

-- System table: _morph_api_keys
CREATE TABLE IF NOT EXISTS morphdb._morph_api_keys (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id UUID NOT NULL,
    key_type INTEGER NOT NULL DEFAULT 0,
    key_hash VARCHAR(255) NOT NULL,
    key_prefix VARCHAR(50) NOT NULL,
    name VARCHAR(100) NOT NULL,
    description TEXT,
    is_active BOOLEAN NOT NULL DEFAULT true,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    expires_at TIMESTAMPTZ,
    last_used_at TIMESTAMPTZ,
    UNIQUE (key_hash)
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

-- System table: _morph_webhook_dlq (Dead Letter Queue)
CREATE TABLE IF NOT EXISTS morphdb._morph_webhook_dlq (
    dlq_id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    delivery_id UUID NOT NULL,
    webhook_id UUID NOT NULL REFERENCES morphdb._morph_webhooks(webhook_id) ON DELETE CASCADE,
    tenant_id UUID NOT NULL,
    record_id UUID,
    event VARCHAR(20) NOT NULL,
    payload JSONB NOT NULL,
    reason VARCHAR(50) NOT NULL,
    attempt_count INTEGER NOT NULL DEFAULT 0,
    last_http_status_code INTEGER,
    last_error_message TEXT,
    status VARCHAR(20) NOT NULL DEFAULT 'pending_review',
    resolution_notes TEXT,
    dlq_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    resolved_at TIMESTAMPTZ,
    resolved_by UUID
);

-- Index for DLQ queries
CREATE INDEX IF NOT EXISTS idx_morph_webhook_dlq_webhook_id ON morphdb._morph_webhook_dlq(webhook_id);
CREATE INDEX IF NOT EXISTS idx_morph_webhook_dlq_tenant_id ON morphdb._morph_webhook_dlq(tenant_id);
CREATE INDEX IF NOT EXISTS idx_morph_webhook_dlq_status ON morphdb._morph_webhook_dlq(status);
CREATE INDEX IF NOT EXISTS idx_morph_webhook_dlq_dlq_at ON morphdb._morph_webhook_dlq(dlq_at);

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

-- System table: _morph_export_data (temporary storage for export data)
CREATE TABLE IF NOT EXISTS morphdb._morph_export_data (
    job_id UUID PRIMARY KEY REFERENCES morphdb._morph_export_jobs(job_id) ON DELETE CASCADE,
    data BYTEA NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- ============================================================================
-- Phase 17: Schema-based Layer Separation - Global Control Plane Tables
-- ============================================================================

-- System table: _morph_organizations (for future hierarchical multi-tenancy)
CREATE TABLE IF NOT EXISTS morphdb._morph_organizations (
    org_id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    name VARCHAR(100) NOT NULL,
    slug VARCHAR(100) NOT NULL UNIQUE,
    owner_id UUID,
    settings JSONB,
    is_active BOOLEAN NOT NULL DEFAULT true,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- System table: _morph_projects (central project registry)
CREATE TABLE IF NOT EXISTS morphdb._morph_projects (
    project_id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
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

-- Create indexes for projects
CREATE INDEX IF NOT EXISTS idx_morph_projects_org ON morphdb._morph_projects(org_id);
CREATE INDEX IF NOT EXISTS idx_morph_projects_status ON morphdb._morph_projects(status);
CREATE INDEX IF NOT EXISTS idx_morph_projects_slug ON morphdb._morph_projects(slug);
CREATE INDEX IF NOT EXISTS idx_morph_organizations_slug ON morphdb._morph_organizations(slug);

-- ============================================================================
-- Legacy Tables (for backward compatibility with tenant-based approach)
-- ============================================================================

-- Create indexes for system tables
CREATE INDEX IF NOT EXISTS idx_morph_tables_tenant ON morphdb._morph_tables(tenant_id);
CREATE INDEX IF NOT EXISTS idx_morph_columns_table ON morphdb._morph_columns(table_id);
CREATE INDEX IF NOT EXISTS idx_morph_relations_source ON morphdb._morph_relations(source_table_id);
CREATE INDEX IF NOT EXISTS idx_morph_relations_target ON morphdb._morph_relations(target_table_id);
CREATE INDEX IF NOT EXISTS idx_morph_changelog_table ON morphdb._morph_changelog(table_id);
CREATE INDEX IF NOT EXISTS idx_morph_api_keys_tenant ON morphdb._morph_api_keys(tenant_id);
CREATE INDEX IF NOT EXISTS idx_morph_api_keys_prefix ON morphdb._morph_api_keys(key_prefix);
CREATE INDEX IF NOT EXISTS idx_morph_webhooks_tenant ON morphdb._morph_webhooks(tenant_id);
CREATE INDEX IF NOT EXISTS idx_morph_webhooks_table ON morphdb._morph_webhooks(table_id);
CREATE INDEX IF NOT EXISTS idx_morph_webhook_deliveries_webhook ON morphdb._morph_webhook_deliveries(webhook_id);
CREATE INDEX IF NOT EXISTS idx_morph_webhook_deliveries_status ON morphdb._morph_webhook_deliveries(status) WHERE status IN ('pending', 'retrying');
CREATE INDEX IF NOT EXISTS idx_morph_webhook_deliveries_next_retry ON morphdb._morph_webhook_deliveries(next_retry_at) WHERE next_retry_at IS NOT NULL;
CREATE INDEX IF NOT EXISTS idx_morph_import_jobs_tenant ON morphdb._morph_import_jobs(tenant_id);
CREATE INDEX IF NOT EXISTS idx_morph_import_jobs_status ON morphdb._morph_import_jobs(status) WHERE status IN ('pending', 'processing');
CREATE INDEX IF NOT EXISTS idx_morph_export_jobs_tenant ON morphdb._morph_export_jobs(tenant_id);
CREATE INDEX IF NOT EXISTS idx_morph_export_jobs_status ON morphdb._morph_export_jobs(status) WHERE status IN ('pending', 'processing');
CREATE INDEX IF NOT EXISTS idx_morph_export_jobs_expires ON morphdb._morph_export_jobs(expires_at) WHERE expires_at IS NOT NULL;

-- Create function for schema change notifications
CREATE OR REPLACE FUNCTION morphdb.notify_schema_change()
RETURNS TRIGGER AS $$
BEGIN
    PERFORM pg_notify('morphdb_schema', json_build_object(
        'table_id', COALESCE(NEW.table_id, OLD.table_id),
        'operation', TG_OP,
        'schema_version', COALESCE(NEW.schema_version, OLD.schema_version)
    )::text);
    RETURN COALESCE(NEW, OLD);
END;
$$ LANGUAGE plpgsql;

-- Create trigger for schema changes
DROP TRIGGER IF EXISTS trg_schema_change ON morphdb._morph_tables;
CREATE TRIGGER trg_schema_change
    AFTER INSERT OR UPDATE OR DELETE ON morphdb._morph_tables
    FOR EACH ROW
    EXECUTE FUNCTION morphdb.notify_schema_change();

-- Create function for auto-updating updated_at
CREATE OR REPLACE FUNCTION morphdb.update_updated_at()
RETURNS TRIGGER AS $$
BEGIN
    NEW.updated_at = NOW();
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

-- Create trigger for auto-updating updated_at
DROP TRIGGER IF EXISTS trg_update_timestamp ON morphdb._morph_tables;
CREATE TRIGGER trg_update_timestamp
    BEFORE UPDATE ON morphdb._morph_tables
    FOR EACH ROW
    EXECUTE FUNCTION morphdb.update_updated_at();

-- Grant permissions
GRANT USAGE ON SCHEMA morphdb TO morph;
GRANT ALL PRIVILEGES ON ALL TABLES IN SCHEMA morphdb TO morph;
GRANT ALL PRIVILEGES ON ALL SEQUENCES IN SCHEMA morphdb TO morph;

-- Output success message
DO $$
BEGIN
    RAISE NOTICE 'MorphDB schema initialized successfully';
END
$$;
