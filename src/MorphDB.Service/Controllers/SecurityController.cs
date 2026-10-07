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
    private readonly ISecurityPolicyService _policyService;
    private readonly IProjectContextAccessor _projectContext;

    public SecurityController(
        ISecurityPolicyService policyService,
        IProjectContextAccessor projectContext)
    {
        _policyService = policyService;
        _projectContext = projectContext;
    }

    #region Security Policies (RLS)

    /// <summary>
    /// Gets all security policies for a table.
    /// </summary>
    [HttpGet("policies/{tableName}")]
    [ProducesResponseType<IReadOnlyList<SecurityPolicyResponse>>(200)]
    public async Task<IActionResult> GetPolicies(string tableName, CancellationToken cancellationToken)
    {
        var policies = await _policyService.GetPoliciesByTableNameAsync(
            _projectContext.ProjectId,
            tableName,
            cancellationToken);

        var response = policies.Select(MapToResponse).ToList();
        return Ok(response);
    }

    /// <summary>
    /// Creates a new security policy.
    /// </summary>
    [HttpPost("policies")]
    [ProducesResponseType<SecurityPolicyResponse>(201)]
    public async Task<IActionResult> CreatePolicy(
        [FromBody] CreateSecurityPolicyRequest request,
        CancellationToken cancellationToken)
    {
        var policy = await _policyService.CreatePolicyAsync(
            _projectContext.ProjectId,
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
    [ProducesResponseType<SecurityPolicyResponse>(200)]
    public async Task<IActionResult> UpdatePolicy(
        Guid policyId,
        [FromBody] UpdateSecurityPolicyRequest request,
        CancellationToken cancellationToken)
    {
        var policy = await _policyService.UpdatePolicyAsync(
            _projectContext.ProjectId,
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
    [ProducesResponseType(204)]
    public async Task<IActionResult> DeletePolicy(Guid policyId, CancellationToken cancellationToken)
    {
        await _policyService.DeletePolicyAsync(_projectContext.ProjectId, policyId, cancellationToken);
        return NoContent();
    }

    #endregion

    #region Mapping Helpers

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
