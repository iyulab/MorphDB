namespace MorphDB.Core.Security;

/// <summary>
/// A connection principal: the thing that opens a connection to this database.
/// <para>
/// A relational database identifies the opener of a connection with a user and a password. This
/// layer has no connection to open — every access is an API call — so the same position is held by
/// a secret. A secret is not a person: it has no email, no invitation and no organization, and it
/// is issued and revoked rather than registered. What a secret carries is a <see cref="Role"/>,
/// which row-level security policies read through the <c>{{role}}</c> placeholder.
/// </para>
/// <para>
/// The plaintext exists exactly once, in the response that issues it. Only <see cref="SecretHash"/>
/// is stored, so a leaked control plane does not leak usable credentials.
/// </para>
/// </summary>
public sealed class Secret
{
    /// <summary>
    /// Gets or sets the secret's identifier. This is not the credential — it names the row so it
    /// can be listed and revoked without ever handling the plaintext again.
    /// </summary>
    public Guid SecretId { get; set; }

    /// <summary>
    /// Gets or sets the operator-supplied label. Purely descriptive; it authorizes nothing.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the SHA-256 hash of the plaintext, lowercase hex.
    /// </summary>
    public string SecretHash { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the role this secret carries. Free-form on purpose: naming the roles a
    /// database may have would assume what the database is used for. Meaning is given by the
    /// security policies that reference it, not by this layer.
    /// </summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the project this secret is confined to, or <c>null</c> for every project.
    /// </summary>
    public Guid? ProjectId { get; set; }

    /// <summary>
    /// Gets or sets whether the secret still authenticates. Revocation sets this false rather than
    /// deleting the row, so an audit trail keeps a name for the secret it recorded.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Gets or sets when the secret was issued.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets when the secret was revoked, if it was.
    /// </summary>
    public DateTimeOffset? RevokedAt { get; set; }
}

/// <summary>
/// The role names this layer reserves for itself.
/// </summary>
public static class SecretRoles
{
    /// <summary>
    /// The role carried by the injected master secret. It bypasses row-level security and is the
    /// only role that may issue and revoke secrets.
    /// </summary>
    public const string Master = "master";

    /// <summary>
    /// The role carried by <see cref="SecurityContext.Service"/> — trusted in-process callers.
    /// </summary>
    public const string Service = "service";

    /// <summary>
    /// Roles that may never be issued through the API.
    /// <para>
    /// This is the invariant that keeps the bootstrap acyclic. The master secret exists only
    /// because a deployment injected it at start-up; if a route could mint another one, the
    /// privilege would have an in-band origin again — which is half of what made the previous
    /// authentication machinery unenforceable and got it removed.
    /// </para>
    /// </summary>
    public static readonly IReadOnlySet<string> Reserved =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Master, Service };

    /// <summary>
    /// Returns whether the given role may be issued through the API.
    /// </summary>
    public static bool IsIssuable(string role) => !Reserved.Contains(role);
}

/// <summary>
/// The request to issue a secret.
/// </summary>
public sealed class IssueSecretRequest
{
    /// <summary>
    /// Gets or sets the descriptive label.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the role to carry. Must not be a <see cref="SecretRoles.Reserved"/> role.
    /// </summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the project to confine the secret to, or <c>null</c> for every project.
    /// </summary>
    public Guid? ProjectId { get; set; }
}

/// <summary>
/// The result of issuing a secret — the only time the plaintext is ever available.
/// </summary>
public sealed class IssuedSecret
{
    /// <summary>
    /// Gets or sets the stored record.
    /// </summary>
    public Secret Secret { get; set; } = new();

    /// <summary>
    /// Gets or sets the plaintext. Not stored anywhere; if it is lost, issue another one.
    /// </summary>
    public string Plaintext { get; set; } = string.Empty;
}
