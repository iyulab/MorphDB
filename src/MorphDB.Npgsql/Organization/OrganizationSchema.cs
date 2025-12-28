namespace MorphDB.Npgsql.Organization;

/// <summary>
/// SQL schema definitions for organization and membership tables.
/// </summary>
public static class OrganizationSchema
{
    /// <summary>
    /// Creates the organization and membership tables in the morphdb schema.
    /// </summary>
    public const string CreateOrganizationTables = """
        -- Organizations table
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

        CREATE INDEX IF NOT EXISTS idx_organizations_slug ON morphdb._morph_organizations (slug);
        CREATE INDEX IF NOT EXISTS idx_organizations_status ON morphdb._morph_organizations (status) WHERE status != 3;

        -- Organization members table
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
        CREATE INDEX IF NOT EXISTS idx_org_members_active ON morphdb._morph_organization_members (organization_id, status) WHERE status = 1;

        -- Project members table
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
        CREATE INDEX IF NOT EXISTS idx_proj_members_active ON morphdb._morph_project_members (project_id, status) WHERE status = 1;

        -- Organization invitations table
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
        CREATE INDEX IF NOT EXISTS idx_org_invitations_email ON morphdb._morph_organization_invitations (email);
        """;

    /// <summary>
    /// Drops the organization and membership tables.
    /// </summary>
    public const string DropOrganizationTables = """
        DROP TABLE IF EXISTS morphdb._morph_organization_invitations;
        DROP TABLE IF EXISTS morphdb._morph_project_members;
        DROP TABLE IF EXISTS morphdb._morph_organization_members;
        DROP TABLE IF EXISTS morphdb._morph_organizations;
        """;
}
