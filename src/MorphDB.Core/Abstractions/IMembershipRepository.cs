using MorphDB.Core.Models;

namespace MorphDB.Core.Abstractions;

/// <summary>
/// Repository for managing organization and project memberships.
/// </summary>
public interface IMembershipRepository
{
    #region Organization Members

    /// <summary>
    /// Adds a member to an organization.
    /// </summary>
    Task<OrganizationMember> AddOrganizationMemberAsync(
        AddOrganizationMemberRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets an organization member by ID.
    /// </summary>
    Task<OrganizationMember?> GetOrganizationMemberAsync(
        Guid memberId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets an organization member by user ID.
    /// </summary>
    Task<OrganizationMember?> GetOrganizationMemberByUserIdAsync(
        Guid organizationId,
        string userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets an organization member by email.
    /// </summary>
    Task<OrganizationMember?> GetOrganizationMemberByEmailAsync(
        Guid organizationId,
        string email,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists members of an organization.
    /// </summary>
    Task<IReadOnlyList<OrganizationMember>> ListOrganizationMembersAsync(
        Guid organizationId,
        MembershipStatus? status = null,
        int offset = 0,
        int limit = 100,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists organizations a user is a member of.
    /// </summary>
    Task<IReadOnlyList<OrganizationMember>> ListUserOrganizationsAsync(
        string userId,
        MembershipStatus? status = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an organization member's role or status.
    /// </summary>
    Task<OrganizationMember> UpdateOrganizationMemberAsync(
        UpdateOrganizationMemberRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a member from an organization.
    /// </summary>
    Task RemoveOrganizationMemberAsync(
        Guid memberId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts members in an organization.
    /// </summary>
    Task<int> CountOrganizationMembersAsync(
        Guid organizationId,
        MembershipStatus? status = null,
        CancellationToken cancellationToken = default);

    #endregion

    #region Project Members

    /// <summary>
    /// Adds a member to a project.
    /// </summary>
    Task<ProjectMember> AddProjectMemberAsync(
        AddProjectMemberRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a project member by ID.
    /// </summary>
    Task<ProjectMember?> GetProjectMemberAsync(
        Guid memberId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a project member by user ID.
    /// </summary>
    Task<ProjectMember?> GetProjectMemberByUserIdAsync(
        Guid projectId,
        string userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists members of a project.
    /// </summary>
    Task<IReadOnlyList<ProjectMember>> ListProjectMembersAsync(
        Guid projectId,
        MembershipStatus? status = null,
        int offset = 0,
        int limit = 100,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists projects a user is a member of.
    /// </summary>
    Task<IReadOnlyList<ProjectMember>> ListUserProjectsAsync(
        string userId,
        MembershipStatus? status = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates a project member's role or status.
    /// </summary>
    Task<ProjectMember> UpdateProjectMemberAsync(
        UpdateProjectMemberRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a member from a project.
    /// </summary>
    Task RemoveProjectMemberAsync(
        Guid memberId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts members in a project.
    /// </summary>
    Task<int> CountProjectMembersAsync(
        Guid projectId,
        MembershipStatus? status = null,
        CancellationToken cancellationToken = default);

    #endregion

    #region Invitations

    /// <summary>
    /// Creates an invitation to join an organization.
    /// </summary>
    Task<OrganizationInvitation> CreateInvitationAsync(
        CreateInvitationRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets an invitation by token.
    /// </summary>
    Task<OrganizationInvitation?> GetInvitationByTokenAsync(
        string token,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists pending invitations for an organization.
    /// </summary>
    Task<IReadOnlyList<OrganizationInvitation>> ListInvitationsAsync(
        Guid organizationId,
        InvitationStatus? status = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an invitation status.
    /// </summary>
    Task UpdateInvitationStatusAsync(
        Guid invitationId,
        InvitationStatus status,
        CancellationToken cancellationToken = default);

    #endregion
}

/// <summary>
/// Request to add a member to an organization.
/// </summary>
public sealed record AddOrganizationMemberRequest
{
    public Guid OrganizationId { get; init; }
    public required string UserId { get; init; }
    public required string Email { get; init; }
    public string? DisplayName { get; init; }
    public OrganizationRole Role { get; init; } = OrganizationRole.Member;
    public string? InvitedBy { get; init; }
}

/// <summary>
/// Request to update an organization member.
/// </summary>
public sealed record UpdateOrganizationMemberRequest
{
    public Guid MemberId { get; init; }
    public OrganizationRole? Role { get; init; }
    public MembershipStatus? Status { get; init; }
    public string? DisplayName { get; init; }
}

/// <summary>
/// Request to add a member to a project.
/// </summary>
public sealed record AddProjectMemberRequest
{
    public Guid ProjectId { get; init; }
    public required string UserId { get; init; }
    public required string Email { get; init; }
    public string? DisplayName { get; init; }
    public ProjectRole Role { get; init; } = ProjectRole.Viewer;
}

/// <summary>
/// Request to update a project member.
/// </summary>
public sealed record UpdateProjectMemberRequest
{
    public Guid MemberId { get; init; }
    public ProjectRole? Role { get; init; }
    public MembershipStatus? Status { get; init; }
    public string? DisplayName { get; init; }
}

/// <summary>
/// Request to create an invitation.
/// </summary>
public sealed record CreateInvitationRequest
{
    public Guid OrganizationId { get; init; }
    public required string Email { get; init; }
    public OrganizationRole Role { get; init; } = OrganizationRole.Member;
    public required string InvitedBy { get; init; }
    public TimeSpan? ExpiresIn { get; init; }
}
