using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using ClosedXML.Excel;
using Dapper;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Exceptions;
using MorphDB.Core.Models;
using MorphDB.Core.Pipeline;
using Npgsql;

namespace MorphDB.Npgsql.Services;

/// <summary>
/// PostgreSQL implementation of bulk import/export operations.
/// Uses WritePipeline with BulkImport options for optimized validation.
/// </summary>
public sealed class PostgresBulkOperationService : IBulkOperationService
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly ISchemaManager _schemaManager;
    private readonly IMorphDataService _dataService;
    private readonly IWritePipeline? _writePipeline;
    private readonly BulkOperationOptions _options;

    public PostgresBulkOperationService(
        NpgsqlDataSource dataSource,
        ISchemaManager schemaManager,
        IMorphDataService dataService,
        BulkOperationOptions options,
        IWritePipeline? writePipeline = null)
    {
        _dataSource = dataSource;
        _schemaManager = schemaManager;
        _dataService = dataService;
        _writePipeline = writePipeline;
        _options = options;
    }

    #region Import Operations

    public async Task<BulkImportJob> StartCsvImportAsync(
        Guid projectId,
        string tableName,
        Stream dataStream,
        CsvImportOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new CsvImportOptions();

        var table = await _schemaManager.GetTableAsync(projectId, tableName, cancellationToken)
            ?? throw new TableNotFoundException(tableName);

        var jobId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        // Create job record first (required for foreign key constraint)
        var job = new BulkImportJob
        {
            JobId = jobId,
            ProjectId = projectId,
            TableId = table.TableId,
            TableName = tableName,
            Format = ImportFormat.Csv,
            Status = BulkJobStatus.Pending,
            Options = JsonDocument.Parse(JsonSerializer.Serialize(options)),
            CreatedAt = now
        };

        await SaveImportJobAsync(job, cancellationToken);

        // Store the stream for processing (after job record exists)
        await StoreImportDataAsync(jobId, dataStream, cancellationToken);

        return job;
    }

    public async Task<BulkImportJob> StartJsonImportAsync(
        Guid projectId,
        string tableName,
        Stream dataStream,
        JsonImportOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new JsonImportOptions();

        var table = await _schemaManager.GetTableAsync(projectId, tableName, cancellationToken)
            ?? throw new TableNotFoundException(tableName);

        var jobId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        // Create job record first (required for foreign key constraint)
        var job = new BulkImportJob
        {
            JobId = jobId,
            ProjectId = projectId,
            TableId = table.TableId,
            TableName = tableName,
            Format = ImportFormat.Json,
            Status = BulkJobStatus.Pending,
            Options = JsonDocument.Parse(JsonSerializer.Serialize(options)),
            CreatedAt = now
        };

        await SaveImportJobAsync(job, cancellationToken);

        // Store the stream for processing (after job record exists)
        await StoreImportDataAsync(jobId, dataStream, cancellationToken);

        return job;
    }

    public async Task<BulkImportJob> StartNdjsonImportAsync(
        Guid projectId,
        string tableName,
        Stream dataStream,
        JsonImportOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new JsonImportOptions();

        var table = await _schemaManager.GetTableAsync(projectId, tableName, cancellationToken)
            ?? throw new TableNotFoundException(tableName);

        var jobId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        // Create job record first (required for foreign key constraint)
        var job = new BulkImportJob
        {
            JobId = jobId,
            ProjectId = projectId,
            TableId = table.TableId,
            TableName = tableName,
            Format = ImportFormat.Ndjson,
            Status = BulkJobStatus.Pending,
            Options = JsonDocument.Parse(JsonSerializer.Serialize(options)),
            CreatedAt = now
        };

        await SaveImportJobAsync(job, cancellationToken);

        // Store the stream for processing (after job record exists)
        await StoreImportDataAsync(jobId, dataStream, cancellationToken);

        return job;
    }

    public async IAsyncEnumerable<ImportRowResult> ProcessImportAsync(
        Guid jobId,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var job = await GetImportJobAsync(jobId, cancellationToken)
            ?? throw new NotFoundException("Import job", jobId.ToString());

        // Update status to processing
        await UpdateImportJobStatusAsync(jobId, BulkJobStatus.Processing, cancellationToken);

        var dataStream = await GetStoredImportDataAsync(jobId, cancellationToken);
        if (dataStream is null)
        {
            throw new InvalidOperationException($"Import data not found for job '{jobId}'");
        }

        await using (dataStream)
        {
            var rowNumber = 0L;
            var successCount = 0L;
            var errorCount = 0L;
            var errorDetails = new List<ImportRowError>();

            // Get table metadata for pipeline usage and, for CSV, for typed value coercion.
            var table = await _schemaManager.GetTableAsync(job.ProjectId, job.TableName, cancellationToken);

            var rows = job.Format switch
            {
                ImportFormat.Csv => ParseCsvAsync(dataStream, job, table, cancellationToken),
                ImportFormat.Json => ParseJsonArrayAsync(dataStream, cancellationToken),
                ImportFormat.Ndjson => ParseNdjsonAsync(dataStream, cancellationToken),
                _ => throw new NotSupportedException($"Format {job.Format} not supported")
            };

            await foreach (var row in rows.WithCancellation(cancellationToken))
            {
                rowNumber++;
                var result = await ProcessImportRowAsync(job, table, row, rowNumber);

                if (result.Success)
                {
                    successCount++;
                }
                else
                {
                    errorCount++;
                    if (errorDetails.Count < BulkImportJob.MaxErrorDetails && result.Error is not null)
                    {
                        errorDetails.Add(new ImportRowError { RowNumber = rowNumber, Error = result.Error });
                    }
                }

                // Update progress periodically
                if (rowNumber % _options.ProgressUpdateInterval == 0)
                {
                    await UpdateImportProgressAsync(jobId, rowNumber, successCount, errorCount, cancellationToken);
                }

                yield return result;
            }

            // Final update
            await UpdateImportJobCompletedAsync(jobId, rowNumber, successCount, errorCount, errorDetails, cancellationToken);
        }

        // Cleanup stored data
        await DeleteStoredImportDataAsync(jobId, cancellationToken);
    }

    #endregion

    #region Export Operations

    public async Task<BulkExportJob> StartCsvExportAsync(
        Guid projectId,
        string tableName,
        CsvExportOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new CsvExportOptions();

        var table = await _schemaManager.GetTableAsync(projectId, tableName, cancellationToken)
            ?? throw new TableNotFoundException(tableName);

        var jobId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        // Count total rows for progress tracking
        var totalRows = await CountRowsAsync(projectId, tableName, options.Filter, cancellationToken);

        var job = new BulkExportJob
        {
            JobId = jobId,
            ProjectId = projectId,
            TableId = table.TableId,
            TableName = tableName,
            Format = ExportFormat.Csv,
            Status = BulkJobStatus.Pending,
            TotalRows = totalRows,
            Options = JsonDocument.Parse(JsonSerializer.Serialize(options)),
            CreatedAt = now,
            ExpiresAt = now.Add(_options.ExportFileExpiration)
        };

        await SaveExportJobAsync(job, cancellationToken);
        return job;
    }

    public async Task<BulkExportJob> StartJsonExportAsync(
        Guid projectId,
        string tableName,
        JsonExportOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new JsonExportOptions();

        var table = await _schemaManager.GetTableAsync(projectId, tableName, cancellationToken)
            ?? throw new TableNotFoundException(tableName);

        var jobId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var totalRows = await CountRowsAsync(projectId, tableName, options.Filter, cancellationToken);

        var job = new BulkExportJob
        {
            JobId = jobId,
            ProjectId = projectId,
            TableId = table.TableId,
            TableName = tableName,
            Format = ExportFormat.Json,
            Status = BulkJobStatus.Pending,
            TotalRows = totalRows,
            Options = JsonDocument.Parse(JsonSerializer.Serialize(options)),
            CreatedAt = now,
            ExpiresAt = now.Add(_options.ExportFileExpiration)
        };

        await SaveExportJobAsync(job, cancellationToken);
        return job;
    }

    public async Task<BulkExportJob> StartXlsxExportAsync(
        Guid projectId,
        string tableName,
        XlsxExportOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new XlsxExportOptions();

        var table = await _schemaManager.GetTableAsync(projectId, tableName, cancellationToken)
            ?? throw new TableNotFoundException(tableName);

        var jobId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var totalRows = await CountRowsAsync(projectId, tableName, options.Filter, cancellationToken);

        var job = new BulkExportJob
        {
            JobId = jobId,
            ProjectId = projectId,
            TableId = table.TableId,
            TableName = tableName,
            Format = ExportFormat.Xlsx,
            Status = BulkJobStatus.Pending,
            TotalRows = totalRows,
            Options = JsonDocument.Parse(JsonSerializer.Serialize(options)),
            CreatedAt = now,
            ExpiresAt = now.Add(_options.ExportFileExpiration)
        };

        await SaveExportJobAsync(job, cancellationToken);
        return job;
    }

    public async Task StreamExportAsync(
        Guid jobId,
        Stream outputStream,
        CancellationToken cancellationToken = default)
    {
        var job = await GetExportJobAsync(jobId, cancellationToken)
            ?? throw new NotFoundException("Export job", jobId.ToString());

        await UpdateExportJobStatusAsync(jobId, BulkJobStatus.Processing, cancellationToken);

        try
        {
            var processedRows = 0L;

            switch (job.Format)
            {
                case ExportFormat.Csv:
                    var csvOptions = job.Options?.Deserialize<CsvExportOptions>() ?? new CsvExportOptions();
                    await StreamCsvExportAsync(job, csvOptions, outputStream, p => processedRows = p, cancellationToken);
                    break;

                case ExportFormat.Json:
                    var jsonOptions = job.Options?.Deserialize<JsonExportOptions>() ?? new JsonExportOptions();
                    await StreamJsonExportAsync(job, jsonOptions, outputStream, p => processedRows = p, cancellationToken);
                    break;

                case ExportFormat.Xlsx:
                    var xlsxOptions = job.Options?.Deserialize<XlsxExportOptions>() ?? new XlsxExportOptions();
                    await StreamXlsxExportAsync(job, xlsxOptions, outputStream, p => processedRows = p, cancellationToken);
                    break;
            }

            await UpdateExportJobCompletedAsync(jobId, processedRows, cancellationToken);
        }
        catch (Exception ex)
        {
            await UpdateExportJobFailedAsync(jobId, ex.Message, cancellationToken);
            throw;
        }
    }

    #endregion

    #region Job Management

    public async Task<BulkImportJob?> GetImportJobAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT job_id, project_id, table_id, table_name, format, status,
                   total_rows, processed_rows, success_count, error_count,
                   error_message, error_details, options, created_at, started_at, completed_at
            FROM morphdb._morph_import_jobs
            WHERE job_id = @JobId
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<ImportJobRow>(sql, new { JobId = jobId });

        return row is null ? null : MapToImportJob(row);
    }

    public async Task<BulkExportJob?> GetExportJobAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT job_id, project_id, table_id, table_name, format, status,
                   total_rows, processed_rows, file_path, file_size,
                   error_message, options, created_at, started_at, completed_at, expires_at
            FROM morphdb._morph_export_jobs
            WHERE job_id = @JobId
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<ExportJobRow>(sql, new { JobId = jobId });

        return row is null ? null : MapToExportJob(row);
    }

    public async Task<IReadOnlyList<BulkImportJob>> ListImportJobsAsync(
        Guid projectId,
        int limit = 50,
        int offset = 0,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT job_id, project_id, table_id, table_name, format, status,
                   total_rows, processed_rows, success_count, error_count,
                   error_message, error_details, options, created_at, started_at, completed_at
            FROM morphdb._morph_import_jobs
            WHERE project_id = @ProjectId
            ORDER BY created_at DESC
            LIMIT @Limit OFFSET @Offset
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<ImportJobRow>(sql, new { ProjectId = projectId, Limit = limit, Offset = offset });

        return rows.Select(MapToImportJob).ToList();
    }

    public async Task<IReadOnlyList<BulkExportJob>> ListExportJobsAsync(
        Guid projectId,
        int limit = 50,
        int offset = 0,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT job_id, project_id, table_id, table_name, format, status,
                   total_rows, processed_rows, file_path, file_size,
                   error_message, options, created_at, started_at, completed_at, expires_at
            FROM morphdb._morph_export_jobs
            WHERE project_id = @ProjectId
            ORDER BY created_at DESC
            LIMIT @Limit OFFSET @Offset
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<ExportJobRow>(sql, new { ProjectId = projectId, Limit = limit, Offset = offset });

        return rows.Select(MapToExportJob).ToList();
    }

    public async Task<bool> CancelJobAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        const string importSql = """
            UPDATE morphdb._morph_import_jobs
            SET status = 'cancelled', completed_at = @Now
            WHERE job_id = @JobId AND status IN ('pending', 'processing')
            """;

        const string exportSql = """
            UPDATE morphdb._morph_export_jobs
            SET status = 'cancelled', completed_at = @Now
            WHERE job_id = @JobId AND status IN ('pending', 'processing')
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        var importRows = await connection.ExecuteAsync(importSql, new { JobId = jobId, Now = DateTimeOffset.UtcNow });
        if (importRows > 0)
            return true;

        var exportRows = await connection.ExecuteAsync(exportSql, new { JobId = jobId, Now = DateTimeOffset.UtcNow });
        return exportRows > 0;
    }

    public async Task<BulkJobProgress?> GetJobProgressAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        // Try import job first
        var importJob = await GetImportJobAsync(jobId, cancellationToken);
        if (importJob is not null)
        {
            return new BulkJobProgress
            {
                JobId = importJob.JobId,
                Status = importJob.Status,
                TotalRows = importJob.TotalRows,
                ProcessedRows = importJob.ProcessedRows,
                SuccessCount = importJob.SuccessCount,
                ErrorCount = importJob.ErrorCount
            };
        }

        // Try export job
        var exportJob = await GetExportJobAsync(jobId, cancellationToken);
        if (exportJob is not null)
        {
            return new BulkJobProgress
            {
                JobId = exportJob.JobId,
                Status = exportJob.Status,
                TotalRows = exportJob.TotalRows,
                ProcessedRows = exportJob.ProcessedRows
            };
        }

        return null;
    }

    public async Task<IReadOnlyList<BulkImportJob>> GetPendingImportJobsAsync(
        int limit = 10,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT job_id, project_id, table_id, table_name, format, status,
                   total_rows, processed_rows, success_count, error_count,
                   error_message, error_details, options, created_at, started_at, completed_at
            FROM morphdb._morph_import_jobs
            WHERE status = 'pending'
            ORDER BY created_at ASC
            LIMIT @Limit
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<ImportJobRow>(sql, new { Limit = limit });

        return rows.Select(MapToImportJob).ToList();
    }

    public async Task<IReadOnlyList<BulkExportJob>> GetPendingExportJobsAsync(
        int limit = 10,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT job_id, project_id, table_id, table_name, format, status,
                   total_rows, processed_rows, file_path, file_size,
                   error_message, options, created_at, started_at, completed_at, expires_at
            FROM morphdb._morph_export_jobs
            WHERE status = 'pending'
            ORDER BY created_at ASC
            LIMIT @Limit
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<ExportJobRow>(sql, new { Limit = limit });

        return rows.Select(MapToExportJob).ToList();
    }

    public async Task StoreExportDataAsync(
        Guid jobId,
        Stream dataStream,
        CancellationToken cancellationToken = default)
    {
        using var ms = new MemoryStream();
        await dataStream.CopyToAsync(ms, cancellationToken);

        const string sql = """
            INSERT INTO morphdb._morph_export_data (job_id, data, created_at)
            VALUES (@JobId, @Data, @CreatedAt)
            ON CONFLICT (job_id) DO UPDATE SET data = @Data
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(sql, new
        {
            JobId = jobId,
            Data = ms.ToArray(),
            CreatedAt = DateTimeOffset.UtcNow
        });

        // Update file size in job record
        const string updateSql = """
            UPDATE morphdb._morph_export_jobs
            SET file_size = @FileSize
            WHERE job_id = @JobId
            """;

        await connection.ExecuteAsync(updateSql, new { JobId = jobId, FileSize = ms.Length });
    }

    public async Task<Stream?> GetStoredExportDataAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        const string sql = "SELECT data FROM morphdb._morph_export_data WHERE job_id = @JobId";

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var data = await connection.ExecuteScalarAsync<byte[]>(sql, new { JobId = jobId });

        return data is null ? null : new MemoryStream(data);
    }

    #endregion

    #region Private Helpers - Parsing

    private async Task<ImportRowResult> ProcessImportRowAsync(
        BulkImportJob job,
        TableMetadata? table,
        IDictionary<string, object?> row,
        long rowNumber)
    {
        try
        {
            IDictionary<string, object?> insertResult;

            // Use WritePipeline with BulkImport options if available
            if (_writePipeline is not null && table is not null)
            {
                var writeResult = await _writePipeline.InsertAsync(
                    job.ProjectId,
                    table,
                    row,
                    WriteOptions.BulkImport,
                    CancellationToken.None);

                if (!writeResult.Success)
                {
                    var errorMessages = string.Join("; ", writeResult.Errors.Select(e => e.Message));
                    return new ImportRowResult
                    {
                        RowNumber = rowNumber,
                        Success = false,
                        Error = errorMessages
                    };
                }

                insertResult = writeResult.Data ?? new Dictionary<string, object?>();
            }
            else
            {
                // Fallback to direct data service
                insertResult = await _dataService.InsertAsync(
                    job.ProjectId,
                    job.TableName,
                    row,
                    CancellationToken.None);
            }

            var recordId = SystemColumns.GetRecordId(insertResult);

            return new ImportRowResult
            {
                RowNumber = rowNumber,
                Success = true,
                RecordId = recordId
            };
        }
        catch (Exception ex)
        {
            return new ImportRowResult
            {
                RowNumber = rowNumber,
                Success = false,
                Error = ex.Message
            };
        }
    }

    private static async IAsyncEnumerable<IDictionary<string, object?>> ParseCsvAsync(
        Stream stream,
        BulkImportJob job,
        TableMetadata? table,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var options = job.Options?.Deserialize<CsvImportOptions>() ?? new CsvImportOptions();
        using var reader = new StreamReader(stream, Encoding.UTF8);

        var columnTypes = table?.Columns.ToDictionary(c => c.LogicalName, c => c.DataType, StringComparer.OrdinalIgnoreCase);

        string[]? headers = null;
        var lineNumber = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
                break;
            if (string.IsNullOrWhiteSpace(line))
                continue;

            lineNumber++;
            var values = ParseCsvLine(line, options.Delimiter);

            if (lineNumber == 1 && options.HasHeader)
            {
                headers = values.Select(v => options.TrimWhitespace ? v.Trim() : v).ToArray();
                continue;
            }

            headers ??= Enumerable.Range(0, values.Length).Select(i => $"column_{i}").ToArray();

            var row = new Dictionary<string, object?>();
            for (var i = 0; i < Math.Min(headers.Length, values.Length); i++)
            {
                var value = options.TrimWhitespace ? values[i].Trim() : values[i];
                var header = headers[i];
                var dataType = columnTypes is not null && columnTypes.TryGetValue(header, out var t) ? t : (MorphDataType?)null;
                row[header] = ConvertCsvValue(value, options.NullHandling, dataType);
            }

            yield return row;
        }
    }

    private static async IAsyncEnumerable<IDictionary<string, object?>> ParseJsonArrayAsync(
        Stream stream,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("JSON must be an array");
        }

        foreach (var element in document.RootElement.EnumerateArray())
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;

            if (element.ValueKind == JsonValueKind.Object)
            {
                yield return JsonElementToDictionary(element);
            }
        }
    }

    private static async IAsyncEnumerable<IDictionary<string, object?>> ParseNdjsonAsync(
        Stream stream,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8);

        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
                break;
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var document = JsonDocument.Parse(line);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                yield return JsonElementToDictionary(document.RootElement);
            }
        }
    }

    private static string[] ParseCsvLine(string line, char delimiter)
    {
        var values = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];

            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == delimiter && !inQuotes)
            {
                values.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        values.Add(current.ToString());
        return [.. values];
    }

    /// <summary>
    /// CSV has one representation for every value: text. Everything the pipeline writes downstream
    /// (<see cref="Infrastructure.TypeMapper.ToDbValue"/>) expects a value already shaped for its
    /// column — a JSON parser hands that shape over for free (a JSON number deserializes to a
    /// number), but a CSV cell never does. This is the one place that gap needs closing: without it,
    /// every non-text column is handed a raw string and the write fails with a Postgres type-mismatch
    /// the row-level <c>Error</c> already carries but nothing before this fix ever surfaced.
    /// A value the declared type cannot parse is left as a string; the write pipeline then rejects
    /// that specific row instead of this parser silently guessing or the whole job dying mid-stream.
    /// </summary>
    private static object? ConvertCsvValue(string value, NullHandling nullHandling, MorphDataType? dataType)
    {
        var normalized = nullHandling switch
        {
            NullHandling.EmptyAsNull when string.IsNullOrEmpty(value) => null,
            NullHandling.NullStringAsNull when value.Equals("null", StringComparison.OrdinalIgnoreCase) => null,
            _ => value
        };

        if (normalized is null || dataType is null)
            return normalized;

        try
        {
            return dataType switch
            {
                MorphDataType.Integer => int.Parse(normalized, CultureInfo.InvariantCulture),
                MorphDataType.BigInteger => long.Parse(normalized, CultureInfo.InvariantCulture),
                MorphDataType.Decimal => decimal.Parse(normalized, CultureInfo.InvariantCulture),
                MorphDataType.Boolean => bool.Parse(normalized),
                MorphDataType.Date or MorphDataType.DateTime or MorphDataType.Time
                    or MorphDataType.CreatedTime or MorphDataType.ModifiedTime
                    => DateTime.Parse(normalized, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal),
                MorphDataType.Uuid or MorphDataType.Relation or MorphDataType.CreatedBy or MorphDataType.ModifiedBy
                    => Guid.Parse(normalized),
                _ => normalized
            };
        }
        catch (FormatException)
        {
            return normalized;
        }
        catch (OverflowException)
        {
            return normalized;
        }
    }

    private static Dictionary<string, object?> JsonElementToDictionary(JsonElement element)
    {
        var dict = new Dictionary<string, object?>();

        foreach (var property in element.EnumerateObject())
        {
            dict[property.Name] = ConvertJsonValue(property.Value);
        }

        return dict;
    }

    private static object? ConvertJsonValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number when element.TryGetInt64(out var l) => l,
            JsonValueKind.Number => element.GetDecimal(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Array => element.EnumerateArray().Select(ConvertJsonValue).ToList(),
            JsonValueKind.Object => JsonElementToDictionary(element),
            _ => element.GetRawText()
        };
    }

    #endregion

    #region Private Helpers - Export

    private async Task StreamCsvExportAsync(
        BulkExportJob job,
        CsvExportOptions options,
        Stream outputStream,
        Action<long> progressCallback,
        CancellationToken cancellationToken)
    {
        await using var writer = new StreamWriter(outputStream, Encoding.UTF8, leaveOpen: true);

        var table = await _schemaManager.GetTableAsync(job.ProjectId, job.TableName, cancellationToken)
            ?? throw new TableNotFoundException(job.TableName);

        var columns = options.Columns?.ToList() ?? table.Columns.Select(c => c.LogicalName).ToList();

        // Write header
        if (options.IncludeHeader)
        {
            await writer.WriteLineAsync(string.Join(options.Delimiter, columns.Select(EscapeCsvValue)));
        }

        // Stream data
        var processedRows = 0L;
        await foreach (var row in StreamTableDataAsync(job.ProjectId, job.TableName, columns, options.Filter, options.OrderBy, cancellationToken))
        {
            var values = columns.Select(col => FormatCsvValue(row.TryGetValue(col, out var v) ? v : null, options.DateFormat));
            await writer.WriteLineAsync(string.Join(options.Delimiter, values.Select(EscapeCsvValue)));

            processedRows++;
            if (processedRows % _options.ProgressUpdateInterval == 0)
            {
                progressCallback(processedRows);
            }
        }

        await writer.FlushAsync(cancellationToken);
        progressCallback(processedRows);
    }

    private async Task StreamJsonExportAsync(
        BulkExportJob job,
        JsonExportOptions options,
        Stream outputStream,
        Action<long> progressCallback,
        CancellationToken cancellationToken)
    {
        var jsonOptions = new JsonWriterOptions { Indented = options.Pretty };
        await using var writer = new Utf8JsonWriter(outputStream, jsonOptions);

        var table = await _schemaManager.GetTableAsync(job.ProjectId, job.TableName, cancellationToken)
            ?? throw new TableNotFoundException(job.TableName);

        var columns = options.Columns?.ToList() ?? table.Columns.Select(c => c.LogicalName).ToList();

        writer.WriteStartArray();

        var processedRows = 0L;
        await foreach (var row in StreamTableDataAsync(job.ProjectId, job.TableName, columns, options.Filter, options.OrderBy, cancellationToken))
        {
            writer.WriteStartObject();
            foreach (var col in columns)
            {
                writer.WritePropertyName(col);
                WriteJsonValue(writer, row.TryGetValue(col, out var v) ? v : null);
            }
            writer.WriteEndObject();

            processedRows++;
            if (processedRows % _options.ProgressUpdateInterval == 0)
            {
                await writer.FlushAsync(cancellationToken);
                progressCallback(processedRows);
            }
        }

        writer.WriteEndArray();
        await writer.FlushAsync(cancellationToken);
        progressCallback(processedRows);
    }

    private async Task StreamXlsxExportAsync(
        BulkExportJob job,
        XlsxExportOptions options,
        Stream outputStream,
        Action<long> progressCallback,
        CancellationToken cancellationToken)
    {
        // Builds the workbook in memory via ClosedXML, then serializes once at the end. OOXML is a
        // ZIP container over several XML parts, so — unlike the row-at-a-time CSV/JSON writers above
        // — there is no way to hand PostgresBulkOperationService's caller a partially-written valid
        // file; the whole part has to be assembled before it can be zipped. This bounds export size
        // by available memory, which is an accepted trade-off for now: what was fixed here is the
        // format lie — a consumer seeing text pretending to be a spreadsheet — not export scale. A
        // streaming OOXML writer is a separate, larger change if a real dataset ever needs it.
        var table = await _schemaManager.GetTableAsync(job.ProjectId, job.TableName, cancellationToken)
            ?? throw new TableNotFoundException(job.TableName);

        var columns = options.Columns?.ToList() ?? table.Columns.Select(c => c.LogicalName).ToList();

        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add(SanitizeSheetName(job.TableName));

        var rowIndex = 1;
        if (options.IncludeHeader)
        {
            for (var i = 0; i < columns.Count; i++)
            {
                worksheet.Cell(rowIndex, i + 1).Value = columns[i];
            }
            rowIndex++;
        }

        var processedRows = 0L;
        await foreach (var row in StreamTableDataAsync(job.ProjectId, job.TableName, columns, options.Filter, options.OrderBy, cancellationToken))
        {
            for (var i = 0; i < columns.Count; i++)
            {
                SetXlsxCellValue(worksheet.Cell(rowIndex, i + 1), row.TryGetValue(columns[i], out var v) ? v : null);
            }
            rowIndex++;

            processedRows++;
            if (processedRows % _options.ProgressUpdateInterval == 0)
            {
                progressCallback(processedRows);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        workbook.SaveAs(outputStream);
        progressCallback(processedRows);
    }

    private static void SetXlsxCellValue(IXLCell cell, object? value)
    {
        switch (value)
        {
            case null:
                break;
            case bool b:
                cell.Value = b;
                break;
            case int i:
                cell.Value = i;
                break;
            case long l:
                cell.Value = l;
                break;
            case decimal dec:
                cell.Value = dec;
                break;
            case double dbl:
                cell.Value = dbl;
                break;
            case DateTime dt:
                cell.Value = dt;
                break;
            case DateTimeOffset dto:
                cell.Value = dto.UtcDateTime;
                break;
            default:
                cell.Value = value.ToString() ?? "";
                break;
        }
    }

    private static string SanitizeSheetName(string tableName)
    {
        // Excel worksheet names: max 31 chars, and none of : \ / ? * [ ]
        var sanitized = new string(tableName.Select(c => ":\\/?*[]".Contains(c) ? '_' : c).ToArray());
        return sanitized.Length > 31 ? sanitized[..31] : sanitized;
    }

    private static string FormatCsvValue(object? value, string? dateFormat)
    {
        return value switch
        {
            null => "",
            DateTime dt => dt.ToString(dateFormat ?? "O", CultureInfo.InvariantCulture),
            DateTimeOffset dto => dto.ToString(dateFormat ?? "O", CultureInfo.InvariantCulture),
            bool b => b.ToString().ToLowerInvariant(),
            _ => value.ToString() ?? ""
        };
    }

    private static string EscapeCsvValue(string value)
    {
        if (value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }
        return value;
    }

    private static void WriteJsonValue(Utf8JsonWriter writer, object? value)
    {
        switch (value)
        {
            case null:
                writer.WriteNullValue();
                break;
            case bool b:
                writer.WriteBooleanValue(b);
                break;
            case int i:
                writer.WriteNumberValue(i);
                break;
            case long l:
                writer.WriteNumberValue(l);
                break;
            case decimal d:
                writer.WriteNumberValue(d);
                break;
            case double dbl:
                writer.WriteNumberValue(dbl);
                break;
            case DateTime dt:
                writer.WriteStringValue(dt);
                break;
            case DateTimeOffset dto:
                writer.WriteStringValue(dto);
                break;
            case Guid g:
                writer.WriteStringValue(g);
                break;
            case string s:
                writer.WriteStringValue(s);
                break;
            case JsonElement je:
                je.WriteTo(writer);
                break;
            default:
                writer.WriteStringValue(value.ToString());
                break;
        }
    }

    #endregion

    #region Private Helpers - Database

    private async Task<long> CountRowsAsync(
        Guid projectId,
        string tableName,
        string? filter,
        CancellationToken cancellationToken)
    {
        var table = await _schemaManager.GetTableAsync(projectId, tableName, cancellationToken)
            ?? throw new TableNotFoundException(tableName);

        var sql = $"SELECT COUNT(*) FROM {table.PhysicalName}";
        // Note: In production, filter would be parsed and added safely

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<long>(sql);
    }

    private async IAsyncEnumerable<IDictionary<string, object?>> StreamTableDataAsync(
        Guid projectId,
        string tableName,
        List<string> columns,
        string? filter,
        string? orderBy,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var table = await _schemaManager.GetTableAsync(projectId, tableName, cancellationToken)
            ?? throw new TableNotFoundException(tableName);

        var columnMap = table.Columns.ToDictionary(c => c.LogicalName, c => c.PhysicalName);
        var selectColumns = columns.Select(c => columnMap.GetValueOrDefault(c, c)).ToList();

        var sql = $"SELECT {string.Join(", ", selectColumns)} FROM {table.PhysicalName}";
        // Note: In production, filter and orderBy would be parsed and added safely

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        using var reader = await connection.ExecuteReaderAsync(sql);

        while (await reader.ReadAsync(cancellationToken))
        {
            var row = new Dictionary<string, object?>();
            for (var i = 0; i < columns.Count; i++)
            {
                row[columns[i]] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            }
            yield return row;
        }
    }

    private async Task StoreImportDataAsync(Guid jobId, Stream dataStream, CancellationToken cancellationToken)
    {
        // For simplicity, store in database. In production, consider blob storage.
        using var ms = new MemoryStream();
        await dataStream.CopyToAsync(ms, cancellationToken);

        const string sql = """
            INSERT INTO morphdb._morph_import_data (job_id, data, created_at)
            VALUES (@JobId, @Data, @CreatedAt)
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(sql, new
        {
            JobId = jobId,
            Data = ms.ToArray(),
            CreatedAt = DateTimeOffset.UtcNow
        });
    }

    private async Task<Stream?> GetStoredImportDataAsync(Guid jobId, CancellationToken cancellationToken)
    {
        const string sql = "SELECT data FROM morphdb._morph_import_data WHERE job_id = @JobId";

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var data = await connection.ExecuteScalarAsync<byte[]>(sql, new { JobId = jobId });

        return data is null ? null : new MemoryStream(data);
    }

    private async Task DeleteStoredImportDataAsync(Guid jobId, CancellationToken cancellationToken)
    {
        const string sql = "DELETE FROM morphdb._morph_import_data WHERE job_id = @JobId";

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(sql, new { JobId = jobId });
    }

    private async Task SaveImportJobAsync(BulkImportJob job, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO morphdb._morph_import_jobs (
                job_id, project_id, table_id, table_name, format, status,
                total_rows, processed_rows, success_count, error_count,
                error_message, options, created_at
            ) VALUES (
                @JobId, @ProjectId, @TableId, @TableName, @Format, @Status,
                @TotalRows, @ProcessedRows, @SuccessCount, @ErrorCount,
                @ErrorMessage, @Options::jsonb, @CreatedAt
            )
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(sql, new
        {
            job.JobId,
            job.ProjectId,
            job.TableId,
            job.TableName,
            Format = job.Format.ToString().ToLowerInvariant(),
            Status = job.Status.ToString().ToLowerInvariant(),
            job.TotalRows,
            job.ProcessedRows,
            job.SuccessCount,
            job.ErrorCount,
            job.ErrorMessage,
            Options = job.Options?.RootElement.GetRawText(),
            job.CreatedAt
        });
    }

    private async Task SaveExportJobAsync(BulkExportJob job, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO morphdb._morph_export_jobs (
                job_id, project_id, table_id, table_name, format, status,
                total_rows, processed_rows, file_path, file_size,
                error_message, options, created_at, expires_at
            ) VALUES (
                @JobId, @ProjectId, @TableId, @TableName, @Format, @Status,
                @TotalRows, @ProcessedRows, @FilePath, @FileSize,
                @ErrorMessage, @Options::jsonb, @CreatedAt, @ExpiresAt
            )
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(sql, new
        {
            job.JobId,
            job.ProjectId,
            job.TableId,
            job.TableName,
            Format = job.Format.ToString().ToLowerInvariant(),
            Status = job.Status.ToString().ToLowerInvariant(),
            job.TotalRows,
            job.ProcessedRows,
            job.FilePath,
            job.FileSize,
            job.ErrorMessage,
            Options = job.Options?.RootElement.GetRawText(),
            job.CreatedAt,
            job.ExpiresAt
        });
    }

    private async Task UpdateImportJobStatusAsync(Guid jobId, BulkJobStatus status, CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE morphdb._morph_import_jobs
            SET status = @Status, started_at = COALESCE(started_at, @Now)
            WHERE job_id = @JobId
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(sql, new
        {
            JobId = jobId,
            Status = status.ToString().ToLowerInvariant(),
            Now = DateTimeOffset.UtcNow
        });
    }

    private async Task UpdateExportJobStatusAsync(Guid jobId, BulkJobStatus status, CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE morphdb._morph_export_jobs
            SET status = @Status, started_at = COALESCE(started_at, @Now)
            WHERE job_id = @JobId
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(sql, new
        {
            JobId = jobId,
            Status = status.ToString().ToLowerInvariant(),
            Now = DateTimeOffset.UtcNow
        });
    }

    private async Task UpdateImportProgressAsync(
        Guid jobId,
        long processedRows,
        long successCount,
        long errorCount,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE morphdb._morph_import_jobs
            SET processed_rows = @ProcessedRows, success_count = @SuccessCount, error_count = @ErrorCount
            WHERE job_id = @JobId
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(sql, new { JobId = jobId, ProcessedRows = processedRows, SuccessCount = successCount, ErrorCount = errorCount });
    }

    private async Task UpdateImportJobCompletedAsync(
        Guid jobId,
        long totalRows,
        long successCount,
        long errorCount,
        List<ImportRowError> errorDetails,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE morphdb._morph_import_jobs
            SET status = @Status, total_rows = @TotalRows, processed_rows = @TotalRows,
                success_count = @SuccessCount, error_count = @ErrorCount,
                error_details = @ErrorDetails::jsonb, completed_at = @Now
            WHERE job_id = @JobId
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(sql, new
        {
            JobId = jobId,
            Status = (errorCount == 0 ? BulkJobStatus.Completed : BulkJobStatus.Completed).ToString().ToLowerInvariant(),
            TotalRows = totalRows,
            SuccessCount = successCount,
            ErrorCount = errorCount,
            ErrorDetails = errorDetails.Count == 0 ? null : JsonSerializer.Serialize(errorDetails),
            Now = DateTimeOffset.UtcNow
        });
    }

    private async Task UpdateExportJobCompletedAsync(
        Guid jobId,
        long processedRows,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE morphdb._morph_export_jobs
            SET status = 'completed', processed_rows = @ProcessedRows, completed_at = @Now
            WHERE job_id = @JobId
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(sql, new { JobId = jobId, ProcessedRows = processedRows, Now = DateTimeOffset.UtcNow });
    }

    private async Task UpdateExportJobFailedAsync(
        Guid jobId,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE morphdb._morph_export_jobs
            SET status = 'failed', error_message = @ErrorMessage, completed_at = @Now
            WHERE job_id = @JobId
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(sql, new { JobId = jobId, ErrorMessage = errorMessage, Now = DateTimeOffset.UtcNow });
    }

    #endregion

    #region Row Mapping

    private static BulkImportJob MapToImportJob(ImportJobRow row)
    {
        var errorDetails = row.error_details is null
            ? null
            : JsonSerializer.Deserialize<List<ImportRowError>>(row.error_details);

        return new BulkImportJob
        {
            JobId = row.job_id,
            ProjectId = row.project_id,
            TableId = row.table_id,
            TableName = row.table_name,
            Format = Enum.Parse<ImportFormat>(row.format, ignoreCase: true),
            Status = Enum.Parse<BulkJobStatus>(row.status, ignoreCase: true),
            TotalRows = row.total_rows,
            ProcessedRows = row.processed_rows,
            SuccessCount = row.success_count,
            ErrorCount = row.error_count,
            ErrorMessage = row.error_message,
            ErrorDetails = errorDetails,
            ErrorDetailsTruncated = row.error_count > BulkImportJob.MaxErrorDetails,
            Options = row.options is null ? null : JsonDocument.Parse(row.options),
            CreatedAt = row.created_at,
            StartedAt = row.started_at,
            CompletedAt = row.completed_at
        };
    }

    private static BulkExportJob MapToExportJob(ExportJobRow row)
    {
        return new BulkExportJob
        {
            JobId = row.job_id,
            ProjectId = row.project_id,
            TableId = row.table_id,
            TableName = row.table_name,
            Format = Enum.Parse<ExportFormat>(row.format, ignoreCase: true),
            Status = Enum.Parse<BulkJobStatus>(row.status, ignoreCase: true),
            TotalRows = row.total_rows,
            ProcessedRows = row.processed_rows,
            FilePath = row.file_path,
            FileSize = row.file_size,
            ErrorMessage = row.error_message,
            Options = row.options is null ? null : JsonDocument.Parse(row.options),
            CreatedAt = row.created_at,
            StartedAt = row.started_at,
            CompletedAt = row.completed_at,
            ExpiresAt = row.expires_at
        };
    }

    #endregion

    #region Row Types

    private sealed record ImportJobRow
    {
        public Guid job_id { get; init; }
        public Guid project_id { get; init; }
        public Guid table_id { get; init; }
        public string table_name { get; init; } = null!;
        public string format { get; init; } = null!;
        public string status { get; init; } = null!;
        public long total_rows { get; init; }
        public long processed_rows { get; init; }
        public long success_count { get; init; }
        public long error_count { get; init; }
        public string? error_message { get; init; }
        public string? error_details { get; init; }
        public string? options { get; init; }
        public DateTimeOffset created_at { get; init; }
        public DateTimeOffset? started_at { get; init; }
        public DateTimeOffset? completed_at { get; init; }
    }

    private sealed record ExportJobRow
    {
        public Guid job_id { get; init; }
        public Guid project_id { get; init; }
        public Guid table_id { get; init; }
        public string table_name { get; init; } = null!;
        public string format { get; init; } = null!;
        public string status { get; init; } = null!;
        public long total_rows { get; init; }
        public long processed_rows { get; init; }
        public string? file_path { get; init; }
        public long? file_size { get; init; }
        public string? error_message { get; init; }
        public string? options { get; init; }
        public DateTimeOffset created_at { get; init; }
        public DateTimeOffset? started_at { get; init; }
        public DateTimeOffset? completed_at { get; init; }
        public DateTimeOffset? expires_at { get; init; }
    }

    #endregion
}

/// <summary>
/// Options for bulk operations.
/// </summary>
public sealed class BulkOperationOptions
{
    /// <summary>
    /// How often to update progress (in rows).
    /// </summary>
    public int ProgressUpdateInterval { get; set; } = 100;

    /// <summary>
    /// How long export files are available for download.
    /// </summary>
    public TimeSpan ExportFileExpiration { get; set; } = TimeSpan.FromHours(24);
}
