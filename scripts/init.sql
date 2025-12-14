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
    key_id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id UUID NOT NULL,
    key_type VARCHAR(20) NOT NULL CHECK (key_type IN ('anon', 'service')),
    key_hash VARCHAR(64) NOT NULL,
    key_prefix VARCHAR(50) NOT NULL,
    name VARCHAR(100),
    is_active BOOLEAN NOT NULL DEFAULT true,
    expires_at TIMESTAMPTZ,
    last_used_at TIMESTAMPTZ,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    metadata JSONB,
    UNIQUE (key_hash)
);

-- Create indexes for system tables
CREATE INDEX IF NOT EXISTS idx_morph_tables_tenant ON morphdb._morph_tables(tenant_id);
CREATE INDEX IF NOT EXISTS idx_morph_columns_table ON morphdb._morph_columns(table_id);
CREATE INDEX IF NOT EXISTS idx_morph_relations_source ON morphdb._morph_relations(source_table_id);
CREATE INDEX IF NOT EXISTS idx_morph_relations_target ON morphdb._morph_relations(target_table_id);
CREATE INDEX IF NOT EXISTS idx_morph_changelog_table ON morphdb._morph_changelog(table_id);
CREATE INDEX IF NOT EXISTS idx_morph_api_keys_tenant ON morphdb._morph_api_keys(tenant_id);
CREATE INDEX IF NOT EXISTS idx_morph_api_keys_prefix ON morphdb._morph_api_keys(key_prefix);

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
