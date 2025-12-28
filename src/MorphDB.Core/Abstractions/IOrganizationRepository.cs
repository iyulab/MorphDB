using MorphDB.Core.Models;

namespace MorphDB.Core.Abstractions;

/// <summary>
/// Repository for managing organization entities in the global control plane.
/// Organizations are stored in the morphdb schema.
/// </summary>
public interface IOrganizationRepository
{
    /// <summary>
    /// Creates a new organization record.
    /// </summary>
    Task<Organization> CreateAsync(
        CreateOrganizationRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets an organization by its ID.
    /// </summary>
    Task<Organization?> GetByIdAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets an organization by its slug.
    /// </summary>
    Task<Organization?> GetBySlugAsync(
        string slug,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all organizations with optional filters.
    /// </summary>
    Task<IReadOnlyList<Organization>> ListAsync(
        OrganizationStatus? status = null,
        int offset = 0,
        int limit = 100,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an organization's settings or status.
    /// </summary>
    Task<Organization> UpdateAsync(
        UpdateOrganizationRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the organization status.
    /// </summary>
    Task UpdateStatusAsync(
        Guid organizationId,
        OrganizationStatus status,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Soft-deletes an organization by setting status to Deleted.
    /// </summary>
    Task DeleteAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a slug is available.
    /// </summary>
    Task<bool> IsSlugAvailableAsync(
        string slug,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets total count of organizations, optionally filtered.
    /// </summary>
    Task<int> CountAsync(
        OrganizationStatus? status = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Request to create a new organization.
/// </summary>
public sealed record CreateOrganizationRequest
{
    /// <summary>
    /// Optional pre-defined organization ID. If null, a new GUID will be generated.
    /// </summary>
    public Guid? OrganizationId { get; init; }

    /// <summary>
    /// Human-readable organization name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// URL-safe unique identifier. If null, will be generated from name.
    /// </summary>
    public string? Slug { get; init; }

    /// <summary>
    /// Optional description.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Optional organization settings.
    /// </summary>
    public OrganizationSettings? Settings { get; init; }

    /// <summary>
    /// The user who is creating this organization (becomes owner).
    /// </summary>
    public required string CreatedByUserId { get; init; }

    /// <summary>
    /// Email of the creating user.
    /// </summary>
    public required string CreatedByEmail { get; init; }
}

/// <summary>
/// Request to update an existing organization.
/// </summary>
public sealed record UpdateOrganizationRequest
{
    /// <summary>
    /// The organization ID to update.
    /// </summary>
    public Guid OrganizationId { get; init; }

    /// <summary>
    /// New organization name (optional).
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// New description (optional).
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Updated settings (optional). Null means no change.
    /// </summary>
    public OrganizationSettings? Settings { get; init; }
}
