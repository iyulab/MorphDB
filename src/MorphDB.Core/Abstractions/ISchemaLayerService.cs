using MorphDB.Core.Models;

namespace MorphDB.Core.Abstractions;

/// <summary>
/// Manages PostgreSQL schema lifecycle for projects.
/// Handles creation, provisioning, and deletion of project schemas.
/// </summary>
public interface ISchemaLayerService
{
    /// <summary>
    /// Creates both system and data schemas for a new project.
    /// Also creates the required system tables in the system schema.
    /// </summary>
    /// <param name="projectId">The project ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The schema names that were created.</returns>
    Task<SchemaNames> ProvisionProjectSchemasAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Drops both schemas for a project (CASCADE).
    /// This is a destructive operation and should be used with caution.
    /// </summary>
    /// <param name="projectId">The project ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DropProjectSchemasAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a schema exists in the database.
    /// </summary>
    Task<bool> SchemaExistsAsync(
        string schemaName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if both project schemas exist.
    /// </summary>
    Task<SchemaExistenceResult> ProjectSchemasExistAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets schema statistics (table count, size, etc.).
    /// </summary>
    Task<SchemaStats> GetSchemaStatsAsync(
        string schemaName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets combined statistics for both project schemas.
    /// </summary>
    Task<ProjectSchemaStats> GetProjectStatsAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Renames a schema. Used during project migration.
    /// </summary>
    Task RenameSchemaAsync(
        string oldName,
        string newName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all MorphDB-managed schemas in the database.
    /// </summary>
    Task<IReadOnlyList<ManagedSchemaInfo>> ListManagedSchemasAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates schema health and integrity.
    /// </summary>
    Task<SchemaHealthReport> ValidateSchemaHealthAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of checking schema existence.
/// </summary>
public readonly record struct SchemaExistenceResult(
    bool SystemSchemaExists,
    bool DataSchemaExists)
{
    public bool BothExist => SystemSchemaExists && DataSchemaExists;
    public bool NeitherExist => !SystemSchemaExists && !DataSchemaExists;
    public bool PartiallyExists => SystemSchemaExists != DataSchemaExists;
}

/// <summary>
/// Statistics for a single schema.
/// </summary>
public sealed class SchemaStats
{
    public required string SchemaName { get; init; }
    public int TableCount { get; init; }
    public int IndexCount { get; init; }
    public long TotalSizeBytes { get; init; }
    public long DataSizeBytes { get; init; }
    public long IndexSizeBytes { get; init; }
    public DateTimeOffset? LastModified { get; init; }
}

/// <summary>
/// Combined statistics for both project schemas.
/// </summary>
public sealed class ProjectSchemaStats
{
    public Guid ProjectId { get; init; }
    public required SchemaStats SystemSchemaStats { get; init; }
    public required SchemaStats DataSchemaStats { get; init; }
    public long TotalSizeBytes => SystemSchemaStats.TotalSizeBytes + DataSchemaStats.TotalSizeBytes;
    public int TotalTableCount => SystemSchemaStats.TableCount + DataSchemaStats.TableCount;
}

/// <summary>
/// Information about a managed schema.
/// </summary>
public sealed class ManagedSchemaInfo
{
    public required string SchemaName { get; init; }
    public SchemaType SchemaType { get; init; }
    public Guid? ProjectId { get; init; }
    public int TableCount { get; init; }
    public long SizeBytes { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>
/// Report on schema health and integrity.
/// </summary>
public sealed class SchemaHealthReport
{
    public Guid ProjectId { get; init; }
    public bool IsHealthy { get; init; }
    public required IReadOnlyList<SchemaHealthIssue> Issues { get; init; }
    public DateTimeOffset CheckedAt { get; init; }
}

/// <summary>
/// A single health issue detected in schema validation.
/// </summary>
public sealed class SchemaHealthIssue
{
    public required string Code { get; init; }
    public required string Message { get; init; }
    public SchemaHealthSeverity Severity { get; init; }
    public string? AffectedObject { get; init; }
}

/// <summary>
/// Severity level for schema health issues.
/// </summary>
public enum SchemaHealthSeverity
{
    Info = 0,
    Warning = 1,
    Error = 2,
    Critical = 3
}
