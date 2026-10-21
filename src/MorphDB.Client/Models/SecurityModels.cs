using System.Text.Json.Serialization;

namespace MorphDB.Client.Models;

/// <summary>
/// The operations a row-level security policy applies to. Travels as its name (<c>"Select"</c>), as the
/// service writes it.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<PolicyType>))]
public enum PolicyType
{
    /// <summary>Reads.</summary>
    Select = 0,

    /// <summary>Inserts.</summary>
    Insert = 1,

    /// <summary>Updates.</summary>
    Update = 2,

    /// <summary>Deletes.</summary>
    Delete = 3,

    /// <summary>Every operation.</summary>
    All = 4,
}

/// <summary>
/// A row-level security policy on a table.
/// </summary>
public sealed class SecurityPolicyInfo
{
    public Guid Id { get; init; }
    public required string Name { get; init; }
    public Guid TableId { get; init; }
    public PolicyType PolicyType { get; init; }

    /// <summary>The condition a row must meet, with placeholders such as <c>{{user_id}}</c> and <c>{{role}}</c>.</summary>
    public required string Expression { get; init; }
    public string? Description { get; init; }
    public bool IsActive { get; init; }
    public int OrdinalPosition { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>
/// Request to create a row-level security policy (mirrors the server's <c>CreateSecurityPolicyRequest</c>).
/// </summary>
public sealed class CreateSecurityPolicyRequest
{
    public required string Name { get; init; }

    /// <summary>The table the policy applies to.</summary>
    public required string TableName { get; init; }
    public PolicyType PolicyType { get; init; }

    /// <summary>The condition a row must meet, with placeholders such as <c>{{user_id}}</c> and <c>{{role}}</c>.</summary>
    public required string Expression { get; init; }
    public string? Description { get; init; }
}

/// <summary>
/// Changes to a row-level security policy. Members left null are left as they are.
/// </summary>
public sealed class UpdateSecurityPolicyRequest
{
    public string? Name { get; init; }
    public string? Expression { get; init; }

    /// <summary>Turns the policy on or off without deleting it.</summary>
    public bool? IsActive { get; init; }
    public string? Description { get; init; }
}

/// <summary>
/// Request to issue a secret (mirrors the server's <c>IssueSecretApiRequest</c>).
/// </summary>
public sealed class IssueSecretRequest
{
    /// <summary>A label for people. Authorizes nothing.</summary>
    public required string Name { get; init; }

    /// <summary>The role the secret carries. The reserved roles (<c>master</c>, <c>service</c>) are refused.</summary>
    public required string Role { get; init; }

    /// <summary>The one project the secret is confined to, or null for every project.</summary>
    public Guid? ProjectId { get; init; }
}

/// <summary>
/// A secret just issued — the only time its plaintext is returned.
/// </summary>
public sealed class IssuedSecret
{
    public Guid SecretId { get; init; }
    public required string Name { get; init; }
    public required string Role { get; init; }
    public Guid? ProjectId { get; init; }
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>The plaintext. Store it now; the service keeps only its hash.</summary>
    public required string Secret { get; init; }
}

/// <summary>
/// An issued secret as listed. Carries no credential material.
/// </summary>
public sealed class SecretInfo
{
    public Guid SecretId { get; init; }
    public required string Name { get; init; }
    public required string Role { get; init; }
    public Guid? ProjectId { get; init; }
    public bool IsActive { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? RevokedAt { get; init; }
}
