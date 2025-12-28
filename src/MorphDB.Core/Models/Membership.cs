namespace MorphDB.Core.Models;

/// <summary>
/// Represents a user's membership in an organization with a specific role.
/// </summary>
public sealed class OrganizationMember
{
    /// <summary>
    /// Unique identifier for this membership.
    /// </summary>
    public Guid MemberId { get; init; }

    /// <summary>
    /// The organization this membership belongs to.
    /// </summary>
    public Guid OrganizationId { get; init; }

    /// <summary>
    /// The user ID (from authentication system).
    /// </summary>
    public required string UserId { get; init; }

    /// <summary>
    /// User's email address.
    /// </summary>
    public required string Email { get; init; }

    /// <summary>
    /// User's display name.
    /// </summary>
    public string? DisplayName { get; init; }

    /// <summary>
    /// Role within the organization.
    /// </summary>
    public OrganizationRole Role { get; init; } = OrganizationRole.Member;

    /// <summary>
    /// Membership status.
    /// </summary>
    public MembershipStatus Status { get; init; } = MembershipStatus.Active;

    /// <summary>
    /// When the user joined the organization.
    /// </summary>
    public DateTimeOffset JoinedAt { get; init; }

    /// <summary>
    /// When the membership was last updated.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; init; }

    /// <summary>
    /// Who invited this member (user ID).
    /// </summary>
    public string? InvitedBy { get; init; }
}

/// <summary>
/// Represents a user's membership in a project with a specific role.
/// </summary>
public sealed class ProjectMember
{
    /// <summary>
    /// Unique identifier for this membership.
    /// </summary>
    public Guid MemberId { get; init; }

    /// <summary>
    /// The project this membership belongs to.
    /// </summary>
    public Guid ProjectId { get; init; }

    /// <summary>
    /// The user ID (from authentication system).
    /// </summary>
    public required string UserId { get; init; }

    /// <summary>
    /// User's email address.
    /// </summary>
    public required string Email { get; init; }

    /// <summary>
    /// User's display name.
    /// </summary>
    public string? DisplayName { get; init; }

    /// <summary>
    /// Role within the project.
    /// </summary>
    public ProjectRole Role { get; init; } = ProjectRole.Viewer;

    /// <summary>
    /// Membership status.
    /// </summary>
    public MembershipStatus Status { get; init; } = MembershipStatus.Active;

    /// <summary>
    /// When the user was added to the project.
    /// </summary>
    public DateTimeOffset JoinedAt { get; init; }

    /// <summary>
    /// When the membership was last updated.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>
/// Roles within an organization.
/// Higher values indicate more permissions.
/// </summary>
public enum OrganizationRole
{
    /// <summary>
    /// Basic member with access only to assigned projects.
    /// </summary>
    Member = 10,

    /// <summary>
    /// Administrator with member management and project creation rights.
    /// </summary>
    Admin = 50,

    /// <summary>
    /// Owner with full control including organization deletion.
    /// </summary>
    Owner = 100
}

/// <summary>
/// Roles within a project.
/// Higher values indicate more permissions.
/// </summary>
public enum ProjectRole
{
    /// <summary>
    /// Read-only access to project data.
    /// </summary>
    Viewer = 10,

    /// <summary>
    /// Read/write access to project data and schema.
    /// </summary>
    Developer = 50,

    /// <summary>
    /// Full project control including settings and member management.
    /// </summary>
    Admin = 100
}

/// <summary>
/// Membership status.
/// </summary>
public enum MembershipStatus
{
    /// <summary>
    /// Invitation sent, pending acceptance.
    /// </summary>
    Pending = 0,

    /// <summary>
    /// Active membership.
    /// </summary>
    Active = 1,

    /// <summary>
    /// Membership suspended.
    /// </summary>
    Suspended = 2,

    /// <summary>
    /// Membership removed.
    /// </summary>
    Removed = 3
}

/// <summary>
/// Represents an invitation to join an organization.
/// </summary>
public sealed class OrganizationInvitation
{
    /// <summary>
    /// Unique invitation ID.
    /// </summary>
    public Guid InvitationId { get; init; }

    /// <summary>
    /// The organization being invited to.
    /// </summary>
    public Guid OrganizationId { get; init; }

    /// <summary>
    /// Email address of the invitee.
    /// </summary>
    public required string Email { get; init; }

    /// <summary>
    /// Role to assign upon acceptance.
    /// </summary>
    public OrganizationRole Role { get; init; }

    /// <summary>
    /// Invitation token for verification.
    /// </summary>
    public required string Token { get; init; }

    /// <summary>
    /// Invitation status.
    /// </summary>
    public InvitationStatus Status { get; init; } = InvitationStatus.Pending;

    /// <summary>
    /// When the invitation was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// When the invitation expires.
    /// </summary>
    public DateTimeOffset ExpiresAt { get; init; }

    /// <summary>
    /// Who sent the invitation.
    /// </summary>
    public required string InvitedBy { get; init; }
}

/// <summary>
/// Invitation status.
/// </summary>
public enum InvitationStatus
{
    /// <summary>
    /// Invitation pending acceptance.
    /// </summary>
    Pending = 0,

    /// <summary>
    /// Invitation accepted.
    /// </summary>
    Accepted = 1,

    /// <summary>
    /// Invitation declined.
    /// </summary>
    Declined = 2,

    /// <summary>
    /// Invitation expired.
    /// </summary>
    Expired = 3,

    /// <summary>
    /// Invitation revoked by sender.
    /// </summary>
    Revoked = 4
}
