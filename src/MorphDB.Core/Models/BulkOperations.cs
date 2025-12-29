using System.Text.Json;

namespace MorphDB.Core.Models;

/// <summary>
/// Represents a bulk import job.
/// </summary>
public sealed class BulkImportJob
{
    public Guid JobId { get; init; }
    public Guid TenantId { get; init; }
    public Guid TableId { get; init; }
    public required string TableName { get; init; }
    public required ImportFormat Format { get; init; }
    public BulkJobStatus Status { get; init; } = BulkJobStatus.Pending;
    public long TotalRows { get; init; }
    public long ProcessedRows { get; init; }
    public long SuccessCount { get; init; }
    public long ErrorCount { get; init; }
    public string? ErrorMessage { get; init; }
    public JsonDocument? Options { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
}

/// <summary>
/// Represents a bulk export job.
/// </summary>
public sealed class BulkExportJob
{
    public Guid JobId { get; init; }
    public Guid TenantId { get; init; }
    public Guid TableId { get; init; }
    public required string TableName { get; init; }
    public required ExportFormat Format { get; init; }
    public BulkJobStatus Status { get; init; } = BulkJobStatus.Pending;
    public long TotalRows { get; init; }
    public long ProcessedRows { get; init; }
    public string? FilePath { get; init; }
    public long? FileSize { get; init; }
    public string? ErrorMessage { get; init; }
    public JsonDocument? Options { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
}

/// <summary>
/// Supported import formats.
/// </summary>
public enum ImportFormat
{
    Csv,
    Json,
    Ndjson
}

/// <summary>
/// Supported export formats.
/// </summary>
public enum ExportFormat
{
    Csv,
    Json,
    Xlsx
}

/// <summary>
/// Status of a bulk job.
/// </summary>
public enum BulkJobStatus
{
    Pending,
    Processing,
    Completed,
    Failed,
    Cancelled
}

/// <summary>
/// Options for CSV import.
/// </summary>
public sealed record CsvImportOptions
{
    public char Delimiter { get; init; } = ',';
    public bool HasHeader { get; init; } = true;
    public string? DateFormat { get; init; }
    public bool TrimWhitespace { get; init; } = true;
    public NullHandling NullHandling { get; init; } = NullHandling.EmptyAsNull;
    public DuplicateHandling DuplicateHandling { get; init; } = DuplicateHandling.Insert;
    public IReadOnlyList<string>? KeyColumns { get; init; }
}

/// <summary>
/// Options for JSON/NDJSON import.
/// </summary>
public sealed record JsonImportOptions
{
    public string? DateFormat { get; init; }
    public DuplicateHandling DuplicateHandling { get; init; } = DuplicateHandling.Insert;
    public IReadOnlyList<string>? KeyColumns { get; init; }
}

/// <summary>
/// Options for CSV export.
/// </summary>
public sealed record CsvExportOptions
{
    public char Delimiter { get; init; } = ',';
    public bool IncludeHeader { get; init; } = true;
    public string? DateFormat { get; init; }
    public IReadOnlyList<string>? Columns { get; init; }
    public string? Filter { get; init; }
    public string? OrderBy { get; init; }
}

/// <summary>
/// Options for JSON export.
/// </summary>
public sealed record JsonExportOptions
{
    public bool Pretty { get; init; }
    public string? DateFormat { get; init; }
    public IReadOnlyList<string>? Columns { get; init; }
    public string? Filter { get; init; }
    public string? OrderBy { get; init; }
}

/// <summary>
/// Options for XLSX export.
/// </summary>
public sealed record XlsxExportOptions
{
    public string SheetName { get; init; } = "Data";
    public bool IncludeHeader { get; init; } = true;
    public IReadOnlyList<string>? Columns { get; init; }
    public string? Filter { get; init; }
    public string? OrderBy { get; init; }
}

/// <summary>
/// How to handle null values during import.
/// </summary>
public enum NullHandling
{
    EmptyAsNull,
    PreserveEmpty,
    NullStringAsNull
}

/// <summary>
/// How to handle duplicate keys during import.
/// </summary>
public enum DuplicateHandling
{
    Insert,
    Update,
    Upsert,
    Skip,
    Error
}

/// <summary>
/// Result of a single row import.
/// </summary>
public sealed record ImportRowResult
{
    public long RowNumber { get; init; }
    public bool Success { get; init; }
    public Guid? RecordId { get; init; }
    public string? Error { get; init; }
}

/// <summary>
/// Progress update for a bulk job.
/// </summary>
public sealed record BulkJobProgress
{
    public Guid JobId { get; init; }
    public BulkJobStatus Status { get; init; }
    public long TotalRows { get; init; }
    public long ProcessedRows { get; init; }
    public long SuccessCount { get; init; }
    public long ErrorCount { get; init; }
    public double PercentComplete => TotalRows > 0 ? (double)ProcessedRows / TotalRows * 100 : 0;
    public TimeSpan? EstimatedTimeRemaining { get; init; }
}

/// <summary>
/// Options for controlling virtual constraint validation during write operations.
/// </summary>
public sealed record WriteOptions
{
    /// <summary>
    /// Default options with all validations enabled.
    /// </summary>
    public static WriteOptions Default { get; } = new();

    /// <summary>
    /// Options for bulk import with deferred validation.
    /// </summary>
    public static WriteOptions BulkImport { get; } = new()
    {
        ValidateRequired = true,
        ValidateForeignKeys = false,
        ValidateUnique = false,
        ValidateCheck = false,
        ApplyDefaults = true,
        ApplyTimestamps = true,
        ApplyVersion = false,
        DeferValidation = true
    };

    /// <summary>
    /// Options that skip all validation (use with caution).
    /// </summary>
    public static WriteOptions NoValidation { get; } = new()
    {
        ValidateRequired = false,
        ValidateForeignKeys = false,
        ValidateUnique = false,
        ValidateCheck = false,
        ApplyDefaults = false,
        ApplyTimestamps = false,
        ApplyVersion = false
    };

    /// <summary>
    /// When true, validates required fields (virtual NOT NULL).
    /// </summary>
    public bool ValidateRequired { get; init; } = true;

    /// <summary>
    /// When true, validates foreign key references exist.
    /// </summary>
    public bool ValidateForeignKeys { get; init; } = true;

    /// <summary>
    /// When true, validates unique constraints.
    /// </summary>
    public bool ValidateUnique { get; init; } = true;

    /// <summary>
    /// When true, validates check constraints.
    /// </summary>
    public bool ValidateCheck { get; init; } = true;

    /// <summary>
    /// When true, applies default values for missing fields.
    /// </summary>
    public bool ApplyDefaults { get; init; } = true;

    /// <summary>
    /// When true, auto-manages _created_at and _updated_at.
    /// </summary>
    public bool ApplyTimestamps { get; init; } = true;

    /// <summary>
    /// When true, auto-manages _version for optimistic locking.
    /// </summary>
    public bool ApplyVersion { get; init; } = true;

    /// <summary>
    /// When true, auto-manages _created_by and _updated_by.
    /// </summary>
    public bool ApplyAuditFields { get; init; } = true;

    /// <summary>
    /// When true, auto-manages _owner_id for ownership-enabled tables.
    /// </summary>
    public bool ApplyOwnership { get; init; } = true;

    /// <summary>
    /// When true, auto-manages _sort_order for hierarchy-enabled tables.
    /// </summary>
    public bool ApplySortOrder { get; init; } = true;

    /// <summary>
    /// When true, validation is deferred until after all rows are inserted.
    /// Useful for bulk imports where post-import validation is preferred.
    /// </summary>
    public bool DeferValidation { get; init; }

    /// <summary>
    /// Expected version for optimistic locking (null to skip check).
    /// </summary>
    public int? ExpectedVersion { get; init; }
}

/// <summary>
/// Result of a write operation with validation details.
/// </summary>
public sealed record WriteResult
{
    /// <summary>
    /// Whether the operation succeeded.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// The written record (with generated values like ID, timestamps).
    /// </summary>
    public IDictionary<string, object?>? Data { get; init; }

    /// <summary>
    /// Validation errors if the operation failed.
    /// </summary>
    public IReadOnlyList<ValidationError> Errors { get; init; } = [];

    /// <summary>
    /// New version number after update (for optimistic locking).
    /// </summary>
    public int? NewVersion { get; init; }

    public static WriteResult Ok(IDictionary<string, object?> data, int? newVersion = null) =>
        new() { Success = true, Data = data, NewVersion = newVersion };

    public static WriteResult Failed(params ValidationError[] errors) =>
        new() { Success = false, Errors = errors };
}

/// <summary>
/// Represents a validation error.
/// </summary>
public sealed record ValidationError
{
    public required string Field { get; init; }
    public required string Code { get; init; }
    public required string Message { get; init; }
    public object? AttemptedValue { get; init; }
}

/// <summary>
/// Well-known validation error codes.
/// </summary>
public static class ValidationErrorCodes
{
    public const string Required = "REQUIRED";
    public const string ForeignKeyViolation = "FK_VIOLATION";
    public const string UniqueViolation = "UNIQUE_VIOLATION";
    public const string CheckViolation = "CHECK_VIOLATION";
    public const string TypeMismatch = "TYPE_MISMATCH";
    public const string VersionConflict = "VERSION_CONFLICT";
    public const string InvalidValue = "INVALID_VALUE";
    public const string NotFound = "NOT_FOUND";
}
