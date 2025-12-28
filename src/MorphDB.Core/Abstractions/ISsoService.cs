using MorphDB.Core.Models;

namespace MorphDB.Core.Abstractions;

/// <summary>
/// Service for managing SSO configurations.
/// </summary>
public interface ISsoConfigurationService
{
    /// <summary>
    /// Gets an SSO configuration by ID.
    /// </summary>
    Task<SsoConfiguration?> GetConfigAsync(
        Guid ssoConfigId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all SSO configurations for an organization.
    /// </summary>
    Task<IReadOnlyList<SsoConfiguration>> ListConfigsAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the active SSO configuration for an organization.
    /// </summary>
    Task<SsoConfiguration?> GetActiveConfigAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets an SSO configuration by organization slug (for login).
    /// </summary>
    Task<SsoConfiguration?> GetConfigByOrgSlugAsync(
        string orgSlug,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates or updates an SSO configuration.
    /// </summary>
    Task<SsoConfiguration> UpsertConfigAsync(
        UpsertSsoConfigRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Activates an SSO configuration.
    /// </summary>
    Task ActivateConfigAsync(
        Guid ssoConfigId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deactivates an SSO configuration.
    /// </summary>
    Task DeactivateConfigAsync(
        Guid ssoConfigId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes an SSO configuration.
    /// </summary>
    Task DeleteConfigAsync(
        Guid ssoConfigId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Tests an SSO configuration by fetching the OIDC discovery document.
    /// </summary>
    Task<SsoTestResult> TestConfigAsync(
        Guid ssoConfigId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records successful use of an SSO configuration.
    /// </summary>
    Task RecordUsageAsync(
        Guid ssoConfigId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of testing an SSO configuration.
/// </summary>
public sealed class SsoTestResult
{
    /// <summary>Whether the test was successful.</summary>
    public bool Success { get; init; }

    /// <summary>Error message if failed.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>OIDC issuer from discovery document.</summary>
    public string? Issuer { get; init; }

    /// <summary>Authorization endpoint.</summary>
    public string? AuthorizationEndpoint { get; init; }

    /// <summary>Token endpoint.</summary>
    public string? TokenEndpoint { get; init; }

    /// <summary>Userinfo endpoint.</summary>
    public string? UserinfoEndpoint { get; init; }

    /// <summary>JWKS URI.</summary>
    public string? JwksUri { get; init; }

    /// <summary>Supported scopes.</summary>
    public IReadOnlyList<string>? SupportedScopes { get; init; }

    /// <summary>Creates a success result.</summary>
    public static SsoTestResult Succeeded(
        string issuer,
        string authorizationEndpoint,
        string tokenEndpoint,
        string? userinfoEndpoint,
        string jwksUri,
        IReadOnlyList<string>? supportedScopes) => new()
        {
            Success = true,
            Issuer = issuer,
            AuthorizationEndpoint = authorizationEndpoint,
            TokenEndpoint = tokenEndpoint,
            UserinfoEndpoint = userinfoEndpoint,
            JwksUri = jwksUri,
            SupportedScopes = supportedScopes
        };

    /// <summary>Creates a failure result.</summary>
    public static SsoTestResult Failed(string errorMessage) => new()
    {
        Success = false,
        ErrorMessage = errorMessage
    };
}

/// <summary>
/// Service for handling SSO authentication flows.
/// </summary>
public interface ISsoAuthenticationService
{
    /// <summary>
    /// Initiates an SSO login flow.
    /// </summary>
    /// <param name="orgSlug">Organization slug identifying the SSO config.</param>
    /// <param name="redirectUri">Where to redirect after authentication.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Login initiation result with authorization URL.</returns>
    Task<SsoLoginInitResult> InitiateLoginAsync(
        string orgSlug,
        string redirectUri,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Completes an SSO login flow after callback from IdP.
    /// </summary>
    /// <param name="orgSlug">Organization slug.</param>
    /// <param name="code">Authorization code from IdP.</param>
    /// <param name="state">State parameter for CSRF validation.</param>
    /// <param name="redirectUri">The redirect URI used in the initial request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Authentication result with tokens.</returns>
    Task<SsoAuthResult> CompleteLoginAsync(
        string orgSlug,
        string code,
        string state,
        string redirectUri,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Refreshes an access token using a refresh token.
    /// </summary>
    Task<SsoAuthResult> RefreshTokenAsync(
        Guid organizationId,
        string refreshToken,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Logs out a user from SSO.
    /// </summary>
    Task<string?> LogoutAsync(
        Guid organizationId,
        string? idTokenHint,
        string? postLogoutRedirectUri,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Repository for SSO configuration persistence.
/// </summary>
public interface ISsoConfigurationRepository
{
    /// <summary>Gets an SSO configuration by ID.</summary>
    Task<SsoConfiguration?> GetByIdAsync(
        Guid ssoConfigId,
        CancellationToken cancellationToken = default);

    /// <summary>Gets all SSO configurations for an organization.</summary>
    Task<IReadOnlyList<SsoConfiguration>> ListByOrganizationAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default);

    /// <summary>Gets the active configuration for an organization.</summary>
    Task<SsoConfiguration?> GetActiveByOrganizationAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default);

    /// <summary>Creates a new SSO configuration.</summary>
    Task<SsoConfiguration> CreateAsync(
        SsoConfiguration config,
        CancellationToken cancellationToken = default);

    /// <summary>Updates an existing SSO configuration.</summary>
    Task<SsoConfiguration> UpdateAsync(
        SsoConfiguration config,
        CancellationToken cancellationToken = default);

    /// <summary>Updates the status of an SSO configuration.</summary>
    Task UpdateStatusAsync(
        Guid ssoConfigId,
        SsoConfigStatus status,
        string? errorMessage = null,
        CancellationToken cancellationToken = default);

    /// <summary>Records usage of an SSO configuration.</summary>
    Task RecordUsageAsync(
        Guid ssoConfigId,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes an SSO configuration.</summary>
    Task DeleteAsync(
        Guid ssoConfigId,
        CancellationToken cancellationToken = default);
}
