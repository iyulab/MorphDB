namespace MorphDB.Core.Models;

/// <summary>
/// Represents an organization that can own multiple projects.
/// Organizations provide hierarchical multi-tenancy and centralized user management.
/// </summary>
public sealed class Organization
{
    /// <summary>
    /// Unique identifier for the organization.
    /// </summary>
    public Guid OrganizationId { get; init; }

    /// <summary>
    /// Human-readable organization name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// URL-safe unique identifier (e.g., "acme-corp").
    /// </summary>
    public required string Slug { get; init; }

    /// <summary>
    /// Optional description of the organization.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Organization configuration and settings.
    /// </summary>
    public OrganizationSettings? Settings { get; init; }

    /// <summary>
    /// Organization status.
    /// </summary>
    public OrganizationStatus Status { get; init; } = OrganizationStatus.Active;

    /// <summary>
    /// Timestamp when the organization was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Timestamp when the organization was last updated.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>
/// Organization-specific settings and configuration.
/// </summary>
public sealed class OrganizationSettings
{
    /// <summary>
    /// Default locale for the organization.
    /// </summary>
    public string? DefaultLocale { get; init; }

    /// <summary>
    /// Timezone for the organization (e.g., "Asia/Seoul").
    /// </summary>
    public string? Timezone { get; init; }

    /// <summary>
    /// Maximum number of projects allowed in this organization.
    /// </summary>
    public int? MaxProjects { get; init; }

    /// <summary>
    /// Maximum number of members allowed in this organization.
    /// </summary>
    public int? MaxMembers { get; init; }

    /// <summary>
    /// SSO configuration (populated when SSO is enabled).
    /// </summary>
    public SsoSettings? Sso { get; init; }

    /// <summary>
    /// Custom metadata/tags for the organization.
    /// </summary>
    public Dictionary<string, string>? Metadata { get; init; }
}

/// <summary>
/// SSO configuration for an organization.
/// </summary>
public sealed class SsoSettings
{
    /// <summary>
    /// Whether SSO is enabled for this organization.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// SSO provider type (oidc, saml).
    /// </summary>
    public string? ProviderType { get; init; }

    /// <summary>
    /// Whether to enforce SSO (disable password login).
    /// </summary>
    public bool EnforceSso { get; init; }

    /// <summary>
    /// Allowed email domains for SSO users.
    /// </summary>
    public List<string>? AllowedDomains { get; init; }
}

/// <summary>
/// Organization lifecycle status.
/// </summary>
public enum OrganizationStatus
{
    /// <summary>
    /// Organization is active and operational.
    /// </summary>
    Active = 1,

    /// <summary>
    /// Organization is suspended (temporarily disabled).
    /// </summary>
    Suspended = 2,

    /// <summary>
    /// Organization has been deleted (soft delete).
    /// </summary>
    Deleted = 3
}
