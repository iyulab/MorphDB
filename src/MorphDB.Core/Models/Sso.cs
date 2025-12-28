namespace MorphDB.Core.Models;

/// <summary>
/// SSO provider types supported by MorphDB.
/// </summary>
public enum SsoProviderType
{
    /// <summary>Generic OpenID Connect provider.</summary>
    Oidc = 0,

    /// <summary>Microsoft Entra ID (formerly Azure AD).</summary>
    EntraId = 1,

    /// <summary>Google Workspace.</summary>
    Google = 2,

    /// <summary>Okta.</summary>
    Okta = 3,

    /// <summary>Auth0.</summary>
    Auth0 = 4,

    /// <summary>Keycloak.</summary>
    Keycloak = 5,

    /// <summary>SAML 2.0 provider (future support).</summary>
    Saml = 10
}

/// <summary>
/// Status of an SSO configuration.
/// </summary>
public enum SsoConfigStatus
{
    /// <summary>Configuration is disabled.</summary>
    Disabled = 0,

    /// <summary>Configuration is active and usable.</summary>
    Active = 1,

    /// <summary>Configuration is being tested.</summary>
    Testing = 2,

    /// <summary>Configuration has errors.</summary>
    Error = 3
}

/// <summary>
/// SSO configuration for an organization.
/// </summary>
public sealed class SsoConfiguration
{
    /// <summary>Unique identifier.</summary>
    public Guid SsoConfigId { get; init; }

    /// <summary>Organization this SSO config belongs to.</summary>
    public Guid OrganizationId { get; init; }

    /// <summary>Display name for this SSO configuration.</summary>
    public required string Name { get; init; }

    /// <summary>Provider type.</summary>
    public SsoProviderType ProviderType { get; init; }

    /// <summary>OIDC Authority URL (issuer).</summary>
    public required string Authority { get; init; }

    /// <summary>Client ID from the identity provider.</summary>
    public required string ClientId { get; init; }

    /// <summary>Client Secret (encrypted at rest).</summary>
    public string? ClientSecret { get; init; }

    /// <summary>Scopes to request.</summary>
    public IReadOnlyList<string> Scopes { get; init; } = ["openid", "profile", "email"];

    /// <summary>Email domain restrictions (e.g., "company.com").</summary>
    public IReadOnlyList<string>? AllowedDomains { get; init; }

    /// <summary>Custom claim mappings.</summary>
    public SsoClaimMappings? ClaimMappings { get; init; }

    /// <summary>Whether to auto-provision users on first login.</summary>
    public bool AutoProvisionUsers { get; init; } = true;

    /// <summary>Default role for auto-provisioned users.</summary>
    public OrganizationRole DefaultRole { get; init; } = OrganizationRole.Member;

    /// <summary>Configuration status.</summary>
    public SsoConfigStatus Status { get; init; }

    /// <summary>Last error message if status is Error.</summary>
    public string? LastError { get; init; }

    /// <summary>When this configuration was created.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>When this configuration was last updated.</summary>
    public DateTimeOffset UpdatedAt { get; init; }

    /// <summary>When this configuration was last used for authentication.</summary>
    public DateTimeOffset? LastUsedAt { get; init; }
}

/// <summary>
/// Custom claim mappings for SSO.
/// </summary>
public sealed class SsoClaimMappings
{
    /// <summary>Claim name for user ID (default: sub).</summary>
    public string? SubjectClaim { get; init; }

    /// <summary>Claim name for email (default: email).</summary>
    public string? EmailClaim { get; init; }

    /// <summary>Claim name for display name.</summary>
    public string? NameClaim { get; init; }

    /// <summary>Claim name for first name.</summary>
    public string? FirstNameClaim { get; init; }

    /// <summary>Claim name for last name.</summary>
    public string? LastNameClaim { get; init; }

    /// <summary>Claim name for groups/roles.</summary>
    public string? GroupsClaim { get; init; }

    /// <summary>Group-to-role mapping (IdP group -> OrganizationRole).</summary>
    public IReadOnlyDictionary<string, OrganizationRole>? GroupRoleMappings { get; init; }
}

/// <summary>
/// Request to create or update an SSO configuration.
/// </summary>
public sealed class UpsertSsoConfigRequest
{
    /// <summary>Organization ID.</summary>
    public Guid OrganizationId { get; init; }

    /// <summary>Existing SSO config ID (for updates).</summary>
    public Guid? SsoConfigId { get; init; }

    /// <summary>Display name.</summary>
    public required string Name { get; init; }

    /// <summary>Provider type.</summary>
    public SsoProviderType ProviderType { get; init; }

    /// <summary>OIDC Authority URL.</summary>
    public required string Authority { get; init; }

    /// <summary>Client ID.</summary>
    public required string ClientId { get; init; }

    /// <summary>Client Secret.</summary>
    public string? ClientSecret { get; init; }

    /// <summary>Scopes to request.</summary>
    public IReadOnlyList<string>? Scopes { get; init; }

    /// <summary>Allowed email domains.</summary>
    public IReadOnlyList<string>? AllowedDomains { get; init; }

    /// <summary>Claim mappings.</summary>
    public SsoClaimMappings? ClaimMappings { get; init; }

    /// <summary>Auto-provision users.</summary>
    public bool AutoProvisionUsers { get; init; } = true;

    /// <summary>Default role for new users.</summary>
    public OrganizationRole DefaultRole { get; init; } = OrganizationRole.Member;
}

/// <summary>
/// Result of SSO authentication.
/// </summary>
public sealed class SsoAuthResult
{
    /// <summary>Whether authentication was successful.</summary>
    public bool Success { get; init; }

    /// <summary>Error code if failed.</summary>
    public string? ErrorCode { get; init; }

    /// <summary>Error message if failed.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>Authenticated user ID.</summary>
    public string? UserId { get; init; }

    /// <summary>User's email.</summary>
    public string? Email { get; init; }

    /// <summary>User's display name.</summary>
    public string? Name { get; init; }

    /// <summary>Organization the user authenticated to.</summary>
    public Guid? OrganizationId { get; init; }

    /// <summary>User's role in the organization.</summary>
    public OrganizationRole? Role { get; init; }

    /// <summary>Whether this is a new user that was auto-provisioned.</summary>
    public bool IsNewUser { get; init; }

    /// <summary>JWT access token for API access.</summary>
    public string? AccessToken { get; init; }

    /// <summary>Token expiration time.</summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>Refresh token (if available).</summary>
    public string? RefreshToken { get; init; }

    /// <summary>Creates a success result.</summary>
    public static SsoAuthResult Succeeded(
        string userId,
        string email,
        string? name,
        Guid organizationId,
        OrganizationRole role,
        bool isNewUser,
        string accessToken,
        DateTimeOffset expiresAt,
        string? refreshToken = null) => new()
        {
            Success = true,
            UserId = userId,
            Email = email,
            Name = name,
            OrganizationId = organizationId,
            Role = role,
            IsNewUser = isNewUser,
            AccessToken = accessToken,
            ExpiresAt = expiresAt,
            RefreshToken = refreshToken
        };

    /// <summary>Creates a failure result.</summary>
    public static SsoAuthResult Failed(string errorCode, string errorMessage) => new()
    {
        Success = false,
        ErrorCode = errorCode,
        ErrorMessage = errorMessage
    };
}

/// <summary>
/// SSO login initiation result.
/// </summary>
public sealed class SsoLoginInitResult
{
    /// <summary>Redirect URL to the identity provider.</summary>
    public required string AuthorizationUrl { get; init; }

    /// <summary>State parameter for CSRF protection.</summary>
    public required string State { get; init; }

    /// <summary>Nonce for replay protection.</summary>
    public required string Nonce { get; init; }
}
