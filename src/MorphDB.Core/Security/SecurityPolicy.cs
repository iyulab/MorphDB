namespace MorphDB.Core.Security;

/// <summary>
/// Represents the type of security policy.
/// </summary>
public enum PolicyType
{
    /// <summary>
    /// Policy applies to SELECT operations.
    /// </summary>
    Select = 0,

    /// <summary>
    /// Policy applies to INSERT operations.
    /// </summary>
    Insert = 1,

    /// <summary>
    /// Policy applies to UPDATE operations.
    /// </summary>
    Update = 2,

    /// <summary>
    /// Policy applies to DELETE operations.
    /// </summary>
    Delete = 3,

    /// <summary>
    /// Policy applies to all operations.
    /// </summary>
    All = 4
}

/// <summary>
/// Represents a Row-Level Security (RLS) policy.
/// </summary>
public sealed class SecurityPolicy
{
    /// <summary>
    /// Gets or sets the policy ID.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the project ID this policy belongs to.
    /// </summary>
    public Guid ProjectId { get; set; }

    /// <summary>
    /// Gets or sets the table ID this policy applies to.
    /// </summary>
    public Guid TableId { get; set; }

    /// <summary>
    /// Gets or sets the policy name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a description for this policy.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the policy type (SELECT, INSERT, UPDATE, DELETE, ALL).
    /// </summary>
    public PolicyType PolicyType { get; set; }

    /// <summary>
    /// Gets or sets the policy expression (SQL boolean expression).
    /// Uses placeholders like {{user_id}}, {{role}}, {{claims.xxx}}.
    /// Example: "user_id = {{user_id}}" or "{{role}} = 'admin'"
    /// </summary>
    public string Expression { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets whether the policy is active.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Gets or sets the ordinal position for policy evaluation order.
    /// </summary>
    public int OrdinalPosition { get; set; }

    /// <summary>
    /// Gets or sets when the policy was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets when the policy was last updated.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// Request to create a security policy.
/// </summary>
public sealed class CreatePolicyRequest
{
    /// <summary>
    /// Gets or sets the policy name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the table name this policy applies to.
    /// </summary>
    public string TableName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the policy type.
    /// </summary>
    public PolicyType PolicyType { get; set; }

    /// <summary>
    /// Gets or sets the policy expression.
    /// </summary>
    public string Expression { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a description for this policy.
    /// </summary>
    public string? Description { get; set; }
}

/// <summary>
/// Request to update a security policy.
/// </summary>
public sealed class UpdatePolicyRequest
{
    /// <summary>
    /// Gets or sets the new policy name.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the new policy expression.
    /// </summary>
    public string? Expression { get; set; }

    /// <summary>
    /// Gets or sets whether the policy is active.
    /// </summary>
    public bool? IsActive { get; set; }

    /// <summary>
    /// Gets or sets a new description for this policy.
    /// </summary>
    public string? Description { get; set; }
}
