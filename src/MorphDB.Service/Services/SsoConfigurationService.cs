using System.Net.Http.Json;
using System.Text.Json;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Exceptions;
using MorphDB.Core.Models;

namespace MorphDB.Service.Services;

/// <summary>
/// LoggerMessage delegates for SsoConfigurationService.
/// </summary>
internal static partial class SsoConfigurationServiceLogs
{
    [LoggerMessage(LogLevel.Information, "Creating SSO configuration for organization {OrganizationId}")]
    public static partial void CreatingSsoConfig(ILogger logger, Guid organizationId);

    [LoggerMessage(LogLevel.Information, "SSO configuration {SsoConfigId} created for organization {OrganizationId}")]
    public static partial void SsoConfigCreated(ILogger logger, Guid ssoConfigId, Guid organizationId);

    [LoggerMessage(LogLevel.Information, "Activating SSO configuration {SsoConfigId}")]
    public static partial void ActivatingSsoConfig(ILogger logger, Guid ssoConfigId);

    [LoggerMessage(LogLevel.Information, "Testing SSO configuration {SsoConfigId}")]
    public static partial void TestingSsoConfig(ILogger logger, Guid ssoConfigId);

    [LoggerMessage(LogLevel.Warning, "SSO configuration test failed for {SsoConfigId}: {Error}")]
    public static partial void SsoConfigTestFailed(ILogger logger, Guid ssoConfigId, string error);

    [LoggerMessage(LogLevel.Error, "Failed to fetch OIDC discovery document from {Authority}: {Error}")]
    public static partial void OidcDiscoveryFailed(ILogger logger, string authority, string error, Exception exception);
}

/// <summary>
/// Service for managing SSO configurations.
/// </summary>
public sealed class SsoConfigurationService : ISsoConfigurationService
{
    private readonly ISsoConfigurationRepository _repository;
    private readonly IOrganizationRepository _organizationRepository;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<SsoConfigurationService> _logger;

    public SsoConfigurationService(
        ISsoConfigurationRepository repository,
        IOrganizationRepository organizationRepository,
        IHttpClientFactory httpClientFactory,
        ILogger<SsoConfigurationService> logger)
    {
        _repository = repository;
        _organizationRepository = organizationRepository;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<SsoConfiguration?> GetConfigAsync(
        Guid ssoConfigId,
        CancellationToken cancellationToken = default)
    {
        return await _repository.GetByIdAsync(ssoConfigId, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<SsoConfiguration>> ListConfigsAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        return await _repository.ListByOrganizationAsync(organizationId, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<SsoConfiguration?> GetActiveConfigAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        return await _repository.GetActiveByOrganizationAsync(organizationId, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<SsoConfiguration?> GetConfigByOrgSlugAsync(
        string orgSlug,
        CancellationToken cancellationToken = default)
    {
        var org = await _organizationRepository.GetBySlugAsync(orgSlug, cancellationToken);
        if (org is null)
        {
            return null;
        }

        return await _repository.GetActiveByOrganizationAsync(org.OrganizationId, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<SsoConfiguration> UpsertConfigAsync(
        UpsertSsoConfigRequest request,
        CancellationToken cancellationToken = default)
    {
        SsoConfigurationServiceLogs.CreatingSsoConfig(_logger, request.OrganizationId);

        // Verify organization exists
        var org = await _organizationRepository.GetByIdAsync(request.OrganizationId, cancellationToken);
        if (org is null)
        {
            throw new MorphDbException("ORGANIZATION_NOT_FOUND", $"Organization '{request.OrganizationId}' not found.");
        }

        var config = new SsoConfiguration
        {
            SsoConfigId = request.SsoConfigId ?? Guid.NewGuid(),
            OrganizationId = request.OrganizationId,
            Name = request.Name,
            ProviderType = request.ProviderType,
            Authority = NormalizeAuthority(request.Authority),
            ClientId = request.ClientId,
            ClientSecret = request.ClientSecret,
            Scopes = request.Scopes ?? ["openid", "profile", "email"],
            AllowedDomains = request.AllowedDomains,
            ClaimMappings = request.ClaimMappings,
            AutoProvisionUsers = request.AutoProvisionUsers,
            DefaultRole = request.DefaultRole,
            Status = SsoConfigStatus.Disabled
        };

        SsoConfiguration result;
        if (request.SsoConfigId.HasValue)
        {
            result = await _repository.UpdateAsync(config, cancellationToken);
        }
        else
        {
            result = await _repository.CreateAsync(config, cancellationToken);
        }

        SsoConfigurationServiceLogs.SsoConfigCreated(_logger, result.SsoConfigId, result.OrganizationId);
        return result;
    }

    /// <inheritdoc/>
    public async Task ActivateConfigAsync(
        Guid ssoConfigId,
        CancellationToken cancellationToken = default)
    {
        SsoConfigurationServiceLogs.ActivatingSsoConfig(_logger, ssoConfigId);

        var config = await _repository.GetByIdAsync(ssoConfigId, cancellationToken);
        if (config is null)
        {
            throw new MorphDbException("SSO_CONFIG_NOT_FOUND", $"SSO configuration '{ssoConfigId}' not found.");
        }

        // Test the configuration before activating
        var testResult = await TestConfigAsync(ssoConfigId, cancellationToken);
        if (!testResult.Success)
        {
            throw new MorphDbException("SSO_CONFIG_INVALID", $"SSO configuration test failed: {testResult.ErrorMessage}");
        }

        // Deactivate any existing active config for this organization
        var existingActive = await _repository.GetActiveByOrganizationAsync(config.OrganizationId, cancellationToken);
        if (existingActive is not null && existingActive.SsoConfigId != ssoConfigId)
        {
            await _repository.UpdateStatusAsync(existingActive.SsoConfigId, SsoConfigStatus.Disabled, cancellationToken: cancellationToken);
        }

        await _repository.UpdateStatusAsync(ssoConfigId, SsoConfigStatus.Active, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task DeactivateConfigAsync(
        Guid ssoConfigId,
        CancellationToken cancellationToken = default)
    {
        await _repository.UpdateStatusAsync(ssoConfigId, SsoConfigStatus.Disabled, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task DeleteConfigAsync(
        Guid ssoConfigId,
        CancellationToken cancellationToken = default)
    {
        await _repository.DeleteAsync(ssoConfigId, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<SsoTestResult> TestConfigAsync(
        Guid ssoConfigId,
        CancellationToken cancellationToken = default)
    {
        SsoConfigurationServiceLogs.TestingSsoConfig(_logger, ssoConfigId);

        var config = await _repository.GetByIdAsync(ssoConfigId, cancellationToken);
        if (config is null)
        {
            return SsoTestResult.Failed("SSO configuration not found.");
        }

        try
        {
            var discoveryDoc = await FetchOidcDiscoveryAsync(config.Authority, cancellationToken);

            return SsoTestResult.Succeeded(
                issuer: discoveryDoc.Issuer,
                authorizationEndpoint: discoveryDoc.AuthorizationEndpoint,
                tokenEndpoint: discoveryDoc.TokenEndpoint,
                userinfoEndpoint: discoveryDoc.UserinfoEndpoint,
                jwksUri: discoveryDoc.JwksUri,
                supportedScopes: discoveryDoc.ScopesSupported);
        }
        catch (Exception ex)
        {
            SsoConfigurationServiceLogs.SsoConfigTestFailed(_logger, ssoConfigId, ex.Message);

            // Update status to error
            await _repository.UpdateStatusAsync(ssoConfigId, SsoConfigStatus.Error, ex.Message, cancellationToken);

            return SsoTestResult.Failed(ex.Message);
        }
    }

    /// <inheritdoc/>
    public async Task RecordUsageAsync(
        Guid ssoConfigId,
        CancellationToken cancellationToken = default)
    {
        await _repository.RecordUsageAsync(ssoConfigId, cancellationToken);
    }

    private async Task<OidcDiscoveryDocument> FetchOidcDiscoveryAsync(
        string authority,
        CancellationToken cancellationToken)
    {
        var discoveryUrl = $"{authority.TrimEnd('/')}/.well-known/openid-configuration";

        using var client = _httpClientFactory.CreateClient("OidcDiscovery");
        client.Timeout = TimeSpan.FromSeconds(10);

        try
        {
            var response = await client.GetAsync(discoveryUrl, cancellationToken);
            response.EnsureSuccessStatusCode();

            var doc = await response.Content.ReadFromJsonAsync<OidcDiscoveryDocument>(cancellationToken);
            if (doc is null)
            {
                throw new InvalidOperationException("Failed to parse OIDC discovery document.");
            }

            return doc;
        }
        catch (Exception ex)
        {
            SsoConfigurationServiceLogs.OidcDiscoveryFailed(_logger, authority, ex.Message, ex);
            throw new MorphDbException("OIDC_DISCOVERY_FAILED",
                $"Failed to fetch OIDC discovery document from '{authority}': {ex.Message}");
        }
    }

    private static string NormalizeAuthority(string authority)
    {
        // Remove trailing slash if present
        return authority.TrimEnd('/');
    }

    /// <summary>
    /// OIDC Discovery Document structure.
    /// </summary>
    private sealed class OidcDiscoveryDocument
    {
        public string Issuer { get; init; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("authorization_endpoint")]
        public string AuthorizationEndpoint { get; init; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("token_endpoint")]
        public string TokenEndpoint { get; init; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("userinfo_endpoint")]
        public string? UserinfoEndpoint { get; init; }

        [System.Text.Json.Serialization.JsonPropertyName("jwks_uri")]
        public string JwksUri { get; init; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("scopes_supported")]
        public List<string>? ScopesSupported { get; init; }
    }
}
