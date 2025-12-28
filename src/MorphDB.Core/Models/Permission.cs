namespace MorphDB.Core.Models;

/// <summary>
/// Defines all permissions in the system.
/// Permissions are grouped by resource type.
/// </summary>
public static class Permissions
{
    /// <summary>
    /// Organization-level permissions.
    /// </summary>
    public static class Organization
    {
        public const string View = "org:view";
        public const string Update = "org:update";
        public const string Delete = "org:delete";
        public const string ManageMembers = "org:manage_members";
        public const string ManageRoles = "org:manage_roles";
        public const string ManageSso = "org:manage_sso";
        public const string ManageBilling = "org:manage_billing";
        public const string CreateProject = "org:create_project";
    }

    /// <summary>
    /// Project-level permissions.
    /// </summary>
    public static class Project
    {
        public const string View = "project:view";
        public const string Update = "project:update";
        public const string Delete = "project:delete";
        public const string ManageMembers = "project:manage_members";
        public const string ManageSchema = "project:manage_schema";
        public const string ManageWebhooks = "project:manage_webhooks";
        public const string ManageApiKeys = "project:manage_api_keys";
        public const string ViewAuditLogs = "project:view_audit_logs";
    }

    /// <summary>
    /// Data-level permissions.
    /// </summary>
    public static class Data
    {
        public const string Read = "data:read";
        public const string Create = "data:create";
        public const string Update = "data:update";
        public const string Delete = "data:delete";
        public const string BulkImport = "data:bulk_import";
        public const string BulkExport = "data:bulk_export";
    }

    /// <summary>
    /// Schema-level permissions.
    /// </summary>
    public static class Schema
    {
        public const string View = "schema:view";
        public const string CreateTable = "schema:create_table";
        public const string AlterTable = "schema:alter_table";
        public const string DropTable = "schema:drop_table";
        public const string ManageIndexes = "schema:manage_indexes";
        public const string ManageRelations = "schema:manage_relations";
    }
}

/// <summary>
/// Maps roles to their granted permissions.
/// </summary>
public static class RolePermissions
{
    /// <summary>
    /// Gets permissions for an organization role.
    /// </summary>
    public static IReadOnlySet<string> GetOrganizationPermissions(OrganizationRole role)
    {
        return role switch
        {
            OrganizationRole.Owner => OwnerPermissions,
            OrganizationRole.Admin => AdminPermissions,
            OrganizationRole.Member => MemberPermissions,
            _ => EmptyPermissions
        };
    }

    /// <summary>
    /// Gets permissions for a project role.
    /// </summary>
    public static IReadOnlySet<string> GetProjectPermissions(ProjectRole role)
    {
        return role switch
        {
            ProjectRole.Admin => ProjectAdminPermissions,
            ProjectRole.Developer => ProjectDeveloperPermissions,
            ProjectRole.Viewer => ProjectViewerPermissions,
            _ => EmptyPermissions
        };
    }

    /// <summary>
    /// Gets the inherited project role from an organization role.
    /// </summary>
    public static ProjectRole? GetInheritedProjectRole(OrganizationRole orgRole)
    {
        return orgRole switch
        {
            OrganizationRole.Owner => ProjectRole.Admin,
            OrganizationRole.Admin => ProjectRole.Developer,
            _ => null // Members need explicit project assignment
        };
    }

    private static readonly HashSet<string> EmptyPermissions = [];

    private static readonly HashSet<string> OwnerPermissions =
    [
        // All organization permissions
        Permissions.Organization.View,
        Permissions.Organization.Update,
        Permissions.Organization.Delete,
        Permissions.Organization.ManageMembers,
        Permissions.Organization.ManageRoles,
        Permissions.Organization.ManageSso,
        Permissions.Organization.ManageBilling,
        Permissions.Organization.CreateProject
    ];

    private static readonly HashSet<string> AdminPermissions =
    [
        Permissions.Organization.View,
        Permissions.Organization.Update,
        Permissions.Organization.ManageMembers,
        Permissions.Organization.CreateProject
    ];

    private static readonly HashSet<string> MemberPermissions =
    [
        Permissions.Organization.View
    ];

    private static readonly HashSet<string> ProjectAdminPermissions =
    [
        // All project permissions
        Permissions.Project.View,
        Permissions.Project.Update,
        Permissions.Project.Delete,
        Permissions.Project.ManageMembers,
        Permissions.Project.ManageSchema,
        Permissions.Project.ManageWebhooks,
        Permissions.Project.ManageApiKeys,
        Permissions.Project.ViewAuditLogs,
        // All data permissions
        Permissions.Data.Read,
        Permissions.Data.Create,
        Permissions.Data.Update,
        Permissions.Data.Delete,
        Permissions.Data.BulkImport,
        Permissions.Data.BulkExport,
        // All schema permissions
        Permissions.Schema.View,
        Permissions.Schema.CreateTable,
        Permissions.Schema.AlterTable,
        Permissions.Schema.DropTable,
        Permissions.Schema.ManageIndexes,
        Permissions.Schema.ManageRelations
    ];

    private static readonly HashSet<string> ProjectDeveloperPermissions =
    [
        Permissions.Project.View,
        Permissions.Project.ManageSchema,
        Permissions.Project.ViewAuditLogs,
        // All data permissions
        Permissions.Data.Read,
        Permissions.Data.Create,
        Permissions.Data.Update,
        Permissions.Data.Delete,
        Permissions.Data.BulkImport,
        Permissions.Data.BulkExport,
        // Schema permissions
        Permissions.Schema.View,
        Permissions.Schema.CreateTable,
        Permissions.Schema.AlterTable,
        Permissions.Schema.ManageIndexes,
        Permissions.Schema.ManageRelations
    ];

    private static readonly HashSet<string> ProjectViewerPermissions =
    [
        Permissions.Project.View,
        Permissions.Data.Read,
        Permissions.Data.BulkExport,
        Permissions.Schema.View
    ];
}

/// <summary>
/// Represents a user's effective permissions for a resource.
/// </summary>
public sealed class EffectivePermissions
{
    /// <summary>
    /// The user ID.
    /// </summary>
    public required string UserId { get; init; }

    /// <summary>
    /// The resource type (organization, project).
    /// </summary>
    public required string ResourceType { get; init; }

    /// <summary>
    /// The resource ID.
    /// </summary>
    public Guid ResourceId { get; init; }

    /// <summary>
    /// The role that grants these permissions (if any).
    /// </summary>
    public string? Role { get; init; }

    /// <summary>
    /// Whether the role is inherited from parent (e.g., org → project).
    /// </summary>
    public bool IsInherited { get; init; }

    /// <summary>
    /// Set of granted permissions.
    /// </summary>
    public required IReadOnlySet<string> Permissions { get; init; }

    /// <summary>
    /// Checks if a specific permission is granted.
    /// </summary>
    public bool HasPermission(string permission) => Permissions.Contains(permission);
}
