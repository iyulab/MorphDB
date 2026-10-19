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
using MorphDB.Core.Security;
using MorphDB.Npgsql.Dml;
using MorphDB.Npgsql.Infrastructure;
using MorphDB.Npgsql.Security;
using Npgsql;

namespace MorphDB.Npgsql.Services;

/// <summary>
/// PostgreSQL implementation of bulk import/export operations.
/// Uses WritePipeline with BulkImport options for optimized validation.
/// </summary>
/// <remarks>
/// <para>A job is requested by one call and carried out by another, usually much later and from a
/// background service that has no caller of its own. The table's rows are read and written as the
/// caller that requested the job: its security context is stored in the job's row when the job is
/// created, and is the ambient context while <see cref="StreamExportAsync"/> reads the table and
/// while <see cref="ProcessImportAsync"/> writes each row — whoever is ambient where those are
/// called from, and nobody when the job's row holds no context.</para>
/// <para>That is done here, in the two methods that carry a job out, and not in what calls them:
/// anything that executes a job goes through one of the two, so nothing can execute one as the
/// wrong caller by leaving a step out.</para>
/// <para>The same row decides who may follow the job afterwards. What a job's file holds is what its
/// requester may read, so a job is there only for a caller working in its project that is its
/// requester, or that bypasses row-level security: <see cref="ReachableByCallerSql"/> is that rule,
/// and every statement that finds a job for a caller — its status, a listing, a cancellation, its
/// file — carries it. The members of <see cref="IBulkJobExecutor"/> find a job by its id alone;
/// they are for the job processor, which is not a caller, and a request is not handed them.</para>
/// </remarks>
public sealed class PostgresBulkOperationService : IBulkOperationService, IBulkJobExecutor
{
    private readonly MorphConnectionFactory _connections;
    private readonly ISchemaManager _schemaManager;
    private readonly IMorphDataService _dataService;
    private readonly ISecurityContextAccessor _securityContext;
    private readonly IWritePipeline? _writePipeline;
    private readonly BulkOperationOptions _options;

    public PostgresBulkOperationService(
        MorphConnectionFactory connections,
        ISchemaManager schemaManager,
        IMorphDataService dataService,
        ISecurityContextAccessor securityContext,
        BulkOperationOptions options,
        IWritePipeline? writePipeline = null)
    {
        _connections = connections;
        _schemaManager = schemaManager;
        _dataService = dataService;
        _securityContext = securityContext;
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
        // A job that fails part-way is marked failed with the reason, as an export job is. Without
        // this, an exception left the job in 'processing' forever -- indistinguishable, to whoever
        // polls it, from a job still running. (An iterator cannot yield inside try/catch, so the
        // rows are pulled here and yielded outside it.)
        var rows = ProcessImportRowsAsync(jobId, cancellationToken).GetAsyncEnumerator(cancellationToken);
        await using (rows.ConfigureAwait(false))
        {
            while (true)
            {
                ImportRowResult current;
                try
                {
                    if (!await rows.MoveNextAsync().ConfigureAwait(false))
                    {
                        yield break;
                    }

                    current = rows.Current;
                }
                catch (Exception ex) when (ex is not OperationCanceledException and not NotFoundException)
                {
                    await UpdateImportJobFailedAsync(jobId, ex.Message, CancellationToken.None).ConfigureAwait(false);
                    throw;
                }

                yield return current;
            }
        }
    }

    private async IAsyncEnumerable<ImportRowResult> ProcessImportRowsAsync(
        Guid jobId,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var job = await ReadImportJobForExecutionAsync(jobId, cancellationToken)
            ?? throw new NotFoundException("Import job", jobId.ToString());

        // A job starts only from pending: one cancelled before the processor reached it stays so.
        if (!await TryStartJobAsync("_morph_import_jobs", jobId, cancellationToken))
        {
            throw new BulkJobCancelledException(jobId);
        }

        var requester = await GetRequesterAsync(ImportRequesterSql, jobId, cancellationToken);

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
                var result = await ProcessImportRowAsync(job, table, row, rowNumber, requester);

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
                    if (!await UpdateImportProgressAsync(jobId, rowNumber, successCount, errorCount, cancellationToken))
                    {
                        throw new BulkJobCancelledException(jobId);
                    }
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

        // An unknown or internal column is refused now, with the 202, not later inside the job.
        ResolveExportColumns(table, options.Columns);

        var jobId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        // Count total rows for progress tracking
        var totalRows = await CountRowsAsync(projectId, tableName, cancellationToken);

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

        // An unknown or internal column is refused now, with the 202, not later inside the job.
        ResolveExportColumns(table, options.Columns);

        var jobId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var totalRows = await CountRowsAsync(projectId, tableName, cancellationToken);

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

        // An unknown or internal column is refused now, with the 202, not later inside the job.
        ResolveExportColumns(table, options.Columns);

        var jobId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var totalRows = await CountRowsAsync(projectId, tableName, cancellationToken);

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
        var job = await ReadExportJobForExecutionAsync(jobId, cancellationToken)
            ?? throw new NotFoundException("Export job", jobId.ToString());

        if (!await TryStartJobAsync("_morph_export_jobs", jobId, cancellationToken))
        {
            throw new BulkJobCancelledException(jobId);
        }

        try
        {
            var processedRows = 0L;

            // Each progress report is also where a cancellation is noticed: the write that records the
            // count finds the job no longer processing, and the export stops there.
            async Task Progress(long rows)
            {
                processedRows = rows;
                if (!await UpdateExportProgressAsync(jobId, rows, cancellationToken))
                {
                    throw new BulkJobCancelledException(jobId);
                }
            }

            await WriteExportAsync(job, outputStream, Progress, cancellationToken);

            if (!await UpdateExportJobCompletedAsync(jobId, processedRows, cancellationToken))
            {
                throw new BulkJobCancelledException(jobId);
            }
        }
        catch (Exception ex) when (ex is not BulkJobCancelledException)
        {
            await UpdateExportJobFailedAsync(jobId, ex.Message, cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// Writes an export job's file to <paramref name="outputStream"/>, reading the table as the
    /// requester the job's row holds — nobody, when it holds none — and leaving whoever is ambient
    /// ambient again once it has been read. Carrying the job out and reading a completed job again
    /// for a download both write the file this way; only the first moves the job through its states.
    /// </summary>
    private async Task WriteExportAsync(
        BulkExportJob job,
        Stream outputStream,
        Func<long, Task> progress,
        CancellationToken cancellationToken)
    {
        var requester = await GetRequesterAsync(ExportRequesterSql, job.JobId, cancellationToken);

        using (SecurityContextScope.Begin(_securityContext, requester))
        {
            switch (job.Format)
            {
                case ExportFormat.Csv:
                    var csvOptions = job.Options?.Deserialize<CsvExportOptions>() ?? new CsvExportOptions();
                    await StreamCsvExportAsync(job, csvOptions, outputStream, progress, cancellationToken);
                    break;

                case ExportFormat.Json:
                    var jsonOptions = job.Options?.Deserialize<JsonExportOptions>() ?? new JsonExportOptions();
                    await StreamJsonExportAsync(job, jsonOptions, outputStream, progress, cancellationToken);
                    break;

                case ExportFormat.Xlsx:
                    var xlsxOptions = job.Options?.Deserialize<XlsxExportOptions>() ?? new XlsxExportOptions();
                    await StreamXlsxExportAsync(job, xlsxOptions, outputStream, progress, cancellationToken);
                    break;
            }
        }
    }

    #endregion

    #region Job Management

    /// <summary>
    /// What finds a job for a caller, as a condition on a job table: the job is of the caller's
    /// project, and the caller bypasses row-level security or is the job's requester.
    /// </summary>
    /// <remarks>
    /// <para>The requester is the caller with the same user id; two callers without one are the same
    /// caller, which is every caller of a deployment that enforces no secrets. A job requested by a
    /// caller that bypasses row-level security is not matched by a caller that does not, although
    /// neither has a user id: its file holds rows read past every policy. A row with no requester —
    /// requested without a caller, or stored by a version that did not record one — matches a
    /// bypassing caller only.</para>
    /// <para>Its parameters are those of <see cref="JobCaller"/>. An empty user id is no user id, on
    /// both sides, as it is to a policy.</para>
    /// </remarks>
    private const string ReachableByCallerSql = """
        project_id = @ProjectId
        AND (@Bypass OR (jsonb_typeof(requested_by) = 'object'
                         AND (requested_by -> 'bypassRls') IS DISTINCT FROM 'true'::jsonb
                         AND nullif(requested_by ->> 'userId', '') IS NOT DISTINCT FROM @UserId::text))
        """;

    /// <summary>Who a job is looked up for: the parameters of <see cref="ReachableByCallerSql"/>.</summary>
    private readonly record struct JobCaller(Guid ProjectId, bool Bypass, string? UserId);

    /// <summary>
    /// The ambient caller, working in <paramref name="projectId"/> — or <c>null</c> when there is no
    /// ambient context: nobody is asking, and no job is there for nobody.
    /// </summary>
    private JobCaller? CallerIn(Guid projectId)
    {
        var context = _securityContext.ContextOrNull;
        return context is null
            ? null
            : new JobCaller(projectId, context.BypassRls, string.IsNullOrEmpty(context.UserId) ? null : context.UserId);
    }

    private const string ImportJobColumns = """
        job_id, project_id, table_id, table_name, format, status,
        total_rows, processed_rows, success_count, error_count,
        error_message, error_details, options, created_at, started_at, completed_at
        """;

    private const string ExportJobColumns = """
        job_id, project_id, table_id, table_name, format, status,
        total_rows, processed_rows, file_path, file_size,
        error_message, options, created_at, started_at, completed_at, expires_at
        """;

    public async Task<BulkImportJob?> GetImportJobAsync(Guid projectId, Guid jobId, CancellationToken cancellationToken = default)
    {
        if (CallerIn(projectId) is not { } caller)
        {
            return null;
        }

        const string sql = $"""
            SELECT {ImportJobColumns}
            FROM morphdb._morph_import_jobs
            WHERE job_id = @JobId AND {ReachableByCallerSql}
            """;

        await using var connection = await _connections.OpenSystemAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<ImportJobRow>(
            sql, new { JobId = jobId, caller.ProjectId, caller.Bypass, caller.UserId });

        return row is null ? null : MapToImportJob(row);
    }

    public async Task<BulkExportJob?> GetExportJobAsync(Guid projectId, Guid jobId, CancellationToken cancellationToken = default)
    {
        if (CallerIn(projectId) is not { } caller)
        {
            return null;
        }

        const string sql = $"""
            SELECT {ExportJobColumns}
            FROM morphdb._morph_export_jobs
            WHERE job_id = @JobId AND {ReachableByCallerSql}
            """;

        await using var connection = await _connections.OpenSystemAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<ExportJobRow>(
            sql, new { JobId = jobId, caller.ProjectId, caller.Bypass, caller.UserId });

        return row is null ? null : MapToExportJob(row);
    }

    public async Task<IReadOnlyList<BulkImportJob>> ListImportJobsAsync(
        Guid projectId,
        int limit = 50,
        int offset = 0,
        CancellationToken cancellationToken = default)
    {
        if (CallerIn(projectId) is not { } caller)
        {
            return [];
        }

        const string sql = $"""
            SELECT {ImportJobColumns}
            FROM morphdb._morph_import_jobs
            WHERE {ReachableByCallerSql}
            ORDER BY created_at DESC
            LIMIT @Limit OFFSET @Offset
            """;

        await using var connection = await _connections.OpenSystemAsync(cancellationToken);
        var rows = await connection.QueryAsync<ImportJobRow>(
            sql, new { caller.ProjectId, caller.Bypass, caller.UserId, Limit = limit, Offset = offset });

        return rows.Select(MapToImportJob).ToList();
    }

    public async Task<IReadOnlyList<BulkExportJob>> ListExportJobsAsync(
        Guid projectId,
        int limit = 50,
        int offset = 0,
        CancellationToken cancellationToken = default)
    {
        if (CallerIn(projectId) is not { } caller)
        {
            return [];
        }

        const string sql = $"""
            SELECT {ExportJobColumns}
            FROM morphdb._morph_export_jobs
            WHERE {ReachableByCallerSql}
            ORDER BY created_at DESC
            LIMIT @Limit OFFSET @Offset
            """;

        await using var connection = await _connections.OpenSystemAsync(cancellationToken);
        var rows = await connection.QueryAsync<ExportJobRow>(
            sql, new { caller.ProjectId, caller.Bypass, caller.UserId, Limit = limit, Offset = offset });

        return rows.Select(MapToExportJob).ToList();
    }

    public async Task<bool> CancelJobAsync(Guid projectId, Guid jobId, CancellationToken cancellationToken = default)
    {
        if (CallerIn(projectId) is not { } caller)
        {
            return false;
        }

        // The rule is part of the statement that cancels, not a lookup made before it: a job that is
        // not there for the caller is one the UPDATE does not find.
        const string importSql = $"""
            UPDATE morphdb._morph_import_jobs
            SET status = 'cancelled', completed_at = @Now
            WHERE job_id = @JobId AND status IN ('pending', 'processing') AND {ReachableByCallerSql}
            """;

        const string exportSql = $"""
            UPDATE morphdb._morph_export_jobs
            SET status = 'cancelled', completed_at = @Now
            WHERE job_id = @JobId AND status IN ('pending', 'processing') AND {ReachableByCallerSql}
            """;

        var parameters = new { JobId = jobId, Now = DateTimeOffset.UtcNow, caller.ProjectId, caller.Bypass, caller.UserId };

        await using var connection = await _connections.OpenSystemAsync(cancellationToken);

        var importRows = await connection.ExecuteAsync(importSql, parameters);
        if (importRows > 0)
            return true;

        var exportRows = await connection.ExecuteAsync(exportSql, parameters);
        return exportRows > 0;
    }

    public async Task<BulkJobProgress?> GetJobProgressAsync(Guid projectId, Guid jobId, CancellationToken cancellationToken = default)
    {
        // Try import job first
        var importJob = await GetImportJobAsync(projectId, jobId, cancellationToken);
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
        var exportJob = await GetExportJobAsync(projectId, jobId, cancellationToken);
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

    public async Task<int> DeleteExpiredExportFilesAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        const string sql = """
            DELETE FROM morphdb._morph_export_data data
            USING morphdb._morph_export_jobs job
            WHERE data.job_id = job.job_id AND job.expires_at IS NOT NULL AND job.expires_at <= @Now
            """;

        await using var connection = await _connections.OpenSystemAsync(cancellationToken);
        return await connection.ExecuteAsync(sql, new { Now = now });
    }

    public async Task<Stream?> OpenExportFileAsync(Guid projectId, Guid jobId, CancellationToken cancellationToken = default)
    {
        // The lookup is the gate: past it, the caller is the job's requester or bypasses row-level
        // security, and either may have the file.
        if (await GetExportJobAsync(projectId, jobId, cancellationToken) is null)
        {
            return null;
        }

        const string sql = "SELECT data FROM morphdb._morph_export_data WHERE job_id = @JobId";

        byte[]? stored;
        await using (var connection = await _connections.OpenSystemAsync(cancellationToken))
        {
            stored = await connection.ExecuteScalarAsync<byte[]>(sql, new { JobId = jobId });
        }

        if (stored is not null)
        {
            return new MemoryStream(stored);
        }

        // No file was stored: the table is read now, as the requester the job's row holds, so a caller
        // that bypasses row-level security opening another caller's job is handed that caller's rows,
        // not every row. A read of a completed job leaves the job as it is.
        var job = await ReadExportJobForExecutionAsync(jobId, cancellationToken)
            ?? throw new NotFoundException("Export job", jobId.ToString());
        var streamed = new MemoryStream();
        await WriteExportAsync(job, streamed, _ => Task.CompletedTask, cancellationToken);
        streamed.Position = 0;
        return streamed;
    }

    #endregion

    #region Carrying Jobs Out

    /// <summary>
    /// The job as stored, found by its id alone — for the two methods that carry a job out. Not for
    /// a caller: <see cref="GetImportJobAsync"/> is what answers one.
    /// </summary>
    private async Task<BulkImportJob?> ReadImportJobForExecutionAsync(Guid jobId, CancellationToken cancellationToken)
    {
        const string sql = $"""
            SELECT {ImportJobColumns}
            FROM morphdb._morph_import_jobs
            WHERE job_id = @JobId
            """;

        await using var connection = await _connections.OpenSystemAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<ImportJobRow>(sql, new { JobId = jobId });

        return row is null ? null : MapToImportJob(row);
    }

    /// <summary>
    /// The job as stored, found by its id alone — for the two methods that carry a job out. Not for
    /// a caller: <see cref="GetExportJobAsync"/> is what answers one.
    /// </summary>
    private async Task<BulkExportJob?> ReadExportJobForExecutionAsync(Guid jobId, CancellationToken cancellationToken)
    {
        const string sql = $"""
            SELECT {ExportJobColumns}
            FROM morphdb._morph_export_jobs
            WHERE job_id = @JobId
            """;

        await using var connection = await _connections.OpenSystemAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<ExportJobRow>(sql, new { JobId = jobId });

        return row is null ? null : MapToExportJob(row);
    }

    public async Task<IReadOnlyList<BulkImportJob>> GetPendingImportJobsAsync(
        int limit = 10,
        CancellationToken cancellationToken = default)
    {
        const string sql = $"""
            SELECT {ImportJobColumns}
            FROM morphdb._morph_import_jobs
            WHERE status = 'pending'
            ORDER BY created_at ASC
            LIMIT @Limit
            """;

        await using var connection = await _connections.OpenSystemAsync(cancellationToken);
        var rows = await connection.QueryAsync<ImportJobRow>(sql, new { Limit = limit });

        return rows.Select(MapToImportJob).ToList();
    }

    public async Task<IReadOnlyList<BulkExportJob>> GetPendingExportJobsAsync(
        int limit = 10,
        CancellationToken cancellationToken = default)
    {
        const string sql = $"""
            SELECT {ExportJobColumns}
            FROM morphdb._morph_export_jobs
            WHERE status = 'pending'
            ORDER BY created_at ASC
            LIMIT @Limit
            """;

        await using var connection = await _connections.OpenSystemAsync(cancellationToken);
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

        await using var connection = await _connections.OpenSystemAsync(cancellationToken);
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

    #endregion

    #region Private Helpers - Parsing

    /// <summary>
    /// Writes one row as <paramref name="requester"/>, the caller the job was requested by.
    /// </summary>
    /// <remarks>
    /// The requester is made ambient here, for each row, and not once around the loop that calls
    /// this. That loop is an iterator: after every row it hands back, it is resumed in whatever
    /// context its consumer happens to be in, and a context set before the first row would be gone
    /// by the second.
    /// </remarks>
    private async Task<ImportRowResult> ProcessImportRowAsync(
        BulkImportJob job,
        TableMetadata? table,
        IDictionary<string, object?> row,
        long rowNumber,
        SecurityContext? requester)
    {
        using var asRequester = SecurityContextScope.Begin(_securityContext, requester);

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
                // A row's error is read by whoever requested the job. MorphDB's own errors name logical
                // columns; PostgreSQL's name physical ones, which are not the caller's to see.
                Error = ex is PostgresException pg
                    ? $"The row could not be written (SQLSTATE {pg.SqlState})."
                    : ex.Message
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

    /// <summary>
    /// A JSON or NDJSON row as the write pipeline receives a JSON row from <c>POST /api/data</c>: each
    /// value the element it is, read against its column's type by the one conversion every JSON door
    /// shares (<see cref="Infrastructure.TypeMapper.ToDbValue"/>). Converting here instead, by the
    /// value's JSON kind alone, handed a date or an id column a string, which PostgreSQL refused.
    /// </summary>
    private static Dictionary<string, object?> JsonElementToDictionary(JsonElement element)
    {
        var dict = new Dictionary<string, object?>();

        foreach (var property in element.EnumerateObject())
        {
            dict[property.Name] = property.Value.ValueKind == JsonValueKind.Null ? null : property.Value.Clone();
        }

        return dict;
    }

    #endregion

    #region Private Helpers - Export

    private async Task StreamCsvExportAsync(
        BulkExportJob job,
        CsvExportOptions options,
        Stream outputStream,
        Func<long, Task> progressCallback,
        CancellationToken cancellationToken)
    {
        await using var writer = new StreamWriter(outputStream, Encoding.UTF8, leaveOpen: true);

        var table = await _schemaManager.GetTableAsync(job.ProjectId, job.TableName, cancellationToken)
            ?? throw new TableNotFoundException(job.TableName);

        var columns = ResolveExportColumns(table, options.Columns).Select(c => c.LogicalName).ToList();

        // Write header
        if (options.IncludeHeader)
        {
            await writer.WriteLineAsync(string.Join(options.Delimiter, columns.Select(EscapeCsvValue)));
        }

        // Stream data
        var processedRows = 0L;
        await foreach (var row in StreamTableDataAsync(job.ProjectId, job.TableName, columns, cancellationToken))
        {
            var values = columns.Select(col => FormatCsvValue(row.TryGetValue(col, out var v) ? v : null, options.DateFormat));
            await writer.WriteLineAsync(string.Join(options.Delimiter, values.Select(EscapeCsvValue)));

            processedRows++;
            if (processedRows % _options.ProgressUpdateInterval == 0)
            {
                await progressCallback(processedRows);
            }
        }

        await writer.FlushAsync(cancellationToken);
        await progressCallback(processedRows);
    }

    private async Task StreamJsonExportAsync(
        BulkExportJob job,
        JsonExportOptions options,
        Stream outputStream,
        Func<long, Task> progressCallback,
        CancellationToken cancellationToken)
    {
        var jsonOptions = new JsonWriterOptions { Indented = options.Pretty };
        await using var writer = new Utf8JsonWriter(outputStream, jsonOptions);

        var table = await _schemaManager.GetTableAsync(job.ProjectId, job.TableName, cancellationToken)
            ?? throw new TableNotFoundException(job.TableName);

        var columns = ResolveExportColumns(table, options.Columns).Select(c => c.LogicalName).ToList();

        writer.WriteStartArray();

        var processedRows = 0L;
        await foreach (var row in StreamTableDataAsync(job.ProjectId, job.TableName, columns, cancellationToken))
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
                await progressCallback(processedRows);
            }
        }

        writer.WriteEndArray();
        await writer.FlushAsync(cancellationToken);
        await progressCallback(processedRows);
    }

    private async Task StreamXlsxExportAsync(
        BulkExportJob job,
        XlsxExportOptions options,
        Stream outputStream,
        Func<long, Task> progressCallback,
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

        var columns = ResolveExportColumns(table, options.Columns).Select(c => c.LogicalName).ToList();

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
        await foreach (var row in StreamTableDataAsync(job.ProjectId, job.TableName, columns, cancellationToken))
        {
            for (var i = 0; i < columns.Count; i++)
            {
                SetXlsxCellValue(worksheet.Cell(rowIndex, i + 1), row.TryGetValue(columns[i], out var v) ? v : null);
            }
            rowIndex++;

            processedRows++;
            if (processedRows % _options.ProgressUpdateInterval == 0)
            {
                await progressCallback(processedRows);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        workbook.SaveAs(outputStream);
        await progressCallback(processedRows);
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
        CancellationToken cancellationToken)
    {
        var table = await _schemaManager.GetTableAsync(projectId, tableName, cancellationToken)
            ?? throw new TableNotFoundException(tableName);

        var sql = $"SELECT COUNT(*) FROM {table.PhysicalName}{ActiveRowsOnly(table)}";

        await using var connection = await _connections.OpenAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<long>(sql);
    }

    /// <summary>
    /// The condition an export reads a soft-delete table under: the rows a list of it would show, not
    /// the rows a delete has marked. Empty for a table without soft delete.
    /// </summary>
    private static string ActiveRowsOnly(TableMetadata table) =>
        table.SoftDeleteEnabled ? $" WHERE {SystemColumns.DeletedAt} IS NULL" : "";

    private async IAsyncEnumerable<IDictionary<string, object?>> StreamTableDataAsync(
        Guid projectId,
        string tableName,
        List<string> columns,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var table = await _schemaManager.GetTableAsync(projectId, tableName, cancellationToken)
            ?? throw new TableNotFoundException(tableName);

        // Only physical names of exposed columns reach the SELECT list, and quoted: the caller's
        // strings were resolved against the table's metadata and refused if they matched nothing.
        var exportColumns = ResolveExportColumns(table, columns);
        var selectList = string.Join(", ", exportColumns.Select(c => DmlBuilder.QuoteIdentifier(c.PhysicalName)));

        // An export is the whole table in storage order; a subset is the query API's job.
        var sql = $"SELECT {selectList} FROM {table.PhysicalName}{ActiveRowsOnly(table)}";

        await using var connection = await _connections.OpenAsync(cancellationToken);
        using var reader = await connection.ExecuteReaderAsync(sql);

        while (await reader.ReadAsync(cancellationToken))
        {
            var row = new Dictionary<string, object?>();
            for (var i = 0; i < exportColumns.Count; i++)
            {
                row[exportColumns[i].LogicalName] = reader.IsDBNull(i) ? null : reader.GetValue(i);
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

        await using var connection = await _connections.OpenSystemAsync(cancellationToken);
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

        await using var connection = await _connections.OpenSystemAsync(cancellationToken);
        var data = await connection.ExecuteScalarAsync<byte[]>(sql, new { JobId = jobId });

        return data is null ? null : new MemoryStream(data);
    }

    private async Task DeleteStoredImportDataAsync(Guid jobId, CancellationToken cancellationToken)
    {
        const string sql = "DELETE FROM morphdb._morph_import_data WHERE job_id = @JobId";

        await using var connection = await _connections.OpenSystemAsync(cancellationToken);
        await connection.ExecuteAsync(sql, new { JobId = jobId });
    }

    private const string ImportRequesterSql =
        "SELECT requested_by::text FROM morphdb._morph_import_jobs WHERE job_id = @JobId";

    private const string ExportRequesterSql =
        "SELECT requested_by::text FROM morphdb._morph_export_jobs WHERE job_id = @JobId";

    /// <summary>
    /// The caller a job was requested by, as its row holds it — or <c>null</c>: a job requested
    /// without a caller, or stored by a version that did not record one. Such a job runs as nobody;
    /// a caller is not guessed for it.
    /// </summary>
    /// <remarks>
    /// Read here, by the two methods that carry a job out, and kept off <see cref="BulkImportJob"/>
    /// and <see cref="BulkExportJob"/>: those are what a status request answers with, and who asked
    /// for a job — claims included — is not part of that answer. <see cref="ReachableByCallerSql"/>
    /// compares with the stored requester inside PostgreSQL, and hands nothing of it back either.
    /// </remarks>
    private async Task<SecurityContext?> GetRequesterAsync(string sql, Guid jobId, CancellationToken cancellationToken)
    {
        await using var connection = await _connections.OpenSystemAsync(cancellationToken);
        var stored = await connection.ExecuteScalarAsync<string?>(
            new CommandDefinition(sql, new { JobId = jobId }, cancellationToken: cancellationToken));

        return SecurityContextSnapshot.Read(stored);
    }

    /// <summary>
    /// Stores the job, and with it the ambient security context: the caller making this request is
    /// the caller the job's rows are later written as.
    /// </summary>
    private async Task SaveImportJobAsync(BulkImportJob job, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO morphdb._morph_import_jobs (
                job_id, project_id, table_id, table_name, format, status,
                total_rows, processed_rows, success_count, error_count,
                error_message, options, requested_by, created_at
            ) VALUES (
                @JobId, @ProjectId, @TableId, @TableName, @Format, @Status,
                @TotalRows, @ProcessedRows, @SuccessCount, @ErrorCount,
                @ErrorMessage, @Options::jsonb, @RequestedBy::jsonb, @CreatedAt
            )
            """;

        await using var connection = await _connections.OpenSystemAsync(cancellationToken);
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
            RequestedBy = SecurityContextSnapshot.Write(_securityContext.ContextOrNull),
            job.CreatedAt
        });
    }

    /// <summary>
    /// The columns an export carries: the caller's selection, or every exposed column when it made
    /// none. A name that is not an exposed column of the table — a typo, a physical name, the
    /// internal project column, anything shaped like SQL — is refused here, before any SQL exists,
    /// with the same error the query surface answers. What this returns is the only thing that
    /// reaches the SELECT list.
    /// </summary>
    private static List<ColumnMetadata> ResolveExportColumns(TableMetadata table, IReadOnlyList<string>? requested)
    {
        var exposed = table.ExposedColumns().ToList();
        if (requested is null || requested.Count == 0)
        {
            return exposed;
        }

        var byLogicalName = exposed.ToDictionary(c => c.LogicalName, StringComparer.Ordinal);
        return requested
            .Select(name => byLogicalName.TryGetValue(name, out var column)
                ? column
                : throw new ColumnNotFoundException(table.LogicalName, name))
            .ToList();
    }

    /// <summary>
    /// Stores the job, and with it the ambient security context: the caller making this request is
    /// the caller the table is later read as.
    /// </summary>
    private async Task SaveExportJobAsync(BulkExportJob job, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO morphdb._morph_export_jobs (
                job_id, project_id, table_id, table_name, format, status,
                total_rows, processed_rows, file_path, file_size,
                error_message, options, requested_by, created_at, expires_at
            ) VALUES (
                @JobId, @ProjectId, @TableId, @TableName, @Format, @Status,
                @TotalRows, @ProcessedRows, @FilePath, @FileSize,
                @ErrorMessage, @Options::jsonb, @RequestedBy::jsonb, @CreatedAt, @ExpiresAt
            )
            """;

        await using var connection = await _connections.OpenSystemAsync(cancellationToken);
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
            RequestedBy = SecurityContextSnapshot.Write(_securityContext.ContextOrNull),
            job.CreatedAt,
            job.ExpiresAt
        });
    }

    /// <summary>
    /// Moves a job from pending to processing. False when it was not pending — cancelled before it
    /// started — and the job is left as it is.
    /// </summary>
    private async Task<bool> TryStartJobAsync(string jobTable, Guid jobId, CancellationToken cancellationToken)
    {
        var sql = $"""
            UPDATE morphdb.{jobTable}
            SET status = 'processing', started_at = COALESCE(started_at, @Now)
            WHERE job_id = @JobId AND status = 'pending'
            """;

        await using var connection = await _connections.OpenSystemAsync(cancellationToken);
        return await connection.ExecuteAsync(sql, new { JobId = jobId, Now = DateTimeOffset.UtcNow }) > 0;
    }

    /// <summary>Records an export's progress. False when the job is no longer processing.</summary>
    private async Task<bool> UpdateExportProgressAsync(Guid jobId, long processedRows, CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE morphdb._morph_export_jobs
            SET processed_rows = @ProcessedRows
            WHERE job_id = @JobId AND status = 'processing'
            """;

        await using var connection = await _connections.OpenSystemAsync(cancellationToken);
        return await connection.ExecuteAsync(sql, new { JobId = jobId, ProcessedRows = processedRows }) > 0;
    }

    /// <summary>Records an import's progress. False when the job is no longer processing.</summary>
    private async Task<bool> UpdateImportProgressAsync(
        Guid jobId,
        long processedRows,
        long successCount,
        long errorCount,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE morphdb._morph_import_jobs
            SET processed_rows = @ProcessedRows, success_count = @SuccessCount, error_count = @ErrorCount
            WHERE job_id = @JobId AND status = 'processing'
            """;

        await using var connection = await _connections.OpenSystemAsync(cancellationToken);
        return await connection.ExecuteAsync(sql, new { JobId = jobId, ProcessedRows = processedRows, SuccessCount = successCount, ErrorCount = errorCount }) > 0;
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
            WHERE job_id = @JobId AND status = 'processing'
            """;

        await using var connection = await _connections.OpenSystemAsync(cancellationToken);
        await connection.ExecuteAsync(sql, new
        {
            JobId = jobId,
            Status = BulkJobStatus.Completed.ToString().ToLowerInvariant(),
            TotalRows = totalRows,
            SuccessCount = successCount,
            ErrorCount = errorCount,
            ErrorDetails = errorDetails.Count == 0 ? null : JsonSerializer.Serialize(errorDetails),
            Now = DateTimeOffset.UtcNow
        });
    }

    /// <summary>Completes an export. False when it is no longer processing — cancelled while it ran.</summary>
    private async Task<bool> UpdateExportJobCompletedAsync(
        Guid jobId,
        long processedRows,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE morphdb._morph_export_jobs
            SET status = 'completed', processed_rows = @ProcessedRows, completed_at = @Now
            WHERE job_id = @JobId AND status = 'processing'
            """;

        await using var connection = await _connections.OpenSystemAsync(cancellationToken);
        return await connection.ExecuteAsync(sql, new { JobId = jobId, ProcessedRows = processedRows, Now = DateTimeOffset.UtcNow }) > 0;
    }

    private async Task UpdateImportJobFailedAsync(
        Guid jobId,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE morphdb._morph_import_jobs
            SET status = 'failed', error_message = @ErrorMessage, completed_at = @Now
            WHERE job_id = @JobId AND status IN ('pending', 'processing')
            """;

        await using var connection = await _connections.OpenSystemAsync(cancellationToken);
        await connection.ExecuteAsync(sql, new { JobId = jobId, ErrorMessage = errorMessage, Now = DateTimeOffset.UtcNow });
    }

    private async Task UpdateExportJobFailedAsync(
        Guid jobId,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE morphdb._morph_export_jobs
            SET status = 'failed', error_message = @ErrorMessage, completed_at = @Now
            WHERE job_id = @JobId AND status IN ('pending', 'processing')
            """;

        await using var connection = await _connections.OpenSystemAsync(cancellationToken);
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
