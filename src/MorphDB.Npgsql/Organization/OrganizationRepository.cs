using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Exceptions;
using MorphDB.Core.Models;
using Npgsql;

namespace MorphDB.Npgsql.Organization;

/// <summary>
/// PostgreSQL repository for managing organizations.
/// </summary>
public sealed partial class OrganizationRepository : IOrganizationRepository
{
    private readonly NpgsqlDataSource _dataSource;

    public OrganizationRepository(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    /// <inheritdoc/>
    public async Task<Core.Models.Organization> CreateAsync(
        CreateOrganizationRequest request,
        CancellationToken cancellationToken = default)
    {
        var organizationId = request.OrganizationId ?? Guid.NewGuid();
        var slug = request.Slug ?? GenerateSlug(request.Name);

        if (!await IsSlugAvailableAsync(slug, cancellationToken))
        {
            throw new MorphDbException("DUPLICATE_SLUG", $"Organization slug '{slug}' is already in use.");
        }

        const string sql = """
            INSERT INTO morphdb._morph_organizations (
                organization_id, name, slug, description, settings, status, created_at, updated_at
            )
            VALUES (
                @OrganizationId, @Name, @Slug, @Description, @Settings::jsonb, @Status, NOW(), NOW()
            )
            RETURNING *
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        var entity = await connection.QuerySingleAsync<OrganizationEntity>(sql, new
        {
            OrganizationId = organizationId,
            request.Name,
            Slug = slug,
            request.Description,
            Settings = JsonSerializer.Serialize(request.Settings),
            Status = (int)OrganizationStatus.Active
        });

        return MapToOrganization(entity);
    }

    /// <inheritdoc/>
    public async Task<Core.Models.Organization?> GetByIdAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT * FROM morphdb._morph_organizations
            WHERE organization_id = @OrganizationId AND status != @DeletedStatus
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        var entity = await connection.QuerySingleOrDefaultAsync<OrganizationEntity>(sql, new
        {
            OrganizationId = organizationId,
            DeletedStatus = (int)OrganizationStatus.Deleted
        });

        return entity is null ? null : MapToOrganization(entity);
    }

    /// <inheritdoc/>
    public async Task<Core.Models.Organization?> GetBySlugAsync(
        string slug,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT * FROM morphdb._morph_organizations
            WHERE slug = @Slug AND status != @DeletedStatus
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        var entity = await connection.QuerySingleOrDefaultAsync<OrganizationEntity>(sql, new
        {
            Slug = slug,
            DeletedStatus = (int)OrganizationStatus.Deleted
        });

        return entity is null ? null : MapToOrganization(entity);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Core.Models.Organization>> ListAsync(
        OrganizationStatus? status = null,
        int offset = 0,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT * FROM morphdb._morph_organizations
            WHERE status != @DeletedStatus
              AND (@Status IS NULL OR status = @Status)
            ORDER BY created_at DESC
            OFFSET @Offset LIMIT @Limit
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        var entities = await connection.QueryAsync<OrganizationEntity>(sql, new
        {
            DeletedStatus = (int)OrganizationStatus.Deleted,
            Status = status.HasValue ? (int?)status.Value : null,
            Offset = offset,
            Limit = limit
        });

        return entities.Select(MapToOrganization).ToList();
    }

    /// <inheritdoc/>
    public async Task<Core.Models.Organization> UpdateAsync(
        UpdateOrganizationRequest request,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE morphdb._morph_organizations
            SET name = COALESCE(@Name, name),
                description = COALESCE(@Description, description),
                settings = COALESCE(@Settings::jsonb, settings),
                updated_at = NOW()
            WHERE organization_id = @OrganizationId AND status != @DeletedStatus
            RETURNING *
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        var entity = await connection.QuerySingleOrDefaultAsync<OrganizationEntity>(sql, new
        {
            request.OrganizationId,
            request.Name,
            request.Description,
            Settings = request.Settings is not null ? JsonSerializer.Serialize(request.Settings) : null,
            DeletedStatus = (int)OrganizationStatus.Deleted
        });

        if (entity is null)
        {
            throw new MorphDbException("ORGANIZATION_NOT_FOUND", $"Organization '{request.OrganizationId}' not found.");
        }

        return MapToOrganization(entity);
    }

    /// <inheritdoc/>
    public async Task UpdateStatusAsync(
        Guid organizationId,
        OrganizationStatus status,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE morphdb._morph_organizations
            SET status = @Status, updated_at = NOW()
            WHERE organization_id = @OrganizationId
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(sql, new { OrganizationId = organizationId, Status = (int)status });
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        await UpdateStatusAsync(organizationId, OrganizationStatus.Deleted, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<bool> IsSlugAvailableAsync(
        string slug,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT NOT EXISTS (
                SELECT 1 FROM morphdb._morph_organizations
                WHERE slug = @Slug AND status != @DeletedStatus
            )
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleAsync<bool>(sql, new
        {
            Slug = slug,
            DeletedStatus = (int)OrganizationStatus.Deleted
        });
    }

    /// <inheritdoc/>
    public async Task<int> CountAsync(
        OrganizationStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT COUNT(*) FROM morphdb._morph_organizations
            WHERE status != @DeletedStatus
              AND (@Status IS NULL OR status = @Status)
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleAsync<int>(sql, new
        {
            DeletedStatus = (int)OrganizationStatus.Deleted,
            Status = status.HasValue ? (int?)status.Value : null
        });
    }

    private static string GenerateSlug(string name)
    {
        var slug = name.ToLowerInvariant();
        slug = SlugRegex().Replace(slug, "-");
        slug = MultiDashRegex().Replace(slug, "-");
        return slug.Trim('-');
    }

    [GeneratedRegex(@"[^a-z0-9\-]")]
    private static partial Regex SlugRegex();

    [GeneratedRegex(@"-+")]
    private static partial Regex MultiDashRegex();

    private static Core.Models.Organization MapToOrganization(OrganizationEntity entity)
    {
        return new Core.Models.Organization
        {
            OrganizationId = entity.organization_id,
            Name = entity.name,
            Slug = entity.slug,
            Description = entity.description,
            Settings = entity.settings is not null
                ? JsonSerializer.Deserialize<OrganizationSettings>(entity.settings)
                : null,
            Status = (OrganizationStatus)entity.status,
            CreatedAt = entity.created_at,
            UpdatedAt = entity.updated_at
        };
    }

    private sealed class OrganizationEntity
    {
        public Guid organization_id { get; init; }
        public string name { get; init; } = null!;
        public string slug { get; init; } = null!;
        public string? description { get; init; }
        public string? settings { get; init; }
        public int status { get; init; }
        public DateTimeOffset created_at { get; init; }
        public DateTimeOffset updated_at { get; init; }
    }
}
