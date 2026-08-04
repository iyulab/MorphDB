namespace MorphDB.Core.Security;

/// <summary>
/// Issues, lists, revokes and authenticates connection secrets.
/// </summary>
public interface ISecretService
{
    /// <summary>
    /// Issues a secret and returns its plaintext exactly once.
    /// </summary>
    /// <exception cref="Exceptions.ValidationException">
    /// The role is one this layer reserves for itself (<see cref="SecretRoles.Reserved"/>).
    /// </exception>
    Task<IssuedSecret> IssueAsync(IssueSecretRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists issued secrets. Hashes are not returned.
    /// </summary>
    Task<IReadOnlyList<Secret>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes a secret. Returns false when no active secret has that id.
    /// </summary>
    Task<bool> RevokeAsync(Guid secretId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves a presented plaintext to the secret it authenticates, or <c>null</c>.
    /// <para>
    /// The injected master secret is answered without a database read: it is compared against
    /// configuration and is never stored, so there is nothing to look up.
    /// </para>
    /// </summary>
    Task<Secret?> AuthenticateAsync(string presented, CancellationToken cancellationToken = default);
}

/// <summary>
/// Configuration for connection secrets, bound from the <c>Security</c> configuration section.
/// </summary>
public sealed class SecretOptions
{
    /// <summary>
    /// The configuration section this binds from.
    /// </summary>
    public const string SectionName = "Security";

    /// <summary>
    /// Gets or sets the master secret, injected at start-up (<c>Security__MasterSecret</c>).
    /// <para>
    /// Its presence is what turns authentication on — see <see cref="IsEnforced"/>. It is supplied
    /// the way PostgreSQL is supplied <c>POSTGRES_PASSWORD</c>: by the deployment, before anything
    /// can ask for it. No API issues it, and it is never written to the database.
    /// </para>
    /// </summary>
    public string? MasterSecret { get; set; }

    /// <summary>
    /// Gets whether the service authenticates requests.
    /// <para>
    /// Authentication is opt-in on the master secret being injected, so a deployment that supplies
    /// nothing behaves exactly as it did before secrets existed. This is deliberate and it is
    /// documented as such: an installation that advertises a boundary it does not enforce is worse
    /// than one that states plainly that it enforces none.
    /// </para>
    /// </summary>
    public bool IsEnforced => !string.IsNullOrWhiteSpace(MasterSecret);
}
