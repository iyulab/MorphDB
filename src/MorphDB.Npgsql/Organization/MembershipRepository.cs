using System.Security.Cryptography;
using Dapper;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Exceptions;
using MorphDB.Core.Models;
using Npgsql;

namespace MorphDB.Npgsql.Organization;

/// <summary>
/// PostgreSQL repository for managing organization and project memberships.
/// </summary>
public sealed class MembershipRepository : IMembershipRepository
{
    private readonly NpgsqlDataSource _dataSource;
    private static readonly TimeSpan DefaultInvitationExpiry = TimeSpan.FromDays(7);

    public MembershipRepository(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    #region Organization Members

    /// <inheritdoc/>
    public async Task<OrganizationMember> AddOrganizationMemberAsync(
        AddOrganizationMemberRequest request,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            INSERT INTO morphdb._morph_organization_members (
                member_id, organization_id, user_id, email, display_name, role, status, joined_at, updated_at, invited_by
            )
            VALUES (
                @MemberId, @OrganizationId, @UserId, @Email, @DisplayName, @Role, @Status, NOW(), NOW(), @InvitedBy
            )
            RETURNING *
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        var entity = await connection.QuerySingleAsync<OrgMemberEntity>(sql, new
        {
            MemberId = Guid.NewGuid(),
            request.OrganizationId,
            request.UserId,
            request.Email,
            request.DisplayName,
            Role = (int)request.Role,
            Status = (int)MembershipStatus.Active,
            request.InvitedBy
        });

        return MapToOrgMember(entity);
    }

    /// <inheritdoc/>
    public async Task<OrganizationMember?> GetOrganizationMemberAsync(
        Guid memberId,
        CancellationToken cancellationToken = default)
    {
        const string sql = "SELECT * FROM morphdb._morph_organization_members WHERE member_id = @MemberId";

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var entity = await connection.QuerySingleOrDefaultAsync<OrgMemberEntity>(sql, new { MemberId = memberId });

        return entity is null ? null : MapToOrgMember(entity);
    }

    /// <inheritdoc/>
    public async Task<OrganizationMember?> GetOrganizationMemberByUserIdAsync(
        Guid organizationId,
        string userId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT * FROM morphdb._morph_organization_members
            WHERE organization_id = @OrganizationId AND user_id = @UserId
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var entity = await connection.QuerySingleOrDefaultAsync<OrgMemberEntity>(sql, new { OrganizationId = organizationId, UserId = userId });

        return entity is null ? null : MapToOrgMember(entity);
    }

    /// <inheritdoc/>
    public async Task<OrganizationMember?> GetOrganizationMemberByEmailAsync(
        Guid organizationId,
        string email,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT * FROM morphdb._morph_organization_members
            WHERE organization_id = @OrganizationId AND LOWER(email) = LOWER(@Email)
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var entity = await connection.QuerySingleOrDefaultAsync<OrgMemberEntity>(sql, new { OrganizationId = organizationId, Email = email });

        return entity is null ? null : MapToOrgMember(entity);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<OrganizationMember>> ListOrganizationMembersAsync(
        Guid organizationId,
        MembershipStatus? status = null,
        int offset = 0,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT * FROM morphdb._morph_organization_members
            WHERE organization_id = @OrganizationId
              AND (@Status IS NULL OR status = @Status)
            ORDER BY role DESC, joined_at
            OFFSET @Offset LIMIT @Limit
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var entities = await connection.QueryAsync<OrgMemberEntity>(sql, new
        {
            OrganizationId = organizationId,
            Status = status.HasValue ? (int?)status.Value : null,
            Offset = offset,
            Limit = limit
        });

        return entities.Select(MapToOrgMember).ToList();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<OrganizationMember>> ListUserOrganizationsAsync(
        string userId,
        MembershipStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT * FROM morphdb._morph_organization_members
            WHERE user_id = @UserId
              AND (@Status IS NULL OR status = @Status)
            ORDER BY joined_at DESC
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var entities = await connection.QueryAsync<OrgMemberEntity>(sql, new
        {
            UserId = userId,
            Status = status.HasValue ? (int?)status.Value : null
        });

        return entities.Select(MapToOrgMember).ToList();
    }

    /// <inheritdoc/>
    public async Task<OrganizationMember> UpdateOrganizationMemberAsync(
        UpdateOrganizationMemberRequest request,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE morphdb._morph_organization_members
            SET role = COALESCE(@Role, role),
                status = COALESCE(@Status, status),
                display_name = COALESCE(@DisplayName, display_name),
                updated_at = NOW()
            WHERE member_id = @MemberId
            RETURNING *
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var entity = await connection.QuerySingleOrDefaultAsync<OrgMemberEntity>(sql, new
        {
            request.MemberId,
            Role = request.Role.HasValue ? (int?)request.Role.Value : null,
            Status = request.Status.HasValue ? (int?)request.Status.Value : null,
            request.DisplayName
        });

        if (entity is null)
        {
            throw new MorphDbException("MEMBER_NOT_FOUND", $"Organization member '{request.MemberId}' not found.");
        }

        return MapToOrgMember(entity);
    }

    /// <inheritdoc/>
    public async Task RemoveOrganizationMemberAsync(
        Guid memberId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE morphdb._morph_organization_members
            SET status = @Status, updated_at = NOW()
            WHERE member_id = @MemberId
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(sql, new { MemberId = memberId, Status = (int)MembershipStatus.Removed });
    }

    /// <inheritdoc/>
    public async Task<int> CountOrganizationMembersAsync(
        Guid organizationId,
        MembershipStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT COUNT(*) FROM morphdb._morph_organization_members
            WHERE organization_id = @OrganizationId
              AND (@Status IS NULL OR status = @Status)
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleAsync<int>(sql, new
        {
            OrganizationId = organizationId,
            Status = status.HasValue ? (int?)status.Value : null
        });
    }

    #endregion

    #region Project Members

    /// <inheritdoc/>
    public async Task<ProjectMember> AddProjectMemberAsync(
        AddProjectMemberRequest request,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            INSERT INTO morphdb._morph_project_members (
                member_id, project_id, user_id, email, display_name, role, status, joined_at, updated_at
            )
            VALUES (
                @MemberId, @ProjectId, @UserId, @Email, @DisplayName, @Role, @Status, NOW(), NOW()
            )
            RETURNING *
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        var entity = await connection.QuerySingleAsync<ProjMemberEntity>(sql, new
        {
            MemberId = Guid.NewGuid(),
            request.ProjectId,
            request.UserId,
            request.Email,
            request.DisplayName,
            Role = (int)request.Role,
            Status = (int)MembershipStatus.Active
        });

        return MapToProjMember(entity);
    }

    /// <inheritdoc/>
    public async Task<ProjectMember?> GetProjectMemberAsync(
        Guid memberId,
        CancellationToken cancellationToken = default)
    {
        const string sql = "SELECT * FROM morphdb._morph_project_members WHERE member_id = @MemberId";

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var entity = await connection.QuerySingleOrDefaultAsync<ProjMemberEntity>(sql, new { MemberId = memberId });

        return entity is null ? null : MapToProjMember(entity);
    }

    /// <inheritdoc/>
    public async Task<ProjectMember?> GetProjectMemberByUserIdAsync(
        Guid projectId,
        string userId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT * FROM morphdb._morph_project_members
            WHERE project_id = @ProjectId AND user_id = @UserId
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var entity = await connection.QuerySingleOrDefaultAsync<ProjMemberEntity>(sql, new { ProjectId = projectId, UserId = userId });

        return entity is null ? null : MapToProjMember(entity);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ProjectMember>> ListProjectMembersAsync(
        Guid projectId,
        MembershipStatus? status = null,
        int offset = 0,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT * FROM morphdb._morph_project_members
            WHERE project_id = @ProjectId
              AND (@Status IS NULL OR status = @Status)
            ORDER BY role DESC, joined_at
            OFFSET @Offset LIMIT @Limit
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var entities = await connection.QueryAsync<ProjMemberEntity>(sql, new
        {
            ProjectId = projectId,
            Status = status.HasValue ? (int?)status.Value : null,
            Offset = offset,
            Limit = limit
        });

        return entities.Select(MapToProjMember).ToList();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ProjectMember>> ListUserProjectsAsync(
        string userId,
        MembershipStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT * FROM morphdb._morph_project_members
            WHERE user_id = @UserId
              AND (@Status IS NULL OR status = @Status)
            ORDER BY joined_at DESC
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var entities = await connection.QueryAsync<ProjMemberEntity>(sql, new
        {
            UserId = userId,
            Status = status.HasValue ? (int?)status.Value : null
        });

        return entities.Select(MapToProjMember).ToList();
    }

    /// <inheritdoc/>
    public async Task<ProjectMember> UpdateProjectMemberAsync(
        UpdateProjectMemberRequest request,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE morphdb._morph_project_members
            SET role = COALESCE(@Role, role),
                status = COALESCE(@Status, status),
                display_name = COALESCE(@DisplayName, display_name),
                updated_at = NOW()
            WHERE member_id = @MemberId
            RETURNING *
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var entity = await connection.QuerySingleOrDefaultAsync<ProjMemberEntity>(sql, new
        {
            request.MemberId,
            Role = request.Role.HasValue ? (int?)request.Role.Value : null,
            Status = request.Status.HasValue ? (int?)request.Status.Value : null,
            request.DisplayName
        });

        if (entity is null)
        {
            throw new MorphDbException("MEMBER_NOT_FOUND", $"Project member '{request.MemberId}' not found.");
        }

        return MapToProjMember(entity);
    }

    /// <inheritdoc/>
    public async Task RemoveProjectMemberAsync(
        Guid memberId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE morphdb._morph_project_members
            SET status = @Status, updated_at = NOW()
            WHERE member_id = @MemberId
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(sql, new { MemberId = memberId, Status = (int)MembershipStatus.Removed });
    }

    /// <inheritdoc/>
    public async Task<int> CountProjectMembersAsync(
        Guid projectId,
        MembershipStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT COUNT(*) FROM morphdb._morph_project_members
            WHERE project_id = @ProjectId
              AND (@Status IS NULL OR status = @Status)
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleAsync<int>(sql, new
        {
            ProjectId = projectId,
            Status = status.HasValue ? (int?)status.Value : null
        });
    }

    #endregion

    #region Invitations

    /// <inheritdoc/>
    public async Task<OrganizationInvitation> CreateInvitationAsync(
        CreateInvitationRequest request,
        CancellationToken cancellationToken = default)
    {
        var token = GenerateInvitationToken();
        var expiresAt = DateTimeOffset.UtcNow.Add(request.ExpiresIn ?? DefaultInvitationExpiry);

        const string sql = """
            INSERT INTO morphdb._morph_organization_invitations (
                invitation_id, organization_id, email, role, token, status, created_at, expires_at, invited_by
            )
            VALUES (
                @InvitationId, @OrganizationId, @Email, @Role, @Token, @Status, NOW(), @ExpiresAt, @InvitedBy
            )
            RETURNING *
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        var entity = await connection.QuerySingleAsync<InvitationEntity>(sql, new
        {
            InvitationId = Guid.NewGuid(),
            request.OrganizationId,
            request.Email,
            Role = (int)request.Role,
            Token = token,
            Status = (int)InvitationStatus.Pending,
            ExpiresAt = expiresAt,
            request.InvitedBy
        });

        return MapToInvitation(entity);
    }

    /// <inheritdoc/>
    public async Task<OrganizationInvitation?> GetInvitationByTokenAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        const string sql = "SELECT * FROM morphdb._morph_organization_invitations WHERE token = @Token";

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var entity = await connection.QuerySingleOrDefaultAsync<InvitationEntity>(sql, new { Token = token });

        return entity is null ? null : MapToInvitation(entity);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<OrganizationInvitation>> ListInvitationsAsync(
        Guid organizationId,
        InvitationStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT * FROM morphdb._morph_organization_invitations
            WHERE organization_id = @OrganizationId
              AND (@Status IS NULL OR status = @Status)
            ORDER BY created_at DESC
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var entities = await connection.QueryAsync<InvitationEntity>(sql, new
        {
            OrganizationId = organizationId,
            Status = status.HasValue ? (int?)status.Value : null
        });

        return entities.Select(MapToInvitation).ToList();
    }

    /// <inheritdoc/>
    public async Task UpdateInvitationStatusAsync(
        Guid invitationId,
        InvitationStatus status,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE morphdb._morph_organization_invitations
            SET status = @Status
            WHERE invitation_id = @InvitationId
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(sql, new { InvitationId = invitationId, Status = (int)status });
    }

    #endregion

    #region Helpers

    private static string GenerateInvitationToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").TrimEnd('=');
    }

    private static OrganizationMember MapToOrgMember(OrgMemberEntity entity)
    {
        return new OrganizationMember
        {
            MemberId = entity.member_id,
            OrganizationId = entity.organization_id,
            UserId = entity.user_id,
            Email = entity.email,
            DisplayName = entity.display_name,
            Role = (OrganizationRole)entity.role,
            Status = (MembershipStatus)entity.status,
            JoinedAt = entity.joined_at,
            UpdatedAt = entity.updated_at,
            InvitedBy = entity.invited_by
        };
    }

    private static ProjectMember MapToProjMember(ProjMemberEntity entity)
    {
        return new ProjectMember
        {
            MemberId = entity.member_id,
            ProjectId = entity.project_id,
            UserId = entity.user_id,
            Email = entity.email,
            DisplayName = entity.display_name,
            Role = (ProjectRole)entity.role,
            Status = (MembershipStatus)entity.status,
            JoinedAt = entity.joined_at,
            UpdatedAt = entity.updated_at
        };
    }

    private static OrganizationInvitation MapToInvitation(InvitationEntity entity)
    {
        return new OrganizationInvitation
        {
            InvitationId = entity.invitation_id,
            OrganizationId = entity.organization_id,
            Email = entity.email,
            Role = (OrganizationRole)entity.role,
            Token = entity.token,
            Status = (InvitationStatus)entity.status,
            CreatedAt = entity.created_at,
            ExpiresAt = entity.expires_at,
            InvitedBy = entity.invited_by
        };
    }

    #endregion

    #region Entity Records

    private sealed record OrgMemberEntity(
        Guid member_id,
        Guid organization_id,
        string user_id,
        string email,
        string? display_name,
        int role,
        int status,
        DateTimeOffset joined_at,
        DateTimeOffset updated_at,
        string? invited_by);

    private sealed record ProjMemberEntity(
        Guid member_id,
        Guid project_id,
        string user_id,
        string email,
        string? display_name,
        int role,
        int status,
        DateTimeOffset joined_at,
        DateTimeOffset updated_at);

    private sealed record InvitationEntity(
        Guid invitation_id,
        Guid organization_id,
        string email,
        int role,
        string token,
        int status,
        DateTimeOffset created_at,
        DateTimeOffset expires_at,
        string invited_by);

    #endregion
}
