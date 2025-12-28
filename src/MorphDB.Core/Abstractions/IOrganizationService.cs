using MorphDB.Core.Models;

namespace MorphDB.Core.Abstractions;

/// <summary>
/// High-level service for organization lifecycle management.
/// </summary>
public interface IOrganizationService
{
    /// <summary>
    /// Creates a new organization with the creator as owner.
    /// </summary>
    Task<Organization> CreateOrganizationAsync(
        CreateOrganizationRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets an organization by its ID.
    /// </summary>
    Task<Organization?> GetOrganizationAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets an organization by its slug.
    /// </summary>
    Task<Organization?> GetOrganizationBySlugAsync(
        string slug,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists organizations the user has access to.
    /// </summary>
    Task<IReadOnlyList<Organization>> ListUserOrganizationsAsync(
        string userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an organization's settings.
    /// </summary>
    Task<Organization> UpdateOrganizationAsync(
        UpdateOrganizationRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Suspends an organization (disables all access).
    /// </summary>
    Task SuspendOrganizationAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reactivates a suspended organization.
    /// </summary>
    Task ReactivateOrganizationAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes an organization and all its projects.
    /// </summary>
    Task DeleteOrganizationAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets organization statistics.
    /// </summary>
    Task<OrganizationStats> GetOrganizationStatsAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Organization statistics.
/// </summary>
public sealed class OrganizationStats
{
    public Guid OrganizationId { get; init; }
    public int TotalProjects { get; init; }
    public int ActiveProjects { get; init; }
    public int TotalMembers { get; init; }
    public int ActiveMembers { get; init; }
    public int PendingInvitations { get; init; }
    public long TotalStorageBytes { get; init; }
    public DateTimeOffset? LastActivityAt { get; init; }
}
