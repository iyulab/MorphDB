namespace MorphDB.Service.Security;

/// <summary>
/// Accepts people's access tokens from an issuer the deployment trusts, so a request can run under
/// row-level security as the person it acts for rather than as a shared secret.
/// <para>
/// MorphDB issues no tokens and keeps no users. The issuer is the deployment's own identity
/// provider; MorphDB only checks what it presents — signature, issuer, audience, lifetime — and
/// carries the token's subject, role and claims into the policies (<c>{{user_id}}</c>,
/// <c>{{role}}</c>, <c>{{claims.x}}</c>). This is the position PostgREST takes with its JWT
/// authentication, and the one PostgreSQL 18 takes with an OAuth validator.
/// </para>
/// <para>
/// Bound from <c>Security:Tokens</c> (<c>Security__Tokens__Issuer</c>, …). Setting
/// <see cref="Issuer"/> turns it on, and it then requires the master secret: a token would mean
/// nothing while an anonymous request may do anything, so start-up refuses that combination.
/// </para>
/// </summary>
public sealed class TokenOptions
{
    /// <summary>The configuration section this binds from.</summary>
    public const string SectionName = "Security:Tokens";

    /// <summary>
    /// Gets or sets the issuer whose tokens are accepted — the <c>iss</c> a token must carry, and,
    /// unless <see cref="MetadataAddress"/> says otherwise, where its signing keys are discovered
    /// (<c>{Issuer}/.well-known/openid-configuration</c>).
    /// </summary>
    public string? Issuer { get; set; }

    /// <summary>
    /// Gets or sets the audience a token must name in <c>aud</c>. Required: a token minted for some
    /// other service is not a credential here.
    /// </summary>
    public string? Audience { get; set; }

    /// <summary>Gets or sets the discovery document's address, when it is not under <see cref="Issuer"/>.</summary>
    public string? MetadataAddress { get; set; }

    /// <summary>Gets or sets whether the discovery document must be fetched over HTTPS. Default true.</summary>
    public bool RequireHttpsMetadata { get; set; } = true;

    /// <summary>
    /// Gets or sets the claim that names the caller's role — what <c>{{role}}</c> becomes. Default
    /// <c>role</c>. A token without it runs with no role. The reserved roles (<c>master</c>,
    /// <c>service</c>) are refused: a token never bypasses row-level security.
    /// </summary>
    public string RoleClaim { get; set; } = "role";

    /// <summary>Gets whether tokens are accepted at all.</summary>
    public bool IsEnabled => !string.IsNullOrWhiteSpace(Issuer);
}
