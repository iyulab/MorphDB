namespace MorphDB.Client.Models;

/// <summary>
/// CSV import options.
/// </summary>
public sealed class CsvImportOptions
{
    /// <summary>
    /// Field delimiter character. Default is comma.
    /// </summary>
    public char Delimiter { get; init; } = ',';

    /// <summary>
    /// Whether the CSV has a header row.
    /// </summary>
    public bool HasHeader { get; init; } = true;

    /// <summary>
    /// Date format for parsing date columns.
    /// </summary>
    public string? DateFormat { get; init; }

    /// <summary>
    /// Whether to trim whitespace from values.
    /// </summary>
    public bool TrimWhitespace { get; init; } = true;

    /// <summary>
    /// How to handle null values.
    /// </summary>
    public string NullHandling { get; init; } = "empty-as-null";

    /// <summary>
    /// How to handle duplicate records.
    /// </summary>
    public string DuplicateHandling { get; init; } = "insert";

    /// <summary>
    /// Key columns for duplicate detection.
    /// </summary>
    public IReadOnlyList<string>? KeyColumns { get; init; }
}

/// <summary>
/// JSON import options.
/// </summary>
public sealed class JsonImportOptions
{
    /// <summary>
    /// JSON path to extract records from.
    /// </summary>
    public string? JsonPath { get; init; }

    /// <summary>
    /// Date format for parsing date columns.
    /// </summary>
    public string? DateFormat { get; init; }

    /// <summary>
    /// How to handle duplicate records.
    /// </summary>
    public string DuplicateHandling { get; init; } = "insert";

    /// <summary>
    /// Key columns for duplicate detection.
    /// </summary>
    public IReadOnlyList<string>? KeyColumns { get; init; }
}

/// <summary>
/// CSV export options.
/// </summary>
public sealed class CsvExportOptions
{
    /// <summary>
    /// Columns to export. If empty, all columns are exported.
    /// </summary>
    public IReadOnlyList<string>? Columns { get; init; }

    /// <summary>
    /// Filter expression.
    /// </summary>
    public string? Filter { get; init; }

    /// <summary>
    /// Order by expression.
    /// </summary>
    public string? OrderBy { get; init; }

    /// <summary>
    /// Field delimiter character.
    /// </summary>
    public char Delimiter { get; init; } = ',';

    /// <summary>
    /// Whether to include a header row.
    /// </summary>
    public bool IncludeHeader { get; init; } = true;

    /// <summary>
    /// Date format for date columns.
    /// </summary>
    public string? DateFormat { get; init; }
}

/// <summary>
/// JSON export options.
/// </summary>
public sealed class JsonExportOptions
{
    /// <summary>
    /// Columns to export. If empty, all columns are exported.
    /// </summary>
    public IReadOnlyList<string>? Columns { get; init; }

    /// <summary>
    /// Filter expression.
    /// </summary>
    public string? Filter { get; init; }

    /// <summary>
    /// Order by expression.
    /// </summary>
    public string? OrderBy { get; init; }

    /// <summary>
    /// Whether to format JSON with indentation.
    /// </summary>
    public bool Pretty { get; init; }

    /// <summary>
    /// Date format for date columns.
    /// </summary>
    public string? DateFormat { get; init; }
}

/// <summary>
/// XLSX export options.
/// </summary>
public sealed class XlsxExportOptions
{
    /// <summary>
    /// Columns to export. If empty, all columns are exported.
    /// </summary>
    public IReadOnlyList<string>? Columns { get; init; }

    /// <summary>
    /// Filter expression.
    /// </summary>
    public string? Filter { get; init; }

    /// <summary>
    /// Order by expression.
    /// </summary>
    public string? OrderBy { get; init; }

    /// <summary>
    /// Sheet name.
    /// </summary>
    public string SheetName { get; init; } = "Sheet1";

    /// <summary>
    /// Date format for date columns.
    /// </summary>
    public string? DateFormat { get; init; }
}

/// <summary>
/// Import job status.
/// </summary>
public sealed class ImportJobStatus
{
    /// <summary>
    /// Job ID.
    /// </summary>
    public Guid JobId { get; init; }

    /// <summary>
    /// Table name.
    /// </summary>
    public required string TableName { get; init; }

    /// <summary>
    /// Import format.
    /// </summary>
    public required string Format { get; init; }

    /// <summary>
    /// Job status.
    /// </summary>
    public required string Status { get; init; }

    /// <summary>
    /// Total number of rows.
    /// </summary>
    public long TotalRows { get; init; }

    /// <summary>
    /// Number of processed rows.
    /// </summary>
    public long ProcessedRows { get; init; }

    /// <summary>
    /// Number of successfully imported rows.
    /// </summary>
    public long SuccessCount { get; init; }

    /// <summary>
    /// Number of failed rows.
    /// </summary>
    public long ErrorCount { get; init; }

    /// <summary>
    /// Error message if failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Per-row failure reasons, capped at the first 100 failures.
    /// </summary>
    public IReadOnlyList<ImportRowError>? ErrorDetails { get; init; }

    /// <summary>
    /// True when <see cref="ErrorCount"/> exceeds the number of entries kept in <see cref="ErrorDetails"/>.
    /// </summary>
    public bool ErrorDetailsTruncated { get; init; }

    /// <summary>
    /// Creation timestamp.
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Start timestamp.
    /// </summary>
    public DateTimeOffset? StartedAt { get; init; }

    /// <summary>
    /// Completion timestamp.
    /// </summary>
    public DateTimeOffset? CompletedAt { get; init; }

    /// <summary>
    /// Progress percentage.
    /// </summary>
    public double PercentComplete => TotalRows > 0 ? (double)ProcessedRows / TotalRows * 100 : 0;
}

/// <summary>
/// Why a single row failed during a bulk import.
/// </summary>
public sealed class ImportRowError
{
    /// <summary>
    /// 1-based row number within the imported file.
    /// </summary>
    public long RowNumber { get; init; }

    /// <summary>
    /// The write pipeline's error message for this row.
    /// </summary>
    public required string Error { get; init; }
}

/// <summary>
/// Export job status.
/// </summary>
public sealed class ExportJobStatus
{
    /// <summary>
    /// Job ID.
    /// </summary>
    public Guid JobId { get; init; }

    /// <summary>
    /// Table name.
    /// </summary>
    public required string TableName { get; init; }

    /// <summary>
    /// Export format.
    /// </summary>
    public required string Format { get; init; }

    /// <summary>
    /// Job status.
    /// </summary>
    public required string Status { get; init; }

    /// <summary>
    /// Total number of rows.
    /// </summary>
    public long TotalRows { get; init; }

    /// <summary>
    /// Number of processed rows.
    /// </summary>
    public long ProcessedRows { get; init; }

    /// <summary>
    /// Download URL when completed.
    /// </summary>
    public string? DownloadUrl { get; init; }

    /// <summary>
    /// File size in bytes.
    /// </summary>
    public long? FileSize { get; init; }

    /// <summary>
    /// Error message if failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Creation timestamp.
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Start timestamp.
    /// </summary>
    public DateTimeOffset? StartedAt { get; init; }

    /// <summary>
    /// Completion timestamp.
    /// </summary>
    public DateTimeOffset? CompletedAt { get; init; }

    /// <summary>
    /// Expiration timestamp.
    /// </summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>
    /// Progress percentage.
    /// </summary>
    public double PercentComplete => TotalRows > 0 ? (double)ProcessedRows / TotalRows * 100 : 0;
}
