using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Models;
using MorphDB.Service.Models.Api;

namespace MorphDB.Service.Controllers;

/// <summary>
/// LoggerMessage delegates for OrganizationController.
/// </summary>
internal static partial class OrganizationControllerLogs
{
    [LoggerMessage(LogLevel.Information, "Creating organization {Name}")]
    public static partial void CreatingOrganization(ILogger logger, string name);

    [LoggerMessage(LogLevel.Information, "Listing organizations for user {UserId}")]
    public static partial void ListingOrganizations(ILogger logger, string userId);

    [LoggerMessage(LogLevel.Error, "Organization operation failed: {Error}")]
    public static partial void OrganizationOperationFailed(ILogger logger, string error, Exception exception);
}

/// <summary>
/// REST API controller for organization management.
/// </summary>
[ApiController]
[Route("api/organizations")]
[Produces("application/json")]
[Authorize]
public sealed class OrganizationController : ControllerBase
{
    private readonly IOrganizationService _organizationService;
    private readonly IMembershipRepository _membershipRepository;
    private readonly IPermissionService _permissionService;
    private readonly ILogger<OrganizationController> _logger;

    public OrganizationController(
        IOrganizationService organizationService,
        IMembershipRepository membershipRepository,
        IPermissionService permissionService,
        ILogger<OrganizationController> logger)
    {
        _organizationService = organizationService;
        _membershipRepository = membershipRepository;
        _permissionService = permissionService;
        _logger = logger;
    }

    private string GetUserId() => User.FindFirst("sub")?.Value ?? User.Identity?.Name ?? "anonymous";
    private string GetUserEmail() => User.FindFirst("email")?.Value ?? $"{GetUserId()}@unknown";

    /// <summary>
    /// Creates a new organization.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(OrganizationApiResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateOrganization(
        [FromBody] CreateOrganizationApiRequest request,
        CancellationToken cancellationToken)
    {
        OrganizationControllerLogs.CreatingOrganization(_logger, request.Name);

        try
        {
            var organization = await _organizationService.CreateOrganizationAsync(new CreateOrganizationRequest
            {
                Name = request.Name,
                Slug = request.Slug,
                Description = request.Description,
                Settings = request.Settings,
                CreatedByUserId = GetUserId(),
                CreatedByEmail = GetUserEmail()
            }, cancellationToken);

            return CreatedAtAction(
                nameof(GetOrganization),
                new { organizationId = organization.OrganizationId },
                OrganizationApiResponse.FromModel(organization));
        }
        catch (Exception ex)
        {
            OrganizationControllerLogs.OrganizationOperationFailed(_logger, ex.Message, ex);
            return BadRequest(new ErrorResponse { Error = "CREATE_FAILED", Message = ex.Message, Code = "CREATE_FAILED" });
        }
    }

    /// <summary>
    /// Gets an organization by ID.
    /// </summary>
    [HttpGet("{organizationId:guid}")]
    [ProducesResponseType(typeof(OrganizationApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetOrganization(Guid organizationId, CancellationToken cancellationToken)
    {
        var hasAccess = await _permissionService.HasOrganizationPermissionAsync(
            GetUserId(), organizationId, Permissions.Organization.View, cancellationToken);

        if (!hasAccess)
        {
            return NotFound();
        }

        var organization = await _organizationService.GetOrganizationAsync(organizationId, cancellationToken);
        if (organization is null)
        {
            return NotFound();
        }

        return Ok(OrganizationApiResponse.FromModel(organization));
    }

    /// <summary>
    /// Lists organizations the current user belongs to.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<OrganizationApiResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListOrganizations(CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        OrganizationControllerLogs.ListingOrganizations(_logger, userId);

        var organizations = await _organizationService.ListUserOrganizationsAsync(userId, cancellationToken);
        return Ok(organizations.Select(OrganizationApiResponse.FromModel));
    }

    /// <summary>
    /// Updates an organization.
    /// </summary>
    [HttpPatch("{organizationId:guid}")]
    [ProducesResponseType(typeof(OrganizationApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateOrganization(
        Guid organizationId,
        [FromBody] UpdateOrganizationApiRequest request,
        CancellationToken cancellationToken)
    {
        var hasPermission = await _permissionService.HasOrganizationPermissionAsync(
            GetUserId(), organizationId, Permissions.Organization.Update, cancellationToken);

        if (!hasPermission)
        {
            return Forbid();
        }

        try
        {
            var organization = await _organizationService.UpdateOrganizationAsync(new UpdateOrganizationRequest
            {
                OrganizationId = organizationId,
                Name = request.Name,
                Description = request.Description,
                Settings = request.Settings
            }, cancellationToken);

            return Ok(OrganizationApiResponse.FromModel(organization));
        }
        catch (Exception ex)
        {
            OrganizationControllerLogs.OrganizationOperationFailed(_logger, ex.Message, ex);
            return NotFound(new ErrorResponse { Error = "NOT_FOUND", Message = ex.Message, Code = "NOT_FOUND" });
        }
    }

    /// <summary>
    /// Deletes an organization and all its projects.
    /// </summary>
    [HttpDelete("{organizationId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteOrganization(Guid organizationId, CancellationToken cancellationToken)
    {
        var hasPermission = await _permissionService.HasOrganizationPermissionAsync(
            GetUserId(), organizationId, Permissions.Organization.Delete, cancellationToken);

        if (!hasPermission)
        {
            return Forbid();
        }

        try
        {
            await _organizationService.DeleteOrganizationAsync(organizationId, cancellationToken);
            return NoContent();
        }
        catch (Exception ex)
        {
            OrganizationControllerLogs.OrganizationOperationFailed(_logger, ex.Message, ex);
            return NotFound(new ErrorResponse { Error = "NOT_FOUND", Message = ex.Message, Code = "NOT_FOUND" });
        }
    }

    /// <summary>
    /// Gets organization statistics.
    /// </summary>
    [HttpGet("{organizationId:guid}/stats")]
    [ProducesResponseType(typeof(OrganizationStats), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetOrganizationStats(Guid organizationId, CancellationToken cancellationToken)
    {
        var hasAccess = await _permissionService.HasOrganizationPermissionAsync(
            GetUserId(), organizationId, Permissions.Organization.View, cancellationToken);

        if (!hasAccess)
        {
            return NotFound();
        }

        var stats = await _organizationService.GetOrganizationStatsAsync(organizationId, cancellationToken);
        return Ok(stats);
    }

    #region Members

    /// <summary>
    /// Lists members of an organization.
    /// </summary>
    [HttpGet("{organizationId:guid}/members")]
    [ProducesResponseType(typeof(IReadOnlyList<OrganizationMemberApiResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListMembers(
        Guid organizationId,
        [FromQuery] int offset = 0,
        [FromQuery] int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var hasAccess = await _permissionService.HasOrganizationPermissionAsync(
            GetUserId(), organizationId, Permissions.Organization.View, cancellationToken);

        if (!hasAccess)
        {
            return NotFound();
        }

        var members = await _membershipRepository.ListOrganizationMembersAsync(
            organizationId, MembershipStatus.Active, offset, limit, cancellationToken);

        return Ok(members.Select(OrganizationMemberApiResponse.FromModel));
    }

    /// <summary>
    /// Adds a member to an organization.
    /// </summary>
    [HttpPost("{organizationId:guid}/members")]
    [ProducesResponseType(typeof(OrganizationMemberApiResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> AddMember(
        Guid organizationId,
        [FromBody] AddMemberApiRequest request,
        CancellationToken cancellationToken)
    {
        var hasPermission = await _permissionService.HasOrganizationPermissionAsync(
            GetUserId(), organizationId, Permissions.Organization.ManageMembers, cancellationToken);

        if (!hasPermission)
        {
            return Forbid();
        }

        var member = await _membershipRepository.AddOrganizationMemberAsync(new AddOrganizationMemberRequest
        {
            OrganizationId = organizationId,
            UserId = request.UserId,
            Email = request.Email,
            DisplayName = request.DisplayName,
            Role = request.Role,
            InvitedBy = GetUserId()
        }, cancellationToken);

        return CreatedAtAction(
            nameof(GetMember),
            new { organizationId, memberId = member.MemberId },
            OrganizationMemberApiResponse.FromModel(member));
    }

    /// <summary>
    /// Gets a member by ID.
    /// </summary>
    [HttpGet("{organizationId:guid}/members/{memberId:guid}")]
    [ProducesResponseType(typeof(OrganizationMemberApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMember(
        Guid organizationId,
        Guid memberId,
        CancellationToken cancellationToken)
    {
        var hasAccess = await _permissionService.HasOrganizationPermissionAsync(
            GetUserId(), organizationId, Permissions.Organization.View, cancellationToken);

        if (!hasAccess)
        {
            return NotFound();
        }

        var member = await _membershipRepository.GetOrganizationMemberAsync(memberId, cancellationToken);
        if (member is null || member.OrganizationId != organizationId)
        {
            return NotFound();
        }

        return Ok(OrganizationMemberApiResponse.FromModel(member));
    }

    /// <summary>
    /// Updates a member's role.
    /// </summary>
    [HttpPatch("{organizationId:guid}/members/{memberId:guid}")]
    [ProducesResponseType(typeof(OrganizationMemberApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> UpdateMember(
        Guid organizationId,
        Guid memberId,
        [FromBody] UpdateMemberApiRequest request,
        CancellationToken cancellationToken)
    {
        var hasPermission = await _permissionService.HasOrganizationPermissionAsync(
            GetUserId(), organizationId, Permissions.Organization.ManageMembers, cancellationToken);

        if (!hasPermission)
        {
            return Forbid();
        }

        var member = await _membershipRepository.UpdateOrganizationMemberAsync(new UpdateOrganizationMemberRequest
        {
            MemberId = memberId,
            Role = request.Role,
            DisplayName = request.DisplayName
        }, cancellationToken);

        return Ok(OrganizationMemberApiResponse.FromModel(member));
    }

    /// <summary>
    /// Removes a member from an organization.
    /// </summary>
    [HttpDelete("{organizationId:guid}/members/{memberId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> RemoveMember(
        Guid organizationId,
        Guid memberId,
        CancellationToken cancellationToken)
    {
        var hasPermission = await _permissionService.HasOrganizationPermissionAsync(
            GetUserId(), organizationId, Permissions.Organization.ManageMembers, cancellationToken);

        if (!hasPermission)
        {
            return Forbid();
        }

        await _membershipRepository.RemoveOrganizationMemberAsync(memberId, cancellationToken);
        return NoContent();
    }

    #endregion

    #region Invitations

    /// <summary>
    /// Lists pending invitations.
    /// </summary>
    [HttpGet("{organizationId:guid}/invitations")]
    [ProducesResponseType(typeof(IReadOnlyList<InvitationApiResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListInvitations(
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        var hasPermission = await _permissionService.HasOrganizationPermissionAsync(
            GetUserId(), organizationId, Permissions.Organization.ManageMembers, cancellationToken);

        if (!hasPermission)
        {
            return Forbid();
        }

        var invitations = await _membershipRepository.ListInvitationsAsync(
            organizationId, InvitationStatus.Pending, cancellationToken);

        return Ok(invitations.Select(InvitationApiResponse.FromModel));
    }

    /// <summary>
    /// Creates an invitation to join the organization.
    /// </summary>
    [HttpPost("{organizationId:guid}/invitations")]
    [ProducesResponseType(typeof(InvitationApiResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> CreateInvitation(
        Guid organizationId,
        [FromBody] CreateInvitationApiRequest request,
        CancellationToken cancellationToken)
    {
        var hasPermission = await _permissionService.HasOrganizationPermissionAsync(
            GetUserId(), organizationId, Permissions.Organization.ManageMembers, cancellationToken);

        if (!hasPermission)
        {
            return Forbid();
        }

        var invitation = await _membershipRepository.CreateInvitationAsync(new CreateInvitationRequest
        {
            OrganizationId = organizationId,
            Email = request.Email,
            Role = request.Role,
            InvitedBy = GetUserId()
        }, cancellationToken);

        return Created($"/api/organizations/{organizationId}/invitations/{invitation.InvitationId}",
            InvitationApiResponse.FromModel(invitation));
    }

    /// <summary>
    /// Revokes an invitation.
    /// </summary>
    [HttpDelete("{organizationId:guid}/invitations/{invitationId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> RevokeInvitation(
        Guid organizationId,
        Guid invitationId,
        CancellationToken cancellationToken)
    {
        var hasPermission = await _permissionService.HasOrganizationPermissionAsync(
            GetUserId(), organizationId, Permissions.Organization.ManageMembers, cancellationToken);

        if (!hasPermission)
        {
            return Forbid();
        }

        await _membershipRepository.UpdateInvitationStatusAsync(invitationId, InvitationStatus.Revoked, cancellationToken);
        return NoContent();
    }

    #endregion
}

#region API Models

public sealed class CreateOrganizationApiRequest
{
    public required string Name { get; init; }
    public string? Slug { get; init; }
    public string? Description { get; init; }
    public OrganizationSettings? Settings { get; init; }
}

public sealed class UpdateOrganizationApiRequest
{
    public string? Name { get; init; }
    public string? Description { get; init; }
    public OrganizationSettings? Settings { get; init; }
}

public sealed class OrganizationApiResponse
{
    public Guid OrganizationId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Slug { get; init; } = string.Empty;
    public string? Description { get; init; }
    public OrganizationSettings? Settings { get; init; }
    public string Status { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }

    public static OrganizationApiResponse FromModel(Organization model) => new()
    {
        OrganizationId = model.OrganizationId,
        Name = model.Name,
        Slug = model.Slug,
        Description = model.Description,
        Settings = model.Settings,
        Status = model.Status.ToString(),
        CreatedAt = model.CreatedAt,
        UpdatedAt = model.UpdatedAt
    };
}

public sealed class AddMemberApiRequest
{
    public required string UserId { get; init; }
    public required string Email { get; init; }
    public string? DisplayName { get; init; }
    public OrganizationRole Role { get; init; } = OrganizationRole.Member;
}

public sealed class UpdateMemberApiRequest
{
    public OrganizationRole? Role { get; init; }
    public string? DisplayName { get; init; }
}

public sealed class OrganizationMemberApiResponse
{
    public Guid MemberId { get; init; }
    public Guid OrganizationId { get; init; }
    public string UserId { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string? DisplayName { get; init; }
    public string Role { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public DateTimeOffset JoinedAt { get; init; }

    public static OrganizationMemberApiResponse FromModel(OrganizationMember model) => new()
    {
        MemberId = model.MemberId,
        OrganizationId = model.OrganizationId,
        UserId = model.UserId,
        Email = model.Email,
        DisplayName = model.DisplayName,
        Role = model.Role.ToString(),
        Status = model.Status.ToString(),
        JoinedAt = model.JoinedAt
    };
}

public sealed class CreateInvitationApiRequest
{
    public required string Email { get; init; }
    public OrganizationRole Role { get; init; } = OrganizationRole.Member;
}

public sealed class InvitationApiResponse
{
    public Guid InvitationId { get; init; }
    public Guid OrganizationId { get; init; }
    public string Email { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
    public string InvitedBy { get; init; } = string.Empty;

    public static InvitationApiResponse FromModel(OrganizationInvitation model) => new()
    {
        InvitationId = model.InvitationId,
        OrganizationId = model.OrganizationId,
        Email = model.Email,
        Role = model.Role.ToString(),
        Status = model.Status.ToString(),
        CreatedAt = model.CreatedAt,
        ExpiresAt = model.ExpiresAt,
        InvitedBy = model.InvitedBy
    };
}

#endregion
