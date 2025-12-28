namespace MorphDB.Npgsql.Security;

/// <summary>
/// SQL schema definitions for security tables.
/// </summary>
public static class SecuritySchema
{
    /// <summary>
    /// Creates the security tables in the morphdb schema.
    /// </summary>
    public const string CreateSecurityTables = """
        -- API Keys table
        CREATE TABLE IF NOT EXISTS morphdb._morph_api_keys (
            id UUID PRIMARY KEY,
            tenant_id UUID NOT NULL,
            key_type INTEGER NOT NULL,
            key_hash VARCHAR(255) NOT NULL,
            key_prefix VARCHAR(32) NOT NULL,
            name VARCHAR(255) NOT NULL,
            description TEXT,
            is_active BOOLEAN NOT NULL DEFAULT true,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            expires_at TIMESTAMPTZ,
            last_used_at TIMESTAMPTZ
        );

        CREATE INDEX IF NOT EXISTS idx_api_keys_tenant_id ON morphdb._morph_api_keys (tenant_id);
        CREATE INDEX IF NOT EXISTS idx_api_keys_key_prefix ON morphdb._morph_api_keys (key_prefix) WHERE is_active = true;

        -- Security Policies (RLS) table
        CREATE TABLE IF NOT EXISTS morphdb._morph_security_policies (
            id UUID PRIMARY KEY,
            tenant_id UUID NOT NULL,
            table_id UUID NOT NULL REFERENCES morphdb._morph_tables(table_id),
            name VARCHAR(255) NOT NULL,
            description TEXT,
            policy_type INTEGER NOT NULL,
            expression TEXT NOT NULL,
            is_active BOOLEAN NOT NULL DEFAULT true,
            ordinal_position INTEGER NOT NULL DEFAULT 0,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            UNIQUE (tenant_id, table_id, name)
        );

        CREATE INDEX IF NOT EXISTS idx_security_policies_tenant_table ON morphdb._morph_security_policies (tenant_id, table_id) WHERE is_active = true;
        """;

    /// <summary>
    /// Drops the security tables.
    /// </summary>
    public const string DropSecurityTables = """
        DROP TABLE IF EXISTS morphdb._morph_security_policies;
        DROP TABLE IF EXISTS morphdb._morph_api_keys;
        """;
}
