using System.Text.RegularExpressions;
using Dapper;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Exceptions;
using MorphDB.Core.Models;
using Npgsql;

namespace MorphDB.Npgsql.Repositories;

/// <summary>
/// PostgreSQL repository for managing projects in the global control plane schema (morphdb).
/// </summary>
public sealed partial class ProjectRepository : IProjectRepository
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly ISchemaNameResolver _schemaNameResolver;
    private const string ProjectsTable = "morphdb._morph_projects";

    public ProjectRepository(
        NpgsqlDataSource dataSource,
        ISchemaNameResolver schemaNameResolver)
    {
        _dataSource = dataSource;
        _schemaNameResolver = schemaNameResolver;
    }

    /// <inheritdoc/>
    public async Task<Project> CreateAsync(
        CreateProjectRequest request,
        CancellationToken cancellationToken = default)
    {
        var projectId = request.ProjectId ?? Guid.NewGuid();
        var slug = request.Slug ?? GenerateSlug(request.Name);
        var schemaNames = _schemaNameResolver.GetSchemaNames(projectId);

        // Only a caller that chose the id can collide here — a generated one cannot, so this costs a
        // query nobody needed until the id became something a request can carry. The catch below
        // answers the same collision when two callers race for it; this answers the ordinary case
        // without depending on what the primary key constraint is named.
        if (request.ProjectId is not null && await ProjectIdExistsAsync(projectId, cancellationToken))
        {
            throw new DuplicateProjectIdException(projectId);
        }

        // Check slug availability
        if (!await IsSlugAvailableAsync(slug, cancellationToken))
        {
            throw new DuplicateSlugException(slug);
        }

        const string sql = """
            INSERT INTO morphdb._morph_projects (
                project_id, name, slug, system_schema, data_schema,
                settings, status, created_at, updated_at
            )
            VALUES (
                @ProjectId, @Name, @Slug, @SystemSchema, @DataSchema,
                @Settings::jsonb, @Status, NOW(), NOW()
            )
            RETURNING *
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        try
        {
            var entity = await connection.QuerySingleAsync<ProjectEntity>(sql, new
            {
                ProjectId = projectId,
                request.Name,
                Slug = slug,
                schemaNames.SystemSchema,
                schemaNames.DataSchema,
                Settings = ProjectSettingsColumn.Serialize(request.Settings),
                Status = (int)ProjectStatus.Provisioning
            });

            return MapToProject(entity);
        }
        catch (PostgresException ex) when (IsProjectIdCollision(ex))
        {
            // The check above is a check-then-insert, so two callers choosing the same id can both
            // pass it. The key is what actually decides, and the caller's answer must not depend on
            // which of the two paths refused them — a start-up step that re-runs is exactly the
            // caller who can meet the race.
            //
            // Only the primary key is read this way. The table is unique on slug and on both schema
            // names as well, and answering "that id is taken" to a slug collision would name the
            // wrong field as the one to change.
            throw new DuplicateProjectIdException(projectId);
        }
    }

    /// <inheritdoc/>
    public async Task<Project?> GetByIdAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT * FROM morphdb._morph_projects
            WHERE project_id = @ProjectId AND status != @DeletedStatus
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        var entity = await connection.QuerySingleOrDefaultAsync<ProjectEntity>(sql, new
        {
            ProjectId = projectId,
            DeletedStatus = (int)ProjectStatus.Deleted
        });

        return entity is null ? null : MapToProject(entity);
    }

    /// <inheritdoc/>
    public async Task<Project?> GetBySlugAsync(
        string slug,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT * FROM morphdb._morph_projects
            WHERE slug = @Slug AND status != @DeletedStatus
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        var entity = await connection.QuerySingleOrDefaultAsync<ProjectEntity>(sql, new
        {
            Slug = slug,
            DeletedStatus = (int)ProjectStatus.Deleted
        });

        return entity is null ? null : MapToProject(entity);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Project>> ListAsync(
        ProjectStatus? status = null,
        int offset = 0,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var sql = """
            SELECT * FROM morphdb._morph_projects
            WHERE status != @DeletedStatus
            """;

        var parameters = new DynamicParameters();
        parameters.Add("DeletedStatus", (int)ProjectStatus.Deleted);
        parameters.Add("Offset", offset);
        parameters.Add("Limit", limit);

        if (status.HasValue)
        {
            sql += " AND status = @Status";
            parameters.Add("Status", (int)status.Value);
        }

        sql += " ORDER BY created_at DESC OFFSET @Offset LIMIT @Limit";

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        var entities = await connection.QueryAsync<ProjectEntity>(sql, parameters);

        return entities.Select(MapToProject).ToList();
    }

    /// <inheritdoc/>
    public async Task<Project> UpdateAsync(
        UpdateProjectRequest request,
        CancellationToken cancellationToken = default)
    {
        var existing = await GetByIdAsync(request.ProjectId, cancellationToken)
            ?? throw new ProjectNotFoundException(request.ProjectId);

        var updates = new List<string> { "updated_at = NOW()" };
        var parameters = new DynamicParameters();
        parameters.Add("ProjectId", request.ProjectId);

        if (request.Name is not null)
        {
            updates.Add("name = @Name");
            parameters.Add("Name", request.Name);
        }

        if (request.Settings is not null)
        {
            updates.Add("settings = @Settings::jsonb");
            parameters.Add("Settings", ProjectSettingsColumn.Serialize(request.Settings));
        }

        var sql = $"""
            UPDATE morphdb._morph_projects
            SET {string.Join(", ", updates)}
            WHERE project_id = @ProjectId
            RETURNING *
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        var entity = await connection.QuerySingleAsync<ProjectEntity>(sql, parameters);

        return MapToProject(entity);
    }

    /// <inheritdoc/>
    public async Task UpdateStatusAsync(
        Guid projectId,
        ProjectStatus status,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE morphdb._morph_projects
            SET status = @Status, updated_at = NOW()
            WHERE project_id = @ProjectId
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(sql, new
        {
            ProjectId = projectId,
            Status = (int)status
        });
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        await UpdateStatusAsync(projectId, ProjectStatus.Deleted, cancellationToken);
    }

    /// <summary>
    /// Whether a unique violation came from the project id rather than one of the table's other
    /// unique columns. The constraint name is the only thing that distinguishes them, so a server
    /// whose primary key was created under a different name would fall through to the generic
    /// answer — wrong, but not misleading.
    /// </summary>
    private static bool IsProjectIdCollision(PostgresException ex) =>
        ex.SqlState == PostgresErrorCodes.UniqueViolation
        && ex.ConstraintName?.EndsWith("_pkey", StringComparison.Ordinal) == true;

    /// <summary>
    /// Whether the id is taken, deleted projects included. Deleting a project sets its status
    /// rather than removing the row, so an id freed by a delete is still held by the primary key —
    /// reading availability the way slugs read it would let the insert fail underneath.
    /// </summary>
    private async Task<bool> ProjectIdExistsAsync(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT EXISTS(SELECT 1 FROM morphdb._morph_projects WHERE project_id = @ProjectId)
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        return await connection.ExecuteScalarAsync<bool>(sql, new { ProjectId = projectId });
    }

    /// <inheritdoc/>
    public async Task<bool> IsSlugAvailableAsync(
        string slug,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT NOT EXISTS(
                SELECT 1 FROM morphdb._morph_projects
                WHERE slug = @Slug AND status != @DeletedStatus
            )
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        return await connection.ExecuteScalarAsync<bool>(sql, new
        {
            Slug = slug,
            DeletedStatus = (int)ProjectStatus.Deleted
        });
    }

    /// <inheritdoc/>
    public async Task<int> CountAsync(
        ProjectStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        var sql = "SELECT COUNT(*) FROM morphdb._morph_projects WHERE status != @DeletedStatus";

        var parameters = new DynamicParameters();
        parameters.Add("DeletedStatus", (int)ProjectStatus.Deleted);

        if (status.HasValue)
        {
            sql += " AND status = @Status";
            parameters.Add("Status", (int)status.Value);
        }

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        return await connection.ExecuteScalarAsync<int>(sql, parameters);
    }

    private static string GenerateSlug(string name)
    {
        // Convert to lowercase, replace spaces with dashes, remove special chars
        var slug = name.ToLowerInvariant()
            .Replace(" ", "-")
            .Replace("_", "-");

        // Remove non-alphanumeric characters except dashes
        slug = SlugCleanupPattern().Replace(slug, "");

        // Remove consecutive dashes
        slug = MultipleDashPattern().Replace(slug, "-");

        // Trim dashes from start/end
        slug = slug.Trim('-');

        // Ensure minimum length
        if (slug.Length < 3)
        {
            slug += "-project";
        }

        return slug;
    }

    private static Project MapToProject(ProjectEntity entity)
    {
        var settings = ProjectSettingsColumn.Deserialize(entity.Settings);

        return new Project
        {
            ProjectId = entity.ProjectId,
            Name = entity.Name,
            Slug = entity.Slug,
            SystemSchema = entity.SystemSchema,
            DataSchema = entity.DataSchema,
            Settings = settings,
            Status = (ProjectStatus)entity.Status,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt
        };
    }

    [GeneratedRegex(@"[^a-z0-9\-]")]
    private static partial Regex SlugCleanupPattern();

    [GeneratedRegex(@"-+")]
    private static partial Regex MultipleDashPattern();

    // Entity class for Dapper mapping
    private sealed class ProjectEntity
    {
        public Guid ProjectId { get; init; }
        public string Name { get; init; } = default!;
        public string Slug { get; init; } = default!;
        public string SystemSchema { get; init; } = default!;
        public string DataSchema { get; init; } = default!;
        public string? Settings { get; init; }
        public int Status { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
        public DateTimeOffset UpdatedAt { get; init; }
    }
}
