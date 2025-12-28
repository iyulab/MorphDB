using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MorphDB.Core.Security;
using MorphDB.Service.Services;

namespace MorphDB.Service.Controllers;

/// <summary>
/// Controller for security management operations.
/// </summary>
[ApiController]
[Route("api/security")]
public sealed class SecurityController : ControllerBase
{
    private readonly IApiKeyService _apiKeyService;
    private readonly ISecurityPolicyService _policyService;
    private readonly ITenantContextAccessor _tenantContext;

    public SecurityController(
        IApiKeyService apiKeyService,
        ISecurityPolicyService policyService,
        ITenantContextAccessor tenantContext)
    {
        _apiKeyService = apiKeyService;
        _policyService = policyService;
        _tenantContext = tenantContext;
    }

    #region API Keys

    /// <summary>
    /// Gets all API keys for the current tenant.
    /// </summary>
    [HttpGet("keys")]
    [Authorize(Roles = "service,admin")]
    [ProducesResponseType<IReadOnlyList<ApiKeyResponse>>(200)]
    public async Task<IActionResult> GetApiKeys(CancellationToken cancellationToken)
    {
        var keys = await _apiKeyService.GetKeysAsync(_tenantContext.TenantId, cancellationToken);
        var response = keys.Select(MapToResponse).ToList();
        return Ok(response);
    }

    /// <summary>
    /// Creates a new API key.
    /// </summary>
    [HttpPost("keys")]
    [Authorize(Roles = "service,admin")]
    [ProducesResponseType<CreateApiKeyResponse>(201)]
    public async Task<IActionResult> CreateApiKey(
        [FromBody] CreateApiKeyRequest request,
        CancellationToken cancellationToken)
    {
        var (key, rawKey) = await _apiKeyService.CreateKeyAsync(
            _tenantContext.TenantId,
            request.KeyType,
            request.Name,
            request.Description,
            request.ExpiresAt,
            cancellationToken);

        return CreatedAtAction(
            nameof(GetApiKeys),
            new CreateApiKeyResponse
            {
                Key = MapToResponse(key),
                RawKey = rawKey
            });
    }

    /// <summary>
    /// Revokes an API key.
    /// </summary>
    [HttpDelete("keys/{keyId:guid}")]
    [Authorize(Roles = "service,admin")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> RevokeApiKey(Guid keyId, CancellationToken cancellationToken)
    {
        await _apiKeyService.RevokeKeyAsync(_tenantContext.TenantId, keyId, cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Rotates an API key (creates new, optionally revokes old).
    /// </summary>
    [HttpPost("keys/{keyId:guid}/rotate")]
    [Authorize(Roles = "service,admin")]
    [ProducesResponseType<CreateApiKeyResponse>(200)]
    public async Task<IActionResult> RotateApiKey(
        Guid keyId,
        [FromQuery] bool revokeOld = true,
        CancellationToken cancellationToken = default)
    {
        var (key, rawKey) = await _apiKeyService.RotateKeyAsync(
            _tenantContext.TenantId,
            keyId,
            revokeOld,
            cancellationToken);

        return Ok(new CreateApiKeyResponse
        {
            Key = MapToResponse(key),
            RawKey = rawKey
        });
    }

    #endregion

    #region Security Policies (RLS)

    /// <summary>
    /// Gets all security policies for a table.
    /// </summary>
    [HttpGet("policies/{tableName}")]
    [Authorize(Roles = "service,admin")]
    [ProducesResponseType<IReadOnlyList<SecurityPolicyResponse>>(200)]
    public async Task<IActionResult> GetPolicies(string tableName, CancellationToken cancellationToken)
    {
        var policies = await _policyService.GetPoliciesByTableNameAsync(
            _tenantContext.TenantId,
            tableName,
            cancellationToken);

        var response = policies.Select(MapToResponse).ToList();
        return Ok(response);
    }

    /// <summary>
    /// Creates a new security policy.
    /// </summary>
    [HttpPost("policies")]
    [Authorize(Roles = "service,admin")]
    [ProducesResponseType<SecurityPolicyResponse>(201)]
    public async Task<IActionResult> CreatePolicy(
        [FromBody] CreateSecurityPolicyRequest request,
        CancellationToken cancellationToken)
    {
        var policy = await _policyService.CreatePolicyAsync(
            _tenantContext.TenantId,
            new CreatePolicyRequest
            {
                Name = request.Name,
                TableName = request.TableName,
                PolicyType = request.PolicyType,
                Expression = request.Expression,
                Description = request.Description
            },
            cancellationToken);

        return CreatedAtAction(
            nameof(GetPolicies),
            new { tableName = request.TableName },
            MapToResponse(policy));
    }

    /// <summary>
    /// Updates a security policy.
    /// </summary>
    [HttpPatch("policies/{policyId:guid}")]
    [Authorize(Roles = "service,admin")]
    [ProducesResponseType<SecurityPolicyResponse>(200)]
    public async Task<IActionResult> UpdatePolicy(
        Guid policyId,
        [FromBody] UpdateSecurityPolicyRequest request,
        CancellationToken cancellationToken)
    {
        var policy = await _policyService.UpdatePolicyAsync(
            _tenantContext.TenantId,
            policyId,
            new UpdatePolicyRequest
            {
                Name = request.Name,
                Expression = request.Expression,
                IsActive = request.IsActive,
                Description = request.Description
            },
            cancellationToken);

        return Ok(MapToResponse(policy));
    }

    /// <summary>
    /// Deletes a security policy.
    /// </summary>
    [HttpDelete("policies/{policyId:guid}")]
    [Authorize(Roles = "service,admin")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> DeletePolicy(Guid policyId, CancellationToken cancellationToken)
    {
        await _policyService.DeletePolicyAsync(_tenantContext.TenantId, policyId, cancellationToken);
        return NoContent();
    }

    #endregion

    #region Mapping Helpers

    private static ApiKeyResponse MapToResponse(ApiKey key) => new()
    {
        Id = key.Id,
        Name = key.Name,
        KeyType = key.KeyType,
        KeyPrefix = key.KeyPrefix,
        Description = key.Description,
        IsActive = key.IsActive,
        CreatedAt = key.CreatedAt,
        ExpiresAt = key.ExpiresAt,
        LastUsedAt = key.LastUsedAt
    };

    private static SecurityPolicyResponse MapToResponse(SecurityPolicy policy) => new()
    {
        Id = policy.Id,
        Name = policy.Name,
        TableId = policy.TableId,
        PolicyType = policy.PolicyType,
        Expression = policy.Expression,
        Description = policy.Description,
        IsActive = policy.IsActive,
        OrdinalPosition = policy.OrdinalPosition,
        CreatedAt = policy.CreatedAt,
        UpdatedAt = policy.UpdatedAt
    };

    #endregion
}

#region Request/Response DTOs

/// <summary>
/// Request to create an API key.
/// </summary>
public sealed class CreateApiKeyRequest
{
    /// <summary>
    /// Display name for the key.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Key type (anon or service).
    /// </summary>
    public ApiKeyType KeyType { get; set; }

    /// <summary>
    /// Optional description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Optional expiration date.
    /// </summary>
    public DateTimeOffset? ExpiresAt { get; set; }
}

/// <summary>
/// API key information (without the raw key).
/// </summary>
public sealed class ApiKeyResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public ApiKeyType KeyType { get; set; }
    public string KeyPrefix { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }
}

/// <summary>
/// Response when creating an API key (includes raw key).
/// </summary>
public sealed class CreateApiKeyResponse
{
    /// <summary>
    /// The API key information.
    /// </summary>
    public ApiKeyResponse Key { get; set; } = null!;

    /// <summary>
    /// The raw API key value (only shown once).
    /// </summary>
    public string RawKey { get; set; } = string.Empty;
}

/// <summary>
/// Request to create a security policy.
/// </summary>
public sealed class CreateSecurityPolicyRequest
{
    /// <summary>
    /// Policy name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Table name this policy applies to.
    /// </summary>
    public string TableName { get; set; } = string.Empty;

    /// <summary>
    /// Policy type (SELECT, INSERT, UPDATE, DELETE, ALL).
    /// </summary>
    public PolicyType PolicyType { get; set; }

    /// <summary>
    /// Policy expression with placeholders like {{user_id}}, {{role}}.
    /// </summary>
    public string Expression { get; set; } = string.Empty;

    /// <summary>
    /// Optional description.
    /// </summary>
    public string? Description { get; set; }
}

/// <summary>
/// Request to update a security policy.
/// </summary>
public sealed class UpdateSecurityPolicyRequest
{
    public string? Name { get; set; }
    public string? Expression { get; set; }
    public bool? IsActive { get; set; }
    public string? Description { get; set; }
}

/// <summary>
/// Security policy response.
/// </summary>
public sealed class SecurityPolicyResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid TableId { get; set; }
    public PolicyType PolicyType { get; set; }
    public string Expression { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; }
    public int OrdinalPosition { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

#endregion
