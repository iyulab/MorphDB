namespace MorphDB.Client.Models;

/// <summary>
/// Settings a project carries.
/// <para>
/// The defaults here are the server's defaults, and that is load-bearing rather than convenient:
/// settings are stored by replacement, so whatever this object holds when it is sent is what the
/// project ends up with. Constructing one and setting a single field therefore writes the defaults
/// for everything else — which is exactly what the server does with a partial body. Mirroring the
/// defaults is what makes the object you build and the object that gets stored the same object.
/// </para>
/// </summary>
public sealed class ProjectSettings
{
    /// <summary>Default locale for the project. Null leaves it unset.</summary>
    public string? DefaultLocale { get; init; }

    /// <summary>Time zone for the project. Null leaves it unset.</summary>
    public string? Timezone { get; init; }

    /// <summary>Whether writes are recorded in the audit log.</summary>
    public bool EnableAuditLog { get; init; } = true;

    /// <summary>Days of audit history to keep. Null keeps everything.</summary>
    public int? AuditLogRetentionDays { get; init; }

    /// <summary>
    /// Whether relations created without stating <c>enforceOnWrite</c> are checked on write.
    /// A relation that states its own value overrides this.
    /// </summary>
    public bool DefaultEnforceOnWrite { get; init; } = true;

    /// <summary>Arbitrary project metadata.</summary>
    public Dictionary<string, string>? Metadata { get; init; }
}

/// <summary>
/// A project as the server reports it.
/// </summary>
public sealed class ProjectInfo
{
    /// <summary>Project id — the value that goes in the <c>X-Project-Id</c> header.</summary>
    public Guid Id { get; init; }

    /// <summary>Human-readable name.</summary>
    public required string Name { get; init; }

    /// <summary>URL-safe unique identifier.</summary>
    public required string Slug { get; init; }

    /// <summary>Physical schema holding the project's own bookkeeping.</summary>
    public required string SystemSchema { get; init; }

    /// <summary>Physical schema holding the project's tables.</summary>
    public required string DataSchema { get; init; }

    /// <summary>Lifecycle status, lower-case (for example <c>active</c>).</summary>
    public required string Status { get; init; }

    /// <summary>Settings in force. Null when the project never stated any.</summary>
    public ProjectSettings? Settings { get; init; }

    /// <summary>When the project was created.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>When the project was last updated.</summary>
    public DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>
/// Request to create a project.
/// </summary>
public sealed class CreateProjectRequest
{
    /// <summary>
    /// Id to create the project under. Null lets the server generate one.
    /// <para>
    /// Supplying it lets a deployment name the project in its own configuration instead of
    /// discovering the id at run time. A server that already has a project under this id answers
    /// with a conflict rather than creating a second one.
    /// </para>
    /// </summary>
    public Guid? ProjectId { get; init; }

    /// <summary>Human-readable name.</summary>
    public required string Name { get; init; }

    /// <summary>URL-safe identifier. Null derives one from the name.</summary>
    public string? Slug { get; init; }

    /// <summary>
    /// Settings to create the project with. Null accepts the server's defaults.
    /// </summary>
    public ProjectSettings? Settings { get; init; }
}

/// <summary>
/// Request to update a project.
/// </summary>
public sealed class UpdateProjectRequest
{
    /// <summary>New name. Null leaves the name alone.</summary>
    public string? Name { get; init; }

    /// <summary>
    /// Settings to store. Null leaves the stored settings alone — but a non-null value
    /// <b>replaces them entirely</b> rather than merging into them.
    /// <para>
    /// Send the settings the project should end up with, not the ones you want to change. There is
    /// no way to express "leave this one field as it is": every field of the object you send is
    /// written, including the ones you never touched. Read the current settings from
    /// <see cref="ProjectInfo.Settings"/> first if you mean to preserve them.
    /// </para>
    /// </summary>
    public ProjectSettings? Settings { get; init; }
}

/// <summary>
/// Size and object counts for one physical schema.
/// </summary>
public sealed class SchemaStats
{
    /// <summary>Physical schema name.</summary>
    public required string SchemaName { get; init; }

    /// <summary>Number of tables.</summary>
    public int TableCount { get; init; }

    /// <summary>Number of indexes.</summary>
    public int IndexCount { get; init; }

    /// <summary>Total size on disk, in bytes.</summary>
    public long TotalSizeBytes { get; init; }

    /// <summary>Size of the data, in bytes.</summary>
    public long DataSizeBytes { get; init; }

    /// <summary>Size of the indexes, in bytes.</summary>
    public long IndexSizeBytes { get; init; }

    /// <summary>When the schema was last modified, when the server can tell.</summary>
    public DateTimeOffset? LastModified { get; init; }
}

/// <summary>
/// Storage statistics for a project, across both of its schemas.
/// </summary>
public sealed class ProjectStats
{
    /// <summary>Project the statistics are for.</summary>
    public Guid ProjectId { get; init; }

    /// <summary>Statistics for the project's system schema.</summary>
    public required SchemaStats SystemSchemaStats { get; init; }

    /// <summary>Statistics for the project's data schema.</summary>
    public required SchemaStats DataSchemaStats { get; init; }

    /// <summary>Total size across both schemas, in bytes.</summary>
    public long TotalSizeBytes { get; init; }

    /// <summary>Total number of tables across both schemas.</summary>
    public int TotalTableCount { get; init; }
}

/// <summary>
/// One problem found while checking a project's schemas.
/// </summary>
public sealed class SchemaHealthIssue
{
    /// <summary>Machine-readable issue code.</summary>
    public required string Code { get; init; }

    /// <summary>What is wrong, in the server's words.</summary>
    public required string Message { get; init; }

    /// <summary>Severity, lower-case (for example <c>warning</c>).</summary>
    public required string Severity { get; init; }

    /// <summary>The object the issue is about, when the server can name one.</summary>
    public string? AffectedObject { get; init; }
}

/// <summary>
/// The result of checking a project's schemas against its metadata.
/// </summary>
public sealed class SchemaHealthReport
{
    /// <summary>Project the report is for.</summary>
    public Guid ProjectId { get; init; }

    /// <summary>Whether the check found nothing wrong.</summary>
    public bool IsHealthy { get; init; }

    /// <summary>Everything the check found.</summary>
    public required IReadOnlyList<SchemaHealthIssue> Issues { get; init; }

    /// <summary>When the check ran.</summary>
    public DateTimeOffset CheckedAt { get; init; }
}
