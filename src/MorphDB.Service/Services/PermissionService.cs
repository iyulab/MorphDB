using System.Collections.Concurrent;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Models;

namespace MorphDB.Service.Services;

/// <summary>
/// LoggerMessage delegates for PermissionService.
/// </summary>
internal static partial class PermissionServiceLogs
{
    [LoggerMessage(LogLevel.Debug, "Checking {Permission} for user {UserId} on organization {OrganizationId}")]
    public static partial void CheckingOrgPermission(ILogger logger, string permission, string userId, Guid organizationId);

    [LoggerMessage(LogLevel.Debug, "Checking {Permission} for user {UserId} on project {ProjectId}")]
    public static partial void CheckingProjectPermission(ILogger logger, string permission, string userId, Guid projectId);

    [LoggerMessage(LogLevel.Debug, "Permission {Permission} granted via role {Role} (inherited: {IsInherited})")]
    public static partial void PermissionGranted(ILogger logger, string permission, string role, bool isInherited);

    [LoggerMessage(LogLevel.Debug, "Permission {Permission} denied for user {UserId}")]
    public static partial void PermissionDenied(ILogger logger, string permission, string userId);
}

/// <summary>
/// Service for checking and managing user permissions.
/// Uses in-memory caching for performance.
/// </summary>
public sealed class PermissionService : IPermissionService
{
    private readonly IMembershipRepository _membershipRepository;
    private readonly IProjectRepository _projectRepository;
    private readonly ILogger<PermissionService> _logger;
    private readonly ConcurrentDictionary<string, CachedPermissions> _cache = new();
    private static readonly TimeSpan CacheExpiry = TimeSpan.FromMinutes(5);

    public PermissionService(
        IMembershipRepository membershipRepository,
        IProjectRepository projectRepository,
        ILogger<PermissionService> logger)
    {
        _membershipRepository = membershipRepository;
        _projectRepository = projectRepository;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<bool> HasOrganizationPermissionAsync(
        string userId,
        Guid organizationId,
        string permission,
        CancellationToken cancellationToken = default)
    {
        PermissionServiceLogs.CheckingOrgPermission(_logger, permission, userId, organizationId);

        var permissions = await GetOrganizationPermissionsAsync(userId, organizationId, cancellationToken);

        if (permissions.HasPermission(permission))
        {
            PermissionServiceLogs.PermissionGranted(_logger, permission, permissions.Role ?? "unknown", permissions.IsInherited);
            return true;
        }

        PermissionServiceLogs.PermissionDenied(_logger, permission, userId);
        return false;
    }

    /// <inheritdoc/>
    public async Task<bool> HasProjectPermissionAsync(
        string userId,
        Guid projectId,
        string permission,
        CancellationToken cancellationToken = default)
    {
        PermissionServiceLogs.CheckingProjectPermission(_logger, permission, userId, projectId);

        var permissions = await GetProjectPermissionsAsync(userId, projectId, cancellationToken);

        if (permissions.HasPermission(permission))
        {
            PermissionServiceLogs.PermissionGranted(_logger, permission, permissions.Role ?? "unknown", permissions.IsInherited);
            return true;
        }

        PermissionServiceLogs.PermissionDenied(_logger, permission, userId);
        return false;
    }

    /// <inheritdoc/>
    public async Task<EffectivePermissions> GetOrganizationPermissionsAsync(
        string userId,
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = $"org:{organizationId}:{userId}";

        if (TryGetFromCache(cacheKey, out var cached))
        {
            return cached;
        }

        var member = await _membershipRepository.GetOrganizationMemberByUserIdAsync(
            organizationId, userId, cancellationToken);

        EffectivePermissions result;

        if (member is null || member.Status != MembershipStatus.Active)
        {
            result = new EffectivePermissions
            {
                UserId = userId,
                ResourceType = "organization",
                ResourceId = organizationId,
                Permissions = new HashSet<string>()
            };
        }
        else
        {
            var permissions = RolePermissions.GetOrganizationPermissions(member.Role);
            result = new EffectivePermissions
            {
                UserId = userId,
                ResourceType = "organization",
                ResourceId = organizationId,
                Role = member.Role.ToString(),
                IsInherited = false,
                Permissions = permissions
            };
        }

        AddToCache(cacheKey, result);
        return result;
    }

    /// <inheritdoc/>
    public async Task<EffectivePermissions> GetProjectPermissionsAsync(
        string userId,
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = $"proj:{projectId}:{userId}";

        if (TryGetFromCache(cacheKey, out var cached))
        {
            return cached;
        }

        // First check direct project membership
        var projectMember = await _membershipRepository.GetProjectMemberByUserIdAsync(
            projectId, userId, cancellationToken);

        if (projectMember is not null && projectMember.Status == MembershipStatus.Active)
        {
            var permissions = RolePermissions.GetProjectPermissions(projectMember.Role);
            var result = new EffectivePermissions
            {
                UserId = userId,
                ResourceType = "project",
                ResourceId = projectId,
                Role = projectMember.Role.ToString(),
                IsInherited = false,
                Permissions = permissions
            };
            AddToCache(cacheKey, result);
            return result;
        }

        // Check inherited permissions from organization
        var project = await _projectRepository.GetByIdAsync(projectId, cancellationToken);

        if (project?.OrganizationId is not null)
        {
            var orgMember = await _membershipRepository.GetOrganizationMemberByUserIdAsync(
                project.OrganizationId.Value, userId, cancellationToken);

            if (orgMember is not null && orgMember.Status == MembershipStatus.Active)
            {
                var inheritedRole = RolePermissions.GetInheritedProjectRole(orgMember.Role);

                if (inheritedRole.HasValue)
                {
                    var permissions = RolePermissions.GetProjectPermissions(inheritedRole.Value);
                    var result = new EffectivePermissions
                    {
                        UserId = userId,
                        ResourceType = "project",
                        ResourceId = projectId,
                        Role = inheritedRole.Value.ToString(),
                        IsInherited = true,
                        Permissions = permissions
                    };
                    AddToCache(cacheKey, result);
                    return result;
                }
            }
        }

        // No permissions
        var noPermissions = new EffectivePermissions
        {
            UserId = userId,
            ResourceType = "project",
            ResourceId = projectId,
            Permissions = new HashSet<string>()
        };
        AddToCache(cacheKey, noPermissions);
        return noPermissions;
    }

    /// <inheritdoc/>
    public async Task<OrganizationRole?> GetOrganizationRoleAsync(
        string userId,
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        var member = await _membershipRepository.GetOrganizationMemberByUserIdAsync(
            organizationId, userId, cancellationToken);

        return member?.Status == MembershipStatus.Active ? member.Role : null;
    }

    /// <inheritdoc/>
    public async Task<ProjectRole?> GetProjectRoleAsync(
        string userId,
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        // Check direct membership first
        var projectMember = await _membershipRepository.GetProjectMemberByUserIdAsync(
            projectId, userId, cancellationToken);

        if (projectMember is not null && projectMember.Status == MembershipStatus.Active)
        {
            return projectMember.Role;
        }

        // Check inherited role
        var project = await _projectRepository.GetByIdAsync(projectId, cancellationToken);

        if (project?.OrganizationId is not null)
        {
            var orgMember = await _membershipRepository.GetOrganizationMemberByUserIdAsync(
                project.OrganizationId.Value, userId, cancellationToken);

            if (orgMember is not null && orgMember.Status == MembershipStatus.Active)
            {
                return RolePermissions.GetInheritedProjectRole(orgMember.Role);
            }
        }

        return null;
    }

    /// <inheritdoc/>
    public async Task<bool> CanAccessProjectAsync(
        string userId,
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var role = await GetProjectRoleAsync(userId, projectId, cancellationToken);
        return role.HasValue;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Guid>> GetAccessibleProjectsAsync(
        string userId,
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        var accessibleProjects = new HashSet<Guid>();

        // Check organization role for inherited access
        var orgMember = await _membershipRepository.GetOrganizationMemberByUserIdAsync(
            organizationId, userId, cancellationToken);

        if (orgMember is not null &&
            orgMember.Status == MembershipStatus.Active &&
            RolePermissions.GetInheritedProjectRole(orgMember.Role).HasValue)
        {
            // Org owners/admins have access to all projects
            var projects = await _projectRepository.ListAsync(organizationId, cancellationToken: cancellationToken);
            foreach (var project in projects)
            {
                accessibleProjects.Add(project.ProjectId);
            }
        }

        // Add explicitly assigned projects
        var projectMemberships = await _membershipRepository.ListUserProjectsAsync(
            userId, MembershipStatus.Active, cancellationToken);

        foreach (var membership in projectMemberships)
        {
            accessibleProjects.Add(membership.ProjectId);
        }

        return accessibleProjects.ToList();
    }

    #region Cache Helpers

    private bool TryGetFromCache(string key, out EffectivePermissions permissions)
    {
        if (_cache.TryGetValue(key, out var cached) && cached.ExpiresAt > DateTimeOffset.UtcNow)
        {
            permissions = cached.Permissions;
            return true;
        }

        permissions = null!;
        return false;
    }

    private void AddToCache(string key, EffectivePermissions permissions)
    {
        _cache[key] = new CachedPermissions(permissions, DateTimeOffset.UtcNow.Add(CacheExpiry));

        // Simple cache cleanup (remove expired entries periodically)
        if (_cache.Count > 1000)
        {
            var expiredKeys = _cache
                .Where(kvp => kvp.Value.ExpiresAt <= DateTimeOffset.UtcNow)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var expiredKey in expiredKeys)
            {
                _cache.TryRemove(expiredKey, out _);
            }
        }
    }

    /// <summary>
    /// Invalidates cached permissions for a user.
    /// Call this when membership changes.
    /// </summary>
    public void InvalidateUserCache(string userId)
    {
        var keysToRemove = _cache.Keys.Where(k => k.EndsWith($":{userId}", StringComparison.Ordinal)).ToList();
        foreach (var key in keysToRemove)
        {
            _cache.TryRemove(key, out _);
        }
    }

    /// <summary>
    /// Invalidates all cached permissions for a resource.
    /// Call this when resource permissions change.
    /// </summary>
    public void InvalidateResourceCache(string resourceType, Guid resourceId)
    {
        var prefix = $"{resourceType}:{resourceId}:";
        var keysToRemove = _cache.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList();
        foreach (var key in keysToRemove)
        {
            _cache.TryRemove(key, out _);
        }
    }

    private sealed record CachedPermissions(EffectivePermissions Permissions, DateTimeOffset ExpiresAt);

    #endregion
}
