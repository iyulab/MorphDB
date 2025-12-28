using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Models;

namespace MorphDB.Service.Controllers;

/// <summary>
/// Controller for SSO configuration and authentication.
/// </summary>
[ApiController]
[Route("api/sso")]
public sealed class SsoController : ControllerBase
{
    private readonly ISsoConfigurationService _configService;
    private readonly ISsoAuthenticationService _authService;
    private readonly IPermissionService _permissionService;

    public SsoController(
        ISsoConfigurationService configService,
        ISsoAuthenticationService authService,
        IPermissionService permissionService)
    {
        _configService = configService;
        _authService = authService;
        _permissionService = permissionService;
    }

    #region Configuration Endpoints (Admin)

    /// <summary>
    /// Lists SSO configurations for an organization.
    /// </summary>
    [HttpGet("organizations/{organizationId:guid}/configs")]
    [Authorize]
    public async Task<ActionResult<IReadOnlyList<SsoConfigResponse>>> ListConfigs(
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        var userId = User.FindFirst("sub")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        if (!await _permissionService.HasOrganizationPermissionAsync(userId, organizationId, Permissions.Organization.ManageSso, cancellationToken))
        {
            return Forbid();
        }

        var configs = await _configService.ListConfigsAsync(organizationId, cancellationToken);
        return Ok(configs.Select(MapToResponse).ToList());
    }

    /// <summary>
    /// Gets an SSO configuration by ID.
    /// </summary>
    [HttpGet("configs/{ssoConfigId:guid}")]
    [Authorize]
    public async Task<ActionResult<SsoConfigResponse>> GetConfig(
        Guid ssoConfigId,
        CancellationToken cancellationToken)
    {
        var config = await _configService.GetConfigAsync(ssoConfigId, cancellationToken);
        if (config is null)
        {
            return NotFound();
        }

        var userId = User.FindFirst("sub")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        if (!await _permissionService.HasOrganizationPermissionAsync(userId, config.OrganizationId, Permissions.Organization.ManageSso, cancellationToken))
        {
            return Forbid();
        }

        return Ok(MapToResponse(config));
    }

    /// <summary>
    /// Creates a new SSO configuration.
    /// </summary>
    [HttpPost("organizations/{organizationId:guid}/configs")]
    [Authorize]
    public async Task<ActionResult<SsoConfigResponse>> CreateConfig(
        Guid organizationId,
        [FromBody] CreateSsoConfigRequest request,
        CancellationToken cancellationToken)
    {
        var userId = User.FindFirst("sub")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        if (!await _permissionService.HasOrganizationPermissionAsync(userId, organizationId, Permissions.Organization.ManageSso, cancellationToken))
        {
            return Forbid();
        }

        var upsertRequest = new UpsertSsoConfigRequest
        {
            OrganizationId = organizationId,
            Name = request.Name,
            ProviderType = request.ProviderType,
            Authority = request.Authority,
            ClientId = request.ClientId,
            ClientSecret = request.ClientSecret,
            Scopes = request.Scopes,
            AllowedDomains = request.AllowedDomains,
            ClaimMappings = request.ClaimMappings,
            AutoProvisionUsers = request.AutoProvisionUsers,
            DefaultRole = request.DefaultRole
        };

        var config = await _configService.UpsertConfigAsync(upsertRequest, cancellationToken);
        return CreatedAtAction(nameof(GetConfig), new { ssoConfigId = config.SsoConfigId }, MapToResponse(config));
    }

    /// <summary>
    /// Updates an SSO configuration.
    /// </summary>
    [HttpPut("configs/{ssoConfigId:guid}")]
    [Authorize]
    public async Task<ActionResult<SsoConfigResponse>> UpdateConfig(
        Guid ssoConfigId,
        [FromBody] UpdateSsoConfigRequest request,
        CancellationToken cancellationToken)
    {
        var existing = await _configService.GetConfigAsync(ssoConfigId, cancellationToken);
        if (existing is null)
        {
            return NotFound();
        }

        var userId = User.FindFirst("sub")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        if (!await _permissionService.HasOrganizationPermissionAsync(userId, existing.OrganizationId, Permissions.Organization.ManageSso, cancellationToken))
        {
            return Forbid();
        }

        var upsertRequest = new UpsertSsoConfigRequest
        {
            OrganizationId = existing.OrganizationId,
            SsoConfigId = ssoConfigId,
            Name = request.Name ?? existing.Name,
            ProviderType = request.ProviderType ?? existing.ProviderType,
            Authority = request.Authority ?? existing.Authority,
            ClientId = request.ClientId ?? existing.ClientId,
            ClientSecret = request.ClientSecret,
            Scopes = request.Scopes ?? existing.Scopes,
            AllowedDomains = request.AllowedDomains ?? existing.AllowedDomains,
            ClaimMappings = request.ClaimMappings ?? existing.ClaimMappings,
            AutoProvisionUsers = request.AutoProvisionUsers ?? existing.AutoProvisionUsers,
            DefaultRole = request.DefaultRole ?? existing.DefaultRole
        };

        var config = await _configService.UpsertConfigAsync(upsertRequest, cancellationToken);
        return Ok(MapToResponse(config));
    }

    /// <summary>
    /// Deletes an SSO configuration.
    /// </summary>
    [HttpDelete("configs/{ssoConfigId:guid}")]
    [Authorize]
    public async Task<IActionResult> DeleteConfig(
        Guid ssoConfigId,
        CancellationToken cancellationToken)
    {
        var existing = await _configService.GetConfigAsync(ssoConfigId, cancellationToken);
        if (existing is null)
        {
            return NotFound();
        }

        var userId = User.FindFirst("sub")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        if (!await _permissionService.HasOrganizationPermissionAsync(userId, existing.OrganizationId, Permissions.Organization.ManageSso, cancellationToken))
        {
            return Forbid();
        }

        await _configService.DeleteConfigAsync(ssoConfigId, cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Tests an SSO configuration.
    /// </summary>
    [HttpPost("configs/{ssoConfigId:guid}/test")]
    [Authorize]
    public async Task<ActionResult<SsoTestResult>> TestConfig(
        Guid ssoConfigId,
        CancellationToken cancellationToken)
    {
        var existing = await _configService.GetConfigAsync(ssoConfigId, cancellationToken);
        if (existing is null)
        {
            return NotFound();
        }

        var userId = User.FindFirst("sub")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        if (!await _permissionService.HasOrganizationPermissionAsync(userId, existing.OrganizationId, Permissions.Organization.ManageSso, cancellationToken))
        {
            return Forbid();
        }

        var result = await _configService.TestConfigAsync(ssoConfigId, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Activates an SSO configuration.
    /// </summary>
    [HttpPost("configs/{ssoConfigId:guid}/activate")]
    [Authorize]
    public async Task<IActionResult> ActivateConfig(
        Guid ssoConfigId,
        CancellationToken cancellationToken)
    {
        var existing = await _configService.GetConfigAsync(ssoConfigId, cancellationToken);
        if (existing is null)
        {
            return NotFound();
        }

        var userId = User.FindFirst("sub")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        if (!await _permissionService.HasOrganizationPermissionAsync(userId, existing.OrganizationId, Permissions.Organization.ManageSso, cancellationToken))
        {
            return Forbid();
        }

        await _configService.ActivateConfigAsync(ssoConfigId, cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Deactivates an SSO configuration.
    /// </summary>
    [HttpPost("configs/{ssoConfigId:guid}/deactivate")]
    [Authorize]
    public async Task<IActionResult> DeactivateConfig(
        Guid ssoConfigId,
        CancellationToken cancellationToken)
    {
        var existing = await _configService.GetConfigAsync(ssoConfigId, cancellationToken);
        if (existing is null)
        {
            return NotFound();
        }

        var userId = User.FindFirst("sub")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        if (!await _permissionService.HasOrganizationPermissionAsync(userId, existing.OrganizationId, Permissions.Organization.ManageSso, cancellationToken))
        {
            return Forbid();
        }

        await _configService.DeactivateConfigAsync(ssoConfigId, cancellationToken);
        return NoContent();
    }

    #endregion

    #region Authentication Endpoints (Public)

    /// <summary>
    /// Initiates an SSO login flow.
    /// </summary>
    [HttpGet("login/{orgSlug}")]
    [AllowAnonymous]
    public async Task<ActionResult<SsoLoginInitResponse>> InitiateLogin(
        string orgSlug,
        [FromQuery] string redirectUri,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(redirectUri))
        {
            return BadRequest(new { error = "redirect_uri is required" });
        }

        try
        {
            var result = await _authService.InitiateLoginAsync(orgSlug, redirectUri, cancellationToken);
            return Ok(new SsoLoginInitResponse
            {
                AuthorizationUrl = result.AuthorizationUrl,
                State = result.State
            });
        }
        catch (Core.Exceptions.MorphDbException ex) when (ex.ErrorCode == "SSO_NOT_CONFIGURED")
        {
            return NotFound(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Completes an SSO login flow (callback endpoint).
    /// </summary>
    [HttpPost("callback/{orgSlug}")]
    [AllowAnonymous]
    public async Task<ActionResult<SsoAuthResult>> CompleteLogin(
        string orgSlug,
        [FromBody] SsoCallbackRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _authService.CompleteLoginAsync(
            orgSlug,
            request.Code,
            request.State,
            request.RedirectUri,
            cancellationToken);

        if (!result.Success)
        {
            return BadRequest(new { error = result.ErrorCode, message = result.ErrorMessage });
        }

        return Ok(result);
    }

    /// <summary>
    /// Refreshes an SSO token.
    /// </summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<ActionResult<SsoAuthResult>> RefreshToken(
        [FromBody] SsoRefreshRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _authService.RefreshTokenAsync(
            request.OrganizationId,
            request.RefreshToken,
            cancellationToken);

        if (!result.Success)
        {
            return BadRequest(new { error = result.ErrorCode, message = result.ErrorMessage });
        }

        return Ok(result);
    }

    /// <summary>
    /// Gets the logout URL for SSO.
    /// </summary>
    [HttpGet("logout/{organizationId:guid}")]
    [Authorize]
    public async Task<ActionResult<SsoLogoutResponse>> GetLogoutUrl(
        Guid organizationId,
        [FromQuery] string? idTokenHint,
        [FromQuery] string? postLogoutRedirectUri,
        CancellationToken cancellationToken)
    {
        var logoutUrl = await _authService.LogoutAsync(
            organizationId,
            idTokenHint,
            postLogoutRedirectUri,
            cancellationToken);

        if (logoutUrl is null)
        {
            return Ok(new SsoLogoutResponse { LogoutUrl = null });
        }

        return Ok(new SsoLogoutResponse { LogoutUrl = logoutUrl });
    }

    #endregion

    #region Mapping

    private static SsoConfigResponse MapToResponse(SsoConfiguration config)
    {
        return new SsoConfigResponse
        {
            SsoConfigId = config.SsoConfigId,
            OrganizationId = config.OrganizationId,
            Name = config.Name,
            ProviderType = config.ProviderType,
            Authority = config.Authority,
            ClientId = config.ClientId,
            HasClientSecret = !string.IsNullOrEmpty(config.ClientSecret),
            Scopes = config.Scopes,
            AllowedDomains = config.AllowedDomains,
            ClaimMappings = config.ClaimMappings,
            AutoProvisionUsers = config.AutoProvisionUsers,
            DefaultRole = config.DefaultRole,
            Status = config.Status,
            LastError = config.LastError,
            CreatedAt = config.CreatedAt,
            UpdatedAt = config.UpdatedAt,
            LastUsedAt = config.LastUsedAt
        };
    }

    #endregion
}

#region DTOs

/// <summary>
/// Response DTO for SSO configuration.
/// </summary>
public sealed class SsoConfigResponse
{
    public Guid SsoConfigId { get; init; }
    public Guid OrganizationId { get; init; }
    public required string Name { get; init; }
    public SsoProviderType ProviderType { get; init; }
    public required string Authority { get; init; }
    public required string ClientId { get; init; }
    public bool HasClientSecret { get; init; }
    public IReadOnlyList<string> Scopes { get; init; } = [];
    public IReadOnlyList<string>? AllowedDomains { get; init; }
    public SsoClaimMappings? ClaimMappings { get; init; }
    public bool AutoProvisionUsers { get; init; }
    public OrganizationRole DefaultRole { get; init; }
    public SsoConfigStatus Status { get; init; }
    public string? LastError { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public DateTimeOffset? LastUsedAt { get; init; }
}

/// <summary>
/// Request DTO for creating an SSO configuration.
/// </summary>
public sealed class CreateSsoConfigRequest
{
    public required string Name { get; init; }
    public SsoProviderType ProviderType { get; init; }
    public required string Authority { get; init; }
    public required string ClientId { get; init; }
    public string? ClientSecret { get; init; }
    public IReadOnlyList<string>? Scopes { get; init; }
    public IReadOnlyList<string>? AllowedDomains { get; init; }
    public SsoClaimMappings? ClaimMappings { get; init; }
    public bool AutoProvisionUsers { get; init; } = true;
    public OrganizationRole DefaultRole { get; init; } = OrganizationRole.Member;
}

/// <summary>
/// Request DTO for updating an SSO configuration.
/// </summary>
public sealed class UpdateSsoConfigRequest
{
    public string? Name { get; init; }
    public SsoProviderType? ProviderType { get; init; }
    public string? Authority { get; init; }
    public string? ClientId { get; init; }
    public string? ClientSecret { get; init; }
    public IReadOnlyList<string>? Scopes { get; init; }
    public IReadOnlyList<string>? AllowedDomains { get; init; }
    public SsoClaimMappings? ClaimMappings { get; init; }
    public bool? AutoProvisionUsers { get; init; }
    public OrganizationRole? DefaultRole { get; init; }
}

/// <summary>
/// Response DTO for SSO login initiation.
/// </summary>
public sealed class SsoLoginInitResponse
{
    public required string AuthorizationUrl { get; init; }
    public required string State { get; init; }
}

/// <summary>
/// Request DTO for SSO callback.
/// </summary>
public sealed class SsoCallbackRequest
{
    public required string Code { get; init; }
    public required string State { get; init; }
    public required string RedirectUri { get; init; }
}

/// <summary>
/// Request DTO for SSO token refresh.
/// </summary>
public sealed class SsoRefreshRequest
{
    public Guid OrganizationId { get; init; }
    public required string RefreshToken { get; init; }
}

/// <summary>
/// Response DTO for SSO logout.
/// </summary>
public sealed class SsoLogoutResponse
{
    public string? LogoutUrl { get; init; }
}

#endregion
