namespace MorphDB.Npgsql.Schema;

/// <summary>
/// Brings a control plane created by an older MorphDB up to the shape the canonical DDL expects.
///
/// The canonical DDL uses CREATE TABLE IF NOT EXISTS, so it cannot repair or remove anything that
/// already exists — a database that booted an older version would keep the old shape forever. These
/// statements run immediately before it, and must stay idempotent: the bootstrap runs on every start.
/// </summary>
public static class GlobalSchemaMigrations
{
    /// <summary>
    /// Builds DDL that brings a pre-0.7 database forward: it removes the SaaS control-plane remnants
    /// and renames the tenant columns and indexes to project.
    /// Safe on a brand-new database: every statement is guarded, and the rename loops select nothing.
    /// </summary>
    /// <remarks>
    /// Organizations, membership, invitations, SSO configuration and backups were removed because
    /// they assume what this database is used for, which a virtual-schema layer must not. Dropping
    /// them is safe in a way it normally would not be: their tables were never created by any
    /// released bootstrap, so nothing could have written rows through the shipped code path. The
    /// two that did exist — _morph_organizations and _morph_projects.org_id — held no usable data
    /// either, because the repository queried column names the bootstrap never created.
    /// </remarks>
    public static string BuildPreBootstrapDdl() => """
        -- Drop the project -> organization link before the table it points at.
        ALTER TABLE IF EXISTS morphdb._morph_projects DROP COLUMN IF EXISTS org_id;

        DROP TABLE IF EXISTS morphdb._morph_backups;
        DROP TABLE IF EXISTS morphdb._morph_sso_configurations;
        DROP TABLE IF EXISTS morphdb._morph_organization_invitations;
        DROP TABLE IF EXISTS morphdb._morph_project_members;
        DROP TABLE IF EXISTS morphdb._morph_organization_members;
        DROP TABLE IF EXISTS morphdb._morph_organizations;

        -- One concept had two names: the domain model, the repositories and the REST routes said
        -- project, while the header, the client option and the columns underneath said tenant. The
        -- name tenant promised an isolation boundary this layer explicitly does not stand -- a
        -- project is a schema namespace, not a customer. The columns are hidden layer, so renaming
        -- them is invisible to callers, but a database created before 0.7 still has the old name and
        -- Dapper maps columns to properties by convention: left alone, every read would return an
        -- empty id with no error raised.
        --
        -- The rename walks the control plane and every per-project system schema. It is idempotent
        -- by construction: a column already named project_id is not selected, so a second run is a
        -- no-op. RENAME COLUMN keeps the data and the indexes, so nothing is rewritten or lost.
        DO $$
        DECLARE
            target RECORD;
        BEGIN
            FOR target IN
                SELECT table_schema, table_name
                FROM information_schema.columns
                WHERE column_name = 'tenant_id'
                  AND (table_schema = 'morphdb' OR table_schema LIKE 'p\_%\_sys')
            LOOP
                EXECUTE format(
                    'ALTER TABLE %I.%I RENAME COLUMN tenant_id TO project_id',
                    target.table_schema, target.table_name);
            END LOOP;
        END $$;

        -- The indexes over those columns carry the old word in their names. The canonical DDL creates
        -- the new names with IF NOT EXISTS, so leaving these would quietly double every one of them:
        -- the old index still exists under its old name and a second identical index gets built
        -- beside it. Renaming keeps the index as it is -- nothing is rebuilt.
        DO $$
        DECLARE
            target RECORD;
        BEGIN
            FOR target IN
                SELECT schemaname, indexname
                FROM pg_indexes
                WHERE indexname LIKE '%tenant%'
                  AND (schemaname = 'morphdb' OR schemaname LIKE 'p\_%\_sys')
            LOOP
                EXECUTE format(
                    'ALTER INDEX %I.%I RENAME TO %I',
                    target.schemaname, target.indexname,
                    replace(target.indexname, 'tenant', 'project'));
            END LOOP;
        END $$;
        """;
}
