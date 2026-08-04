using Microsoft.AspNetCore.Mvc;
using MorphDB.Core.Exceptions;
using MorphDB.Core.Security;

namespace MorphDB.Service.Controllers;

/// <summary>
/// Issues, lists and revokes connection secrets.
/// <para>
/// Every route here requires the master secret. That is what keeps the bootstrap acyclic: the
/// master secret comes from the deployment at start-up, so the authority to issue credentials never
/// originates inside the API. An issued secret — whatever role it carries — cannot reach these
/// routes, and no route can mint a reserved role.
/// </para>
/// </summary>
[ApiController]
[Route("api/security/secrets")]
public sealed class SecretsController : ControllerBase
{
    private readonly ISecretService _secrets;
    private readonly SecretOptions _options;
    private readonly ISecurityContextAccessor _securityContext;

    public SecretsController(
        ISecretService secrets,
        SecretOptions options,
        ISecurityContextAccessor securityContext)
    {
        _secrets = secrets;
        _options = options;
        _securityContext = securityContext;
    }

    /// <summary>
    /// Issues a secret. The plaintext is in the response and is never retrievable again.
    /// </summary>
    [HttpPost]
    [ProducesResponseType<IssuedSecretResponse>(201)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    [ProducesResponseType(503)]
    public async Task<IActionResult> Issue(
        [FromBody] IssueSecretApiRequest request,
        CancellationToken cancellationToken)
    {
        if (GuardMaster() is { } refusal)
        {
            return refusal;
        }

        var issued = await _secrets.IssueAsync(
            new IssueSecretRequest
            {
                Name = request.Name,
                Role = request.Role,
                ProjectId = request.ProjectId
            },
            cancellationToken);

        return CreatedAtAction(nameof(List), new IssuedSecretResponse
        {
            SecretId = issued.Secret.SecretId,
            Name = issued.Secret.Name,
            Role = issued.Secret.Role,
            ProjectId = issued.Secret.ProjectId,
            CreatedAt = issued.Secret.CreatedAt,
            Secret = issued.Plaintext
        });
    }

    /// <summary>
    /// Lists issued secrets. Never returns hashes or plaintexts.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<SecretResponse>>(200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    [ProducesResponseType(503)]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        if (GuardMaster() is { } refusal)
        {
            return refusal;
        }

        var secrets = await _secrets.ListAsync(cancellationToken);

        return Ok(secrets.Select(s => new SecretResponse
        {
            SecretId = s.SecretId,
            Name = s.Name,
            Role = s.Role,
            ProjectId = s.ProjectId,
            IsActive = s.IsActive,
            CreatedAt = s.CreatedAt,
            RevokedAt = s.RevokedAt
        }).ToList());
    }

    /// <summary>
    /// Revokes a secret. The row is kept so audit records keep a name for it.
    /// </summary>
    [HttpDelete("{secretId:guid}")]
    [ProducesResponseType(204)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    [ProducesResponseType(503)]
    public async Task<IActionResult> Revoke(Guid secretId, CancellationToken cancellationToken)
    {
        if (GuardMaster() is { } refusal)
        {
            return refusal;
        }

        var revoked = await _secrets.RevokeAsync(secretId, cancellationToken);
        return revoked ? NoContent() : NotFound();
    }

    /// <summary>
    /// Returns a refusal when the caller may not manage secrets, or null when it may.
    /// </summary>
    private ObjectResult? GuardMaster()
    {
        // Without an injected master secret nothing authenticates, so there is no caller these
        // routes could tell apart. Answering 503 states that plainly instead of quietly issuing
        // credentials to anyone who can reach the port.
        if (!_options.IsEnforced)
        {
            return StatusCode(503, new
            {
                error = "Unavailable",
                code = "SECRETS_NOT_CONFIGURED",
                message = "Secrets require a master secret injected at start-up (Security__MasterSecret)."
            });
        }

        var role = _securityContext.ContextOrNull?.Role;
        if (!string.Equals(role, SecretRoles.Master, StringComparison.Ordinal))
        {
            throw new ForbiddenException("Only the master secret may manage secrets.");
        }

        return null;
    }
}

/// <summary>
/// Request body for issuing a secret.
/// </summary>
public sealed class IssueSecretApiRequest
{
    /// <summary>
    /// Descriptive label. Authorizes nothing.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The role the secret carries. Reserved roles (<c>master</c>, <c>service</c>) are refused.
    /// </summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>
    /// The project to confine the secret to, or null for every project.
    /// </summary>
    public Guid? ProjectId { get; set; }
}

/// <summary>
/// The response to issuing a secret — the only place the plaintext appears.
/// </summary>
public sealed class IssuedSecretResponse
{
    public Guid SecretId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public Guid? ProjectId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// The plaintext. Store it now; it is not recoverable.
    /// </summary>
    public string Secret { get; set; } = string.Empty;
}

/// <summary>
/// A listed secret. Carries no credential material.
/// </summary>
public sealed class SecretResponse
{
    public Guid SecretId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public Guid? ProjectId { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}
