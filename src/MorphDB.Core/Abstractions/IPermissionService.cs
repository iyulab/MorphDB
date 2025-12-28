using MorphDB.Core.Models;

namespace MorphDB.Core.Abstractions;

/// <summary>
/// Service for checking and managing user permissions.
/// </summary>
public interface IPermissionService
{
    /// <summary>
    /// Checks if a user has a specific permission on an organization.
    /// </summary>
    Task<bool> HasOrganizationPermissionAsync(
        string userId,
        Guid organizationId,
        string permission,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a user has a specific permission on a project.
    /// This includes inherited permissions from the organization.
    /// </summary>
    Task<bool> HasProjectPermissionAsync(
        string userId,
        Guid projectId,
        string permission,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all effective permissions for a user on an organization.
    /// </summary>
    Task<EffectivePermissions> GetOrganizationPermissionsAsync(
        string userId,
        Guid organizationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all effective permissions for a user on a project.
    /// Includes inherited permissions from the organization.
    /// </summary>
    Task<EffectivePermissions> GetProjectPermissionsAsync(
        string userId,
        Guid projectId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the user's role in an organization.
    /// </summary>
    Task<OrganizationRole?> GetOrganizationRoleAsync(
        string userId,
        Guid organizationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the user's effective role in a project.
    /// This considers both direct assignment and inherited roles.
    /// </summary>
    Task<ProjectRole?> GetProjectRoleAsync(
        string userId,
        Guid projectId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a user can access a project (has any role).
    /// </summary>
    Task<bool> CanAccessProjectAsync(
        string userId,
        Guid projectId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all projects a user can access within an organization.
    /// </summary>
    Task<IReadOnlyList<Guid>> GetAccessibleProjectsAsync(
        string userId,
        Guid organizationId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Authorization result with details.
/// </summary>
public sealed class AuthorizationResult
{
    /// <summary>
    /// Whether access is allowed.
    /// </summary>
    public bool IsAllowed { get; init; }

    /// <summary>
    /// The permission that was checked.
    /// </summary>
    public required string Permission { get; init; }

    /// <summary>
    /// The role that grants this permission (if allowed).
    /// </summary>
    public string? GrantedByRole { get; init; }

    /// <summary>
    /// Whether the permission is inherited from a parent resource.
    /// </summary>
    public bool IsInherited { get; init; }

    /// <summary>
    /// Reason for denial (if not allowed).
    /// </summary>
    public string? DenialReason { get; init; }

    public static AuthorizationResult Allowed(string permission, string role, bool inherited = false)
        => new() { IsAllowed = true, Permission = permission, GrantedByRole = role, IsInherited = inherited };

    public static AuthorizationResult Denied(string permission, string reason)
        => new() { IsAllowed = false, Permission = permission, DenialReason = reason };
}
