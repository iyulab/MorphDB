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

        -- API keys went with the authentication machinery: the service never required
        -- authentication, and a production image had no way to mint a key in the first place, so
        -- the rows a Development-mode database may hold authorize nothing and identify nobody.
        --
        -- This name is retired and must not be reused. The statement below runs on every start,
        -- before the canonical CREATE TABLE IF NOT EXISTS -- so anything given this name would be
        -- dropped and recreated empty at each boot, silently and without an error. Connection
        -- secrets live in _morph_secrets for exactly this reason.
        DROP TABLE IF EXISTS morphdb._morph_api_keys;

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

        -- Uniqueness is about to be expressed as an index over is_active, and CREATE TABLE IF NOT
        -- EXISTS adds no column to a table that already exists. An older control plane missing the
        -- flag would therefore fail the CREATE INDEX on every start -- a boot crash loop, not a
        -- degraded feature. Whatever the column already is, this leaves it alone.
        -- Virtual-constraint flags on relations. They existed on the model from the start and the
        -- write pipeline already read EnforceOnWrite, but there were no columns to hold them: every
        -- relation loaded from the database came back with the model's default of true, whatever
        -- was asked for. The option looked settable and was not. Defaulting the columns to true
        -- keeps every existing relation behaving exactly as it did.
        ALTER TABLE IF EXISTS morphdb._morph_relations ADD COLUMN IF NOT EXISTS enforce_on_write BOOLEAN NOT NULL DEFAULT true;
        ALTER TABLE IF EXISTS morphdb._morph_relations ADD COLUMN IF NOT EXISTS virtual_cascade BOOLEAN NOT NULL DEFAULT true;

        -- Per-row import failures were computed (WritePipeline returns the real error per row) and
        -- then discarded: the streaming loop only tallied error_count, and this table had no column
        -- to hold the reasons. A caller with errorCount > 0 and errorMessage = NULL (the job only
        -- ever failed as a whole, in the catch block) had no way to ask "which rows, and why" short
        -- of reading the source. NULL on every existing row is correct: no historical job has
        -- reasons to backfill, because the reasons were never kept anywhere.
        ALTER TABLE IF EXISTS morphdb._morph_import_jobs ADD COLUMN IF NOT EXISTS error_details JSONB;

        ALTER TABLE IF EXISTS morphdb._morph_tables ADD COLUMN IF NOT EXISTS is_active BOOLEAN NOT NULL DEFAULT true;
        ALTER TABLE IF EXISTS morphdb._morph_columns ADD COLUMN IF NOT EXISTS is_active BOOLEAN NOT NULL DEFAULT true;
        ALTER TABLE IF EXISTS morphdb._morph_indexes ADD COLUMN IF NOT EXISTS is_active BOOLEAN NOT NULL DEFAULT true;
        ALTER TABLE IF EXISTS morphdb._morph_views ADD COLUMN IF NOT EXISTS is_active BOOLEAN NOT NULL DEFAULT true;
        ALTER TABLE IF EXISTS morphdb._morph_security_policies ADD COLUMN IF NOT EXISTS is_active BOOLEAN NOT NULL DEFAULT true;

        -- The soft-deleted control-plane tables carried table-level UNIQUE constraints, which count
        -- tombstones as occupants of the name they hold. A database created before this ran cannot
        -- recreate any logical name it ever deleted -- the constraint rejects the INSERT with 23505
        -- and no amount of retrying helps. The canonical DDL now expresses the same uniqueness as a
        -- partial index over is_active = true, but CREATE TABLE IF NOT EXISTS cannot remove what the
        -- old shape already built, so the constraints are dropped here first.
        --
        -- Scoped to the five soft-deleted tables by name on purpose. _morph_projects (slug,
        -- schemas) is unique for reasons unrelated to liveness, so its constraints are left
        -- standing. Idempotent: a second run selects nothing, because the constraints are gone.
        DO $$
        DECLARE
            target RECORD;
        BEGIN
            FOR target IN
                SELECT rel.relname AS table_name, con.conname AS constraint_name
                FROM pg_constraint con
                JOIN pg_class rel ON rel.oid = con.conrelid
                JOIN pg_namespace nsp ON nsp.oid = rel.relnamespace
                WHERE nsp.nspname = 'morphdb'
                  AND con.contype = 'u'
                  AND rel.relname IN (
                      '_morph_tables',
                      '_morph_columns',
                      '_morph_indexes',
                      '_morph_views',
                      '_morph_security_policies')
            LOOP
                EXECUTE format(
                    'ALTER TABLE morphdb.%I DROP CONSTRAINT %I',
                    target.table_name, target.constraint_name);
            END LOOP;
        END $$;
        """;
}
