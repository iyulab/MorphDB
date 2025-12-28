using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MorphDB.Core.Encryption;
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
    private readonly IKeyRotationService? _keyRotationService;
    private readonly ITenantContextAccessor _tenantContext;

    public SecurityController(
        IApiKeyService apiKeyService,
        ISecurityPolicyService policyService,
        ITenantContextAccessor tenantContext,
        IKeyRotationService? keyRotationService = null)
    {
        _apiKeyService = apiKeyService;
        _policyService = policyService;
        _tenantContext = tenantContext;
        _keyRotationService = keyRotationService;
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

    #region Encryption Key Rotation

    /// <summary>
    /// Rotates encryption keys for a specific table.
    /// Re-encrypts all encrypted data with the current key version.
    /// </summary>
    [HttpPost("encryption/rotate/{tableName}")]
    [Authorize(Roles = "service,admin")]
    [ProducesResponseType<KeyRotationResultResponse>(200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(503)]
    public async Task<IActionResult> RotateTableKey(
        string tableName,
        CancellationToken cancellationToken)
    {
        if (_keyRotationService is null)
        {
            return StatusCode(503, new { message = "Encryption is not enabled" });
        }

        var result = await _keyRotationService.RotateTableKeyAsync(
            _tenantContext.TenantId,
            tableName,
            cancellationToken);

        return Ok(MapToResponse(result));
    }

    /// <summary>
    /// Rotates encryption keys for all tables of the current tenant.
    /// </summary>
    [HttpPost("encryption/rotate")]
    [Authorize(Roles = "service,admin")]
    [ProducesResponseType<KeyRotationResultResponse>(200)]
    [ProducesResponseType(503)]
    public async Task<IActionResult> RotateTenantKeys(CancellationToken cancellationToken)
    {
        if (_keyRotationService is null)
        {
            return StatusCode(503, new { message = "Encryption is not enabled" });
        }

        var result = await _keyRotationService.RotateTenantKeysAsync(
            _tenantContext.TenantId,
            cancellationToken);

        return Ok(MapToResponse(result));
    }

    /// <summary>
    /// Gets the current key rotation status for a table.
    /// </summary>
    [HttpGet("encryption/status/{tableName}")]
    [Authorize(Roles = "service,admin")]
    [ProducesResponseType<KeyRotationStatusResponse>(200)]
    [ProducesResponseType(503)]
    public async Task<IActionResult> GetRotationStatus(
        string tableName,
        CancellationToken cancellationToken)
    {
        if (_keyRotationService is null)
        {
            return StatusCode(503, new { message = "Encryption is not enabled" });
        }

        var status = await _keyRotationService.GetRotationStatusAsync(
            _tenantContext.TenantId,
            tableName,
            cancellationToken);

        return Ok(MapToResponse(status));
    }

    /// <summary>
    /// Validates encryption status for a table.
    /// Checks if all data is encrypted with the current key version.
    /// </summary>
    [HttpGet("encryption/validate/{tableName}")]
    [Authorize(Roles = "service,admin")]
    [ProducesResponseType<KeyValidationResultResponse>(200)]
    [ProducesResponseType(503)]
    public async Task<IActionResult> ValidateEncryption(
        string tableName,
        CancellationToken cancellationToken)
    {
        if (_keyRotationService is null)
        {
            return StatusCode(503, new { message = "Encryption is not enabled" });
        }

        var result = await _keyRotationService.ValidateEncryptionAsync(
            _tenantContext.TenantId,
            tableName,
            cancellationToken);

        return Ok(MapToResponse(result));
    }

    /// <summary>
    /// Gets encryption configuration info (key version, status).
    /// </summary>
    [HttpGet("encryption/info")]
    [Authorize(Roles = "service,admin")]
    [ProducesResponseType<EncryptionInfoResponse>(200)]
    [ProducesResponseType(503)]
    public IActionResult GetEncryptionInfo()
    {
        if (_keyRotationService is null)
        {
            return StatusCode(503, new { message = "Encryption is not enabled" });
        }

        return Ok(new EncryptionInfoResponse
        {
            Enabled = true,
            CurrentKeyVersion = _keyRotationService.CurrentKeyVersion,
            AvailableKeyVersions = _keyRotationService.AvailableKeyVersions.ToList()
        });
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

    private static KeyRotationResultResponse MapToResponse(KeyRotationResult result) => new()
    {
        Success = result.Success,
        TableName = result.TableName,
        PreviousKeyVersion = result.PreviousKeyVersion,
        NewKeyVersion = result.NewKeyVersion,
        RowsProcessed = result.RowsProcessed,
        ColumnsRotated = result.ColumnsRotated,
        DurationMs = (long)result.Duration.TotalMilliseconds,
        ErrorMessage = result.ErrorMessage,
        StartedAt = result.StartedAt,
        CompletedAt = result.CompletedAt
    };

    private static KeyRotationStatusResponse MapToResponse(KeyRotationStatus status) => new()
    {
        State = status.State.ToString(),
        TableName = status.TableName,
        CurrentKeyVersion = status.CurrentKeyVersion,
        TargetKeyVersion = status.TargetKeyVersion,
        ProgressPercent = status.ProgressPercent,
        RowsProcessed = status.RowsProcessed,
        TotalRows = status.TotalRows,
        EstimatedTimeRemainingMs = status.EstimatedTimeRemaining.HasValue
            ? (long)status.EstimatedTimeRemaining.Value.TotalMilliseconds
            : null,
        StartedAt = status.StartedAt,
        LastRotatedAt = status.LastRotatedAt
    };

    private static KeyValidationResultResponse MapToResponse(KeyValidationResult result) => new()
    {
        IsValid = result.IsValid,
        TableName = result.TableName,
        ExpectedKeyVersion = result.ExpectedKeyVersion,
        TotalEncryptedValues = result.TotalEncryptedValues,
        CurrentVersionCount = result.CurrentVersionCount,
        OldVersionCount = result.OldVersionCount,
        UnencryptedCount = result.UnencryptedCount,
        VersionBreakdown = result.VersionBreakdown.ToDictionary(kv => kv.Key, kv => kv.Value)
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

/// <summary>
/// Response for key rotation operation.
/// </summary>
public sealed class KeyRotationResultResponse
{
    public bool Success { get; set; }
    public string TableName { get; set; } = string.Empty;
    public int PreviousKeyVersion { get; set; }
    public int NewKeyVersion { get; set; }
    public long RowsProcessed { get; set; }
    public int ColumnsRotated { get; set; }
    public long DurationMs { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset CompletedAt { get; set; }
}

/// <summary>
/// Response for key rotation status.
/// </summary>
public sealed class KeyRotationStatusResponse
{
    public string State { get; set; } = string.Empty;
    public string TableName { get; set; } = string.Empty;
    public int CurrentKeyVersion { get; set; }
    public int? TargetKeyVersion { get; set; }
    public double ProgressPercent { get; set; }
    public long RowsProcessed { get; set; }
    public long TotalRows { get; set; }
    public long? EstimatedTimeRemainingMs { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? LastRotatedAt { get; set; }
}

/// <summary>
/// Response for encryption validation.
/// </summary>
public sealed class KeyValidationResultResponse
{
    public bool IsValid { get; set; }
    public string TableName { get; set; } = string.Empty;
    public int ExpectedKeyVersion { get; set; }
    public long TotalEncryptedValues { get; set; }
    public long CurrentVersionCount { get; set; }
    public long OldVersionCount { get; set; }
    public long UnencryptedCount { get; set; }
    public Dictionary<int, long> VersionBreakdown { get; set; } = new();
}

/// <summary>
/// Response for encryption info.
/// </summary>
public sealed class EncryptionInfoResponse
{
    public bool Enabled { get; set; }
    public int CurrentKeyVersion { get; set; }
    public List<int> AvailableKeyVersions { get; set; } = new();
}

#endregion
