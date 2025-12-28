using MorphDB.Core.Abstractions;
using MorphDB.Core.Exceptions;
using MorphDB.Core.Models;

namespace MorphDB.Service.Services;

/// <summary>
/// LoggerMessage delegates for OrganizationService.
/// </summary>
internal static partial class OrganizationServiceLogs
{
    [LoggerMessage(LogLevel.Information, "Creating organization {Name} by user {UserId}")]
    public static partial void CreatingOrganization(ILogger logger, string name, string userId);

    [LoggerMessage(LogLevel.Information, "Organization {OrganizationId} created with slug {Slug}")]
    public static partial void OrganizationCreated(ILogger logger, Guid organizationId, string slug);

    [LoggerMessage(LogLevel.Information, "Deleting organization {OrganizationId}")]
    public static partial void DeletingOrganization(ILogger logger, Guid organizationId);

    [LoggerMessage(LogLevel.Warning, "Organization {OrganizationId} not found")]
    public static partial void OrganizationNotFound(ILogger logger, Guid organizationId);

    [LoggerMessage(LogLevel.Error, "Failed to create organization: {Error}")]
    public static partial void OrganizationCreationFailed(ILogger logger, string error, Exception exception);
}

/// <summary>
/// High-level service for organization lifecycle management.
/// </summary>
public sealed class OrganizationService : IOrganizationService
{
    private readonly IOrganizationRepository _organizationRepository;
    private readonly IMembershipRepository _membershipRepository;
    private readonly IProjectRepository _projectRepository;
    private readonly IProjectService _projectService;
    private readonly ILogger<OrganizationService> _logger;

    public OrganizationService(
        IOrganizationRepository organizationRepository,
        IMembershipRepository membershipRepository,
        IProjectRepository projectRepository,
        IProjectService projectService,
        ILogger<OrganizationService> logger)
    {
        _organizationRepository = organizationRepository;
        _membershipRepository = membershipRepository;
        _projectRepository = projectRepository;
        _projectService = projectService;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<Organization> CreateOrganizationAsync(
        CreateOrganizationRequest request,
        CancellationToken cancellationToken = default)
    {
        OrganizationServiceLogs.CreatingOrganization(_logger, request.Name, request.CreatedByUserId);

        try
        {
            // Create the organization
            var organization = await _organizationRepository.CreateAsync(request, cancellationToken);

            // Add the creator as owner
            await _membershipRepository.AddOrganizationMemberAsync(new AddOrganizationMemberRequest
            {
                OrganizationId = organization.OrganizationId,
                UserId = request.CreatedByUserId,
                Email = request.CreatedByEmail,
                Role = OrganizationRole.Owner
            }, cancellationToken);

            OrganizationServiceLogs.OrganizationCreated(_logger, organization.OrganizationId, organization.Slug);
            return organization;
        }
        catch (Exception ex)
        {
            OrganizationServiceLogs.OrganizationCreationFailed(_logger, ex.Message, ex);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<Organization?> GetOrganizationAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        return await _organizationRepository.GetByIdAsync(organizationId, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Organization?> GetOrganizationBySlugAsync(
        string slug,
        CancellationToken cancellationToken = default)
    {
        return await _organizationRepository.GetBySlugAsync(slug, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Organization>> ListUserOrganizationsAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        var memberships = await _membershipRepository.ListUserOrganizationsAsync(
            userId, MembershipStatus.Active, cancellationToken);

        var organizations = new List<Organization>();

        foreach (var membership in memberships)
        {
            var org = await _organizationRepository.GetByIdAsync(membership.OrganizationId, cancellationToken);
            if (org is not null)
            {
                organizations.Add(org);
            }
        }

        return organizations;
    }

    /// <inheritdoc/>
    public async Task<Organization> UpdateOrganizationAsync(
        UpdateOrganizationRequest request,
        CancellationToken cancellationToken = default)
    {
        return await _organizationRepository.UpdateAsync(request, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task SuspendOrganizationAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        var organization = await _organizationRepository.GetByIdAsync(organizationId, cancellationToken);
        if (organization is null)
        {
            OrganizationServiceLogs.OrganizationNotFound(_logger, organizationId);
            throw new MorphDbException("ORGANIZATION_NOT_FOUND", $"Organization '{organizationId}' not found.");
        }

        await _organizationRepository.UpdateStatusAsync(organizationId, OrganizationStatus.Suspended, cancellationToken);

        // Suspend all projects in the organization
        var projects = await _projectRepository.ListAsync(organizationId, cancellationToken: cancellationToken);
        foreach (var project in projects)
        {
            if (project.Status == ProjectStatus.Active)
            {
                await _projectService.SuspendProjectAsync(project.ProjectId, cancellationToken);
            }
        }
    }

    /// <inheritdoc/>
    public async Task ReactivateOrganizationAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        var organization = await _organizationRepository.GetByIdAsync(organizationId, cancellationToken);
        if (organization is null)
        {
            OrganizationServiceLogs.OrganizationNotFound(_logger, organizationId);
            throw new MorphDbException("ORGANIZATION_NOT_FOUND", $"Organization '{organizationId}' not found.");
        }

        await _organizationRepository.UpdateStatusAsync(organizationId, OrganizationStatus.Active, cancellationToken);

        // Reactivate suspended projects
        var projects = await _projectRepository.ListAsync(organizationId, ProjectStatus.Suspended, cancellationToken: cancellationToken);
        foreach (var project in projects)
        {
            await _projectService.ReactivateProjectAsync(project.ProjectId, cancellationToken);
        }
    }

    /// <inheritdoc/>
    public async Task DeleteOrganizationAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        OrganizationServiceLogs.DeletingOrganization(_logger, organizationId);

        var organization = await _organizationRepository.GetByIdAsync(organizationId, cancellationToken);
        if (organization is null)
        {
            OrganizationServiceLogs.OrganizationNotFound(_logger, organizationId);
            throw new MorphDbException("ORGANIZATION_NOT_FOUND", $"Organization '{organizationId}' not found.");
        }

        // Delete all projects first
        var projects = await _projectRepository.ListAsync(organizationId, cancellationToken: cancellationToken);
        foreach (var project in projects)
        {
            await _projectService.DeleteProjectAsync(project.ProjectId, cancellationToken);
        }

        // Delete the organization
        await _organizationRepository.DeleteAsync(organizationId, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<OrganizationStats> GetOrganizationStatsAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        var projects = await _projectRepository.ListAsync(organizationId, cancellationToken: cancellationToken);
        var activeProjects = projects.Count(p => p.Status == ProjectStatus.Active);

        var totalMembers = await _membershipRepository.CountOrganizationMembersAsync(organizationId, cancellationToken: cancellationToken);
        var activeMembers = await _membershipRepository.CountOrganizationMembersAsync(organizationId, MembershipStatus.Active, cancellationToken);

        var invitations = await _membershipRepository.ListInvitationsAsync(organizationId, InvitationStatus.Pending, cancellationToken);

        return new OrganizationStats
        {
            OrganizationId = organizationId,
            TotalProjects = projects.Count,
            ActiveProjects = activeProjects,
            TotalMembers = totalMembers,
            ActiveMembers = activeMembers,
            PendingInvitations = invitations.Count,
            TotalStorageBytes = 0, // TODO: Calculate from project stats
            LastActivityAt = projects.Max(p => (DateTimeOffset?)p.UpdatedAt)
        };
    }
}
