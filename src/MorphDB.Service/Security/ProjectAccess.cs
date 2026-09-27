using MorphDB.Core.Exceptions;
using MorphDB.Core.Security;

namespace MorphDB.Service.Security;

/// <summary>
/// Decides what a secret may do on the routes that address a project by its path rather than by the
/// <c>X-Project-Id</c> header.
/// <para>
/// <see cref="SecretAuthenticationMiddleware"/> holds a confined secret to the project its header
/// names, and nowhere else — so a route that takes the project from its path (<c>/api/projects/{id}</c>,
/// its audit log) was addressed with the header set to the secret's own project and answered for any
/// other. These routes ask here instead. Creating, changing and deleting a project is administration,
/// not data access, and takes the master secret, as issuing secrets does; reading a project a confined
/// secret is not confined to is refused the way the header check refuses it.
/// </para>
/// <para>
/// With no master secret injected the service authenticates nothing and every check here passes —
/// the default deployment answers exactly as it did before.
/// </para>
/// </summary>
public sealed class ProjectAccess
{
    private readonly SecretOptions _options;
    private readonly ISecurityContextAccessor _securityContext;

    public ProjectAccess(SecretOptions options, ISecurityContextAccessor securityContext)
    {
        _options = options;
        _securityContext = securityContext;
    }

    /// <summary>The project the calling secret is confined to, or null when it is not confined (or nothing is enforced).</summary>
    public Guid? ConfinedTo => _options.IsEnforced ? _securityContext.ContextOrNull?.ConfinedToProjectId : null;

    /// <summary>Refuses anything but the master secret while secrets are enforced.</summary>
    public void RequireMaster()
    {
        if (!_options.IsEnforced)
        {
            return;
        }

        if (!string.Equals(_securityContext.ContextOrNull?.Role, SecretRoles.Master, StringComparison.Ordinal))
        {
            throw new ForbiddenException("Only the master secret may create, change or delete projects.");
        }
    }

    /// <summary>Refuses a confined secret addressing a project other than its own.</summary>
    public void RequireAccessTo(Guid projectId)
    {
        if (ConfinedTo is { } confinedTo && confinedTo != projectId)
        {
            throw new ForbiddenException("This secret is confined to a different project.");
        }
    }
}
