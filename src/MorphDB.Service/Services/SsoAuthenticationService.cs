using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Web;
using Microsoft.IdentityModel.Tokens;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Exceptions;
using MorphDB.Core.Models;
using MorphDB.Core.Security;

namespace MorphDB.Service.Services;

/// <summary>
/// LoggerMessage delegates for SsoAuthenticationService.
/// </summary>
internal static partial class SsoAuthenticationServiceLogs
{
    [LoggerMessage(LogLevel.Information, "Initiating SSO login for organization {OrgSlug}")]
    public static partial void InitiatingSsoLogin(ILogger logger, string orgSlug);

    [LoggerMessage(LogLevel.Information, "Completing SSO login for organization {OrgSlug}")]
    public static partial void CompletingSsoLogin(ILogger logger, string orgSlug);

    [LoggerMessage(LogLevel.Information, "SSO login successful for user {Email} in organization {OrganizationId}")]
    public static partial void SsoLoginSuccessful(ILogger logger, string email, Guid organizationId);

    [LoggerMessage(LogLevel.Warning, "SSO login failed: {Error}")]
    public static partial void SsoLoginFailed(ILogger logger, string error);

    [LoggerMessage(LogLevel.Warning, "Email domain {Domain} not allowed for organization")]
    public static partial void EmailDomainNotAllowed(ILogger logger, string domain);

    [LoggerMessage(LogLevel.Information, "Auto-provisioning new user {Email} for organization {OrganizationId}")]
    public static partial void AutoProvisioningUser(ILogger logger, string email, Guid organizationId);
}

/// <summary>
/// Service for handling SSO authentication flows using OIDC.
/// </summary>
public sealed class SsoAuthenticationService : ISsoAuthenticationService
{
    private readonly ISsoConfigurationService _configService;
    private readonly IMembershipRepository _membershipRepository;
    private readonly IJwtService _jwtService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<SsoAuthenticationService> _logger;

    // In-memory state store for PKCE and nonce validation (v0.x simplification)
    private readonly ConcurrentDictionary<string, SsoAuthState> _stateStore = new();
    private static readonly TimeSpan StateExpiry = TimeSpan.FromMinutes(10);

    public SsoAuthenticationService(
        ISsoConfigurationService configService,
        IMembershipRepository membershipRepository,
        IJwtService jwtService,
        IHttpClientFactory httpClientFactory,
        ILogger<SsoAuthenticationService> logger)
    {
        _configService = configService;
        _membershipRepository = membershipRepository;
        _jwtService = jwtService;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<SsoLoginInitResult> InitiateLoginAsync(
        string orgSlug,
        string redirectUri,
        CancellationToken cancellationToken = default)
    {
        SsoAuthenticationServiceLogs.InitiatingSsoLogin(_logger, orgSlug);

        var config = await _configService.GetConfigByOrgSlugAsync(orgSlug, cancellationToken);
        if (config is null)
        {
            throw new MorphDbException("SSO_NOT_CONFIGURED",
                $"SSO is not configured or not active for organization '{orgSlug}'.");
        }

        // Generate PKCE and state
        var state = GenerateSecureToken();
        var nonce = GenerateSecureToken();
        var codeVerifier = GeneratePkceCodeVerifier();
        var codeChallenge = GeneratePkceCodeChallenge(codeVerifier);

        // Store state for validation
        _stateStore[state] = new SsoAuthState
        {
            State = state,
            Nonce = nonce,
            CodeVerifier = codeVerifier,
            RedirectUri = redirectUri,
            SsoConfigId = config.SsoConfigId,
            CreatedAt = DateTimeOffset.UtcNow
        };

        // Clean up expired states
        CleanupExpiredStates();

        // Fetch discovery document
        var discovery = await FetchDiscoveryDocumentAsync(config.Authority, cancellationToken);

        // Build authorization URL
        var authUrl = BuildAuthorizationUrl(
            discovery.AuthorizationEndpoint,
            config.ClientId,
            redirectUri,
            config.Scopes,
            state,
            nonce,
            codeChallenge);

        return new SsoLoginInitResult
        {
            AuthorizationUrl = authUrl,
            State = state,
            Nonce = nonce
        };
    }

    /// <inheritdoc/>
    public async Task<SsoAuthResult> CompleteLoginAsync(
        string orgSlug,
        string code,
        string state,
        string redirectUri,
        CancellationToken cancellationToken = default)
    {
        SsoAuthenticationServiceLogs.CompletingSsoLogin(_logger, orgSlug);

        // Validate state
        if (!_stateStore.TryRemove(state, out var authState))
        {
            SsoAuthenticationServiceLogs.SsoLoginFailed(_logger, "Invalid or expired state parameter");
            return SsoAuthResult.Failed("INVALID_STATE", "Invalid or expired state parameter.");
        }

        // Verify redirect URI matches
        if (authState.RedirectUri != redirectUri)
        {
            SsoAuthenticationServiceLogs.SsoLoginFailed(_logger, "Redirect URI mismatch");
            return SsoAuthResult.Failed("REDIRECT_MISMATCH", "Redirect URI does not match.");
        }

        var config = await _configService.GetConfigAsync(authState.SsoConfigId, cancellationToken);
        if (config is null || config.Status != SsoConfigStatus.Active)
        {
            SsoAuthenticationServiceLogs.SsoLoginFailed(_logger, "SSO configuration not found or inactive");
            return SsoAuthResult.Failed("SSO_NOT_CONFIGURED", "SSO is not configured or not active.");
        }

        try
        {
            // Exchange code for tokens
            var tokenResponse = await ExchangeCodeForTokensAsync(
                config, code, redirectUri, authState.CodeVerifier, cancellationToken);

            // Parse and validate ID token
            var idToken = ParseIdToken(tokenResponse.IdToken);

            // Validate nonce
            var nonceClaim = idToken.Claims.FirstOrDefault(c => c.Type == "nonce")?.Value;
            if (nonceClaim != authState.Nonce)
            {
                SsoAuthenticationServiceLogs.SsoLoginFailed(_logger, "Invalid nonce in ID token");
                return SsoAuthResult.Failed("INVALID_NONCE", "Invalid nonce in ID token.");
            }

            // Extract user info from claims
            var userInfo = ExtractUserInfo(idToken.Claims, config.ClaimMappings);

            // Validate email domain if restricted
            if (config.AllowedDomains?.Count > 0)
            {
                var emailDomain = userInfo.Email?.Split('@').LastOrDefault();
                if (emailDomain is null || !config.AllowedDomains.Contains(emailDomain, StringComparer.OrdinalIgnoreCase))
                {
                    SsoAuthenticationServiceLogs.EmailDomainNotAllowed(_logger, emailDomain ?? "unknown");
                    return SsoAuthResult.Failed("DOMAIN_NOT_ALLOWED",
                        $"Email domain '{emailDomain}' is not allowed for this organization.");
                }
            }

            // Check or provision membership
            var (member, isNewUser) = await GetOrProvisionMemberAsync(
                config, userInfo, cancellationToken);

            if (member is null)
            {
                SsoAuthenticationServiceLogs.SsoLoginFailed(_logger, "User is not a member and auto-provisioning is disabled");
                return SsoAuthResult.Failed("NOT_A_MEMBER",
                    "You are not a member of this organization. Contact an administrator.");
            }

            // Generate MorphDB JWT
            var additionalClaims = new Dictionary<string, string>
            {
                ["sso_provider"] = config.ProviderType.ToString(),
                ["sso_sub"] = userInfo.Subject
            };

            var morphDbToken = _jwtService.GenerateToken(
                tenantId: config.OrganizationId,
                userId: member.UserId,
                email: member.Email,
                role: member.Role.ToString(),
                additionalClaims: additionalClaims,
                expiresIn: TimeSpan.FromHours(1));

            // Record usage
            await _configService.RecordUsageAsync(config.SsoConfigId, cancellationToken);

            SsoAuthenticationServiceLogs.SsoLoginSuccessful(_logger, member.Email, config.OrganizationId);

            return SsoAuthResult.Succeeded(
                userId: member.UserId,
                email: member.Email,
                name: member.DisplayName,
                organizationId: config.OrganizationId,
                role: member.Role,
                isNewUser: isNewUser,
                accessToken: morphDbToken,
                expiresAt: DateTimeOffset.UtcNow.AddHours(1),
                refreshToken: tokenResponse.RefreshToken);
        }
        catch (Exception ex)
        {
            SsoAuthenticationServiceLogs.SsoLoginFailed(_logger, ex.Message);
            return SsoAuthResult.Failed("SSO_AUTH_FAILED", ex.Message);
        }
    }

    /// <inheritdoc/>
    public async Task<SsoAuthResult> RefreshTokenAsync(
        Guid organizationId,
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        var config = await _configService.GetActiveConfigAsync(organizationId, cancellationToken);
        if (config is null)
        {
            return SsoAuthResult.Failed("SSO_NOT_CONFIGURED", "SSO is not configured for this organization.");
        }

        try
        {
            var discovery = await FetchDiscoveryDocumentAsync(config.Authority, cancellationToken);

            using var client = _httpClientFactory.CreateClient("OidcToken");
            var tokenRequest = new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
                ["client_id"] = config.ClientId
            };

            if (!string.IsNullOrEmpty(config.ClientSecret))
            {
                tokenRequest["client_secret"] = config.ClientSecret;
            }

            var response = await client.PostAsync(
                discovery.TokenEndpoint,
                new FormUrlEncodedContent(tokenRequest),
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                return SsoAuthResult.Failed("TOKEN_REFRESH_FAILED", $"Failed to refresh token: {error}");
            }

            var tokenResponse = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken);
            if (tokenResponse is null)
            {
                return SsoAuthResult.Failed("TOKEN_REFRESH_FAILED", "Invalid token response.");
            }

            // Parse ID token to get user info
            var idToken = ParseIdToken(tokenResponse.IdToken);
            var userInfo = ExtractUserInfo(idToken.Claims, config.ClaimMappings);

            // Get existing member
            var member = await _membershipRepository.GetOrganizationMemberByUserIdAsync(
                organizationId, userInfo.Subject, cancellationToken);

            if (member is null)
            {
                return SsoAuthResult.Failed("NOT_A_MEMBER", "User is no longer a member of this organization.");
            }

            // Generate new MorphDB JWT
            var morphDbToken = _jwtService.GenerateToken(
                tenantId: organizationId,
                userId: member.UserId,
                email: member.Email,
                role: member.Role.ToString(),
                expiresIn: TimeSpan.FromHours(1));

            return SsoAuthResult.Succeeded(
                userId: member.UserId,
                email: member.Email,
                name: member.DisplayName,
                organizationId: organizationId,
                role: member.Role,
                isNewUser: false,
                accessToken: morphDbToken,
                expiresAt: DateTimeOffset.UtcNow.AddHours(1),
                refreshToken: tokenResponse.RefreshToken);
        }
        catch (Exception ex)
        {
            return SsoAuthResult.Failed("TOKEN_REFRESH_FAILED", ex.Message);
        }
    }

    /// <inheritdoc/>
    public async Task<string?> LogoutAsync(
        Guid organizationId,
        string? idTokenHint,
        string? postLogoutRedirectUri,
        CancellationToken cancellationToken = default)
    {
        var config = await _configService.GetActiveConfigAsync(organizationId, cancellationToken);
        if (config is null)
        {
            return null;
        }

        var discovery = await FetchDiscoveryDocumentAsync(config.Authority, cancellationToken);

        if (string.IsNullOrEmpty(discovery.EndSessionEndpoint))
        {
            return null;
        }

        var logoutUrl = new StringBuilder(discovery.EndSessionEndpoint);
        logoutUrl.Append('?');

        if (!string.IsNullOrEmpty(idTokenHint))
        {
            logoutUrl.Append("id_token_hint=");
            logoutUrl.Append(HttpUtility.UrlEncode(idTokenHint));
            logoutUrl.Append('&');
        }

        if (!string.IsNullOrEmpty(postLogoutRedirectUri))
        {
            logoutUrl.Append("post_logout_redirect_uri=");
            logoutUrl.Append(HttpUtility.UrlEncode(postLogoutRedirectUri));
            logoutUrl.Append('&');
        }

        logoutUrl.Append("client_id=");
        logoutUrl.Append(HttpUtility.UrlEncode(config.ClientId));

        return logoutUrl.ToString();
    }

    #region Private Helpers

    private async Task<OidcDiscoveryDocument> FetchDiscoveryDocumentAsync(
        string authority,
        CancellationToken cancellationToken)
    {
        var discoveryUrl = $"{authority.TrimEnd('/')}/.well-known/openid-configuration";

        using var client = _httpClientFactory.CreateClient("OidcDiscovery");
        var response = await client.GetFromJsonAsync<OidcDiscoveryDocument>(discoveryUrl, cancellationToken);

        return response ?? throw new MorphDbException("OIDC_DISCOVERY_FAILED",
            "Failed to fetch OIDC discovery document.");
    }

    private async Task<TokenResponse> ExchangeCodeForTokensAsync(
        SsoConfiguration config,
        string code,
        string redirectUri,
        string codeVerifier,
        CancellationToken cancellationToken)
    {
        var discovery = await FetchDiscoveryDocumentAsync(config.Authority, cancellationToken);

        using var client = _httpClientFactory.CreateClient("OidcToken");

        var tokenRequest = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["client_id"] = config.ClientId,
            ["code_verifier"] = codeVerifier
        };

        if (!string.IsNullOrEmpty(config.ClientSecret))
        {
            tokenRequest["client_secret"] = config.ClientSecret;
        }

        var response = await client.PostAsync(
            discovery.TokenEndpoint,
            new FormUrlEncodedContent(tokenRequest),
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new MorphDbException("TOKEN_EXCHANGE_FAILED", $"Failed to exchange code for tokens: {error}");
        }

        var tokenResponse = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken);
        return tokenResponse ?? throw new MorphDbException("TOKEN_EXCHANGE_FAILED", "Invalid token response.");
    }

    private static JwtSecurityToken ParseIdToken(string? idToken)
    {
        if (string.IsNullOrEmpty(idToken))
        {
            throw new MorphDbException("INVALID_ID_TOKEN", "ID token is missing from token response.");
        }

        var handler = new JwtSecurityTokenHandler();
        if (!handler.CanReadToken(idToken))
        {
            throw new MorphDbException("INVALID_ID_TOKEN", "Cannot parse ID token.");
        }

        return handler.ReadJwtToken(idToken);
    }

    private static SsoUserInfo ExtractUserInfo(IEnumerable<Claim> claims, SsoClaimMappings? mappings)
    {
        var claimsList = claims.ToList();

        string? GetClaim(string? customName, params string[] defaults)
        {
            if (!string.IsNullOrEmpty(customName))
            {
                var claim = claimsList.FirstOrDefault(c => c.Type == customName);
                if (claim is not null)
                {
                    return claim.Value;
                }
            }

            foreach (var defaultName in defaults)
            {
                var claim = claimsList.FirstOrDefault(c => c.Type == defaultName);
                if (claim is not null)
                {
                    return claim.Value;
                }
            }

            return null;
        }

        var subject = GetClaim(mappings?.SubjectClaim, "sub", ClaimTypes.NameIdentifier);
        var email = GetClaim(mappings?.EmailClaim, "email", ClaimTypes.Email);
        var name = GetClaim(mappings?.NameClaim, "name", ClaimTypes.Name);
        var firstName = GetClaim(mappings?.FirstNameClaim, "given_name", ClaimTypes.GivenName);
        var lastName = GetClaim(mappings?.LastNameClaim, "family_name", ClaimTypes.Surname);

        if (string.IsNullOrEmpty(subject))
        {
            throw new MorphDbException("MISSING_SUBJECT", "Subject claim is missing from ID token.");
        }

        if (string.IsNullOrEmpty(email))
        {
            throw new MorphDbException("MISSING_EMAIL", "Email claim is missing from ID token.");
        }

        // Build display name from available info
        var displayName = name;
        if (string.IsNullOrEmpty(displayName) && !string.IsNullOrEmpty(firstName))
        {
            displayName = string.IsNullOrEmpty(lastName) ? firstName : $"{firstName} {lastName}";
        }

        return new SsoUserInfo
        {
            Subject = subject,
            Email = email,
            Name = displayName ?? email.Split('@').First()
        };
    }

    private async Task<(OrganizationMember? Member, bool IsNew)> GetOrProvisionMemberAsync(
        SsoConfiguration config,
        SsoUserInfo userInfo,
        CancellationToken cancellationToken)
    {
        // Check for existing member by SSO subject
        var existingMember = await _membershipRepository.GetOrganizationMemberByUserIdAsync(
            config.OrganizationId, userInfo.Subject, cancellationToken);

        if (existingMember is not null)
        {
            return (existingMember, false);
        }

        // Check by email (might have been invited)
        var memberByEmail = await _membershipRepository.GetOrganizationMemberByEmailAsync(
            config.OrganizationId, userInfo.Email, cancellationToken);

        if (memberByEmail is not null)
        {
            // Update user ID to SSO subject
            // TODO: Consider updating the user ID in the repository
            return (memberByEmail, false);
        }

        // Auto-provision if enabled
        if (!config.AutoProvisionUsers)
        {
            return (null, false);
        }

        SsoAuthenticationServiceLogs.AutoProvisioningUser(_logger, userInfo.Email, config.OrganizationId);

        var newMember = await _membershipRepository.AddOrganizationMemberAsync(new AddOrganizationMemberRequest
        {
            OrganizationId = config.OrganizationId,
            UserId = userInfo.Subject,
            Email = userInfo.Email,
            DisplayName = userInfo.Name,
            Role = config.DefaultRole
        }, cancellationToken);

        return (newMember, true);
    }

    private static string BuildAuthorizationUrl(
        string authEndpoint,
        string clientId,
        string redirectUri,
        IEnumerable<string> scopes,
        string state,
        string nonce,
        string codeChallenge)
    {
        var scopeString = string.Join(" ", scopes);

        var url = new StringBuilder(authEndpoint);
        url.Append("?response_type=code");
        url.Append("&client_id=").Append(HttpUtility.UrlEncode(clientId));
        url.Append("&redirect_uri=").Append(HttpUtility.UrlEncode(redirectUri));
        url.Append("&scope=").Append(HttpUtility.UrlEncode(scopeString));
        url.Append("&state=").Append(HttpUtility.UrlEncode(state));
        url.Append("&nonce=").Append(HttpUtility.UrlEncode(nonce));
        url.Append("&code_challenge=").Append(HttpUtility.UrlEncode(codeChallenge));
        url.Append("&code_challenge_method=S256");

        return url.ToString();
    }

    private static string GenerateSecureToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .TrimEnd('=');
    }

    private static string GeneratePkceCodeVerifier()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .TrimEnd('=');
    }

    private static string GeneratePkceCodeChallenge(string codeVerifier)
    {
        var bytes = SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier));
        return Convert.ToBase64String(bytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .TrimEnd('=');
    }

    private void CleanupExpiredStates()
    {
        var expiredStates = _stateStore
            .Where(kvp => kvp.Value.CreatedAt.Add(StateExpiry) < DateTimeOffset.UtcNow)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in expiredStates)
        {
            _stateStore.TryRemove(key, out _);
        }
    }

    #endregion

    #region Internal Types

    private sealed class SsoAuthState
    {
        public required string State { get; init; }
        public required string Nonce { get; init; }
        public required string CodeVerifier { get; init; }
        public required string RedirectUri { get; init; }
        public required Guid SsoConfigId { get; init; }
        public required DateTimeOffset CreatedAt { get; init; }
    }

    private sealed class SsoUserInfo
    {
        public required string Subject { get; init; }
        public required string Email { get; init; }
        public string? Name { get; init; }
    }

    private sealed class OidcDiscoveryDocument
    {
        [JsonPropertyName("issuer")]
        public string Issuer { get; init; } = string.Empty;

        [JsonPropertyName("authorization_endpoint")]
        public string AuthorizationEndpoint { get; init; } = string.Empty;

        [JsonPropertyName("token_endpoint")]
        public string TokenEndpoint { get; init; } = string.Empty;

        [JsonPropertyName("userinfo_endpoint")]
        public string? UserinfoEndpoint { get; init; }

        [JsonPropertyName("end_session_endpoint")]
        public string? EndSessionEndpoint { get; init; }

        [JsonPropertyName("jwks_uri")]
        public string JwksUri { get; init; } = string.Empty;
    }

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; init; }

        [JsonPropertyName("id_token")]
        public string? IdToken { get; init; }

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; init; }

        [JsonPropertyName("token_type")]
        public string? TokenType { get; init; }

        [JsonPropertyName("expires_in")]
        public int? ExpiresIn { get; init; }
    }

    #endregion
}
