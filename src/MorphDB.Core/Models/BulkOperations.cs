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
