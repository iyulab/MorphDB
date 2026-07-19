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
    /// Builds DDL that removes the SaaS control-plane remnants from a pre-0.7 database.
    /// Safe on a brand-new database: every statement is guarded.
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
        """;
}
