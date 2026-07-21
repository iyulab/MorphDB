using Microsoft.AspNetCore.Mvc;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Exceptions;
using MorphDB.Core.Models;
using MorphDB.Service.Filters;
using MorphDB.Service.Models.Api;
using MorphDB.Service.Services;

namespace MorphDB.Service.Controllers;

/// <summary>
/// Controller for bulk import/export operations.
/// </summary>
[ApiController]
[Route("api/bulk")]
[RequireProject]
public sealed class BulkController : ControllerBase
{
    private readonly IBulkOperationService _bulkService;
    private readonly IProjectContextAccessor _projectContext;
    private readonly ILogger<BulkController> _logger;

    public BulkController(
        IBulkOperationService bulkService,
        IProjectContextAccessor projectContext,
        ILogger<BulkController> logger)
    {
        _bulkService = bulkService;
        _projectContext = projectContext;
        _logger = logger;
    }

    private Guid GetProjectId() => _projectContext.ProjectId;

    #region Import Operations

    /// <summary>
    /// Start a CSV import.
    /// </summary>
    [HttpPost("{table}/import/csv")]
    [Consumes("text/csv", "application/octet-stream")]
    [ProducesResponseType(typeof(ImportJobApiResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ImportCsv(
        string table,
        [FromQuery] CsvImportApiRequest? options,
        CancellationToken cancellationToken = default)
    {
        var projectId = GetProjectId();

        var csvOptions = new CsvImportOptions
        {
            Delimiter = options?.Delimiter ?? ',',
            HasHeader = options?.HasHeader ?? true,
            DateFormat = options?.DateFormat,
            TrimWhitespace = options?.TrimWhitespace ?? true,
            NullHandling = ParseNullHandling(options?.NullHandling),
            DuplicateHandling = ParseDuplicateHandling(options?.DuplicateHandling),
            KeyColumns = options?.KeyColumns
        };

        var job = await _bulkService.StartCsvImportAsync(
            projectId,
            table,
            Request.Body,
            csvOptions,
            cancellationToken);

        return AcceptedAtAction(
            nameof(GetImportJob),
            new { jobId = job.JobId },
            ToImportJobResponse(job));
    }

    /// <summary>
    /// Start a JSON import.
    /// </summary>
    [HttpPost("{table}/import/json")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(ImportJobApiResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ImportJson(
        string table,
        [FromQuery] JsonImportApiRequest? options,
        CancellationToken cancellationToken = default)
    {
        var projectId = GetProjectId();

        var jsonOptions = new JsonImportOptions
        {
            DateFormat = options?.DateFormat,
            DuplicateHandling = ParseDuplicateHandling(options?.DuplicateHandling),
            KeyColumns = options?.KeyColumns
        };

        var job = await _bulkService.StartJsonImportAsync(
            projectId,
            table,
            Request.Body,
            jsonOptions,
            cancellationToken);

        return AcceptedAtAction(
            nameof(GetImportJob),
            new { jobId = job.JobId },
            ToImportJobResponse(job));
    }

    /// <summary>
    /// Start an NDJSON import.
    /// </summary>
    [HttpPost("{table}/import/ndjson")]
    [Consumes("application/x-ndjson", "application/jsonl")]
    [ProducesResponseType(typeof(ImportJobApiResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ImportNdjson(
        string table,
        [FromQuery] JsonImportApiRequest? options,
        CancellationToken cancellationToken = default)
    {
        var projectId = GetProjectId();

        var jsonOptions = new JsonImportOptions
        {
            DateFormat = options?.DateFormat,
            DuplicateHandling = ParseDuplicateHandling(options?.DuplicateHandling),
            KeyColumns = options?.KeyColumns
        };

        var job = await _bulkService.StartNdjsonImportAsync(
            projectId,
            table,
            Request.Body,
            jsonOptions,
            cancellationToken);

        return AcceptedAtAction(
            nameof(GetImportJob),
            new { jobId = job.JobId },
            ToImportJobResponse(job));
    }

    /// <summary>
    /// Get import job status.
    /// </summary>
    [HttpGet("import/{jobId:guid}")]
    [ProducesResponseType(typeof(ImportJobApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetImportJob(Guid jobId, CancellationToken cancellationToken = default)
    {
        var job = await _bulkService.GetImportJobAsync(jobId, cancellationToken);

        if (job == null)
        {
            return NotFound(new ErrorResponse
            {
                Error = "NotFound",
                Message = $"Import job with ID '{jobId}' not found",
                Code = "JOB_NOT_FOUND"
            });
        }

        return Ok(ToImportJobResponse(job));
    }

    /// <summary>
    /// List import jobs for a project.
    /// </summary>
    [HttpGet("import")]
    [ProducesResponseType(typeof(IReadOnlyList<ImportJobApiResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListImportJobs(
        [FromQuery] int limit = 50,
        [FromQuery] int offset = 0,
        CancellationToken cancellationToken = default)
    {
        var projectId = GetProjectId();
        var jobs = await _bulkService.ListImportJobsAsync(projectId, limit, offset, cancellationToken);
        return Ok(jobs.Select(ToImportJobResponse).ToList());
    }

    #endregion

    #region Export Operations

    /// <summary>
    /// Start a CSV export.
    /// </summary>
    [HttpPost("{table}/export/csv")]
    [ProducesResponseType(typeof(ExportJobApiResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ExportCsv(
        string table,
        [FromBody] CsvExportApiRequest? options,
        CancellationToken cancellationToken = default)
    {
        var projectId = GetProjectId();

        var csvOptions = new CsvExportOptions
        {
            Delimiter = options?.Delimiter ?? ',',
            IncludeHeader = options?.IncludeHeader ?? true,
            DateFormat = options?.DateFormat,
            Columns = options?.Columns,
            Filter = options?.Filter,
            OrderBy = options?.OrderBy
        };

        var job = await _bulkService.StartCsvExportAsync(projectId, table, csvOptions, cancellationToken);

        return AcceptedAtAction(
            nameof(GetExportJob),
            new { jobId = job.JobId },
            ToExportJobResponse(job));
    }

    /// <summary>
    /// Start a JSON export.
    /// </summary>
    [HttpPost("{table}/export/json")]
    [ProducesResponseType(typeof(ExportJobApiResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ExportJson(
        string table,
        [FromBody] JsonExportApiRequest? options,
        CancellationToken cancellationToken = default)
    {
        var projectId = GetProjectId();

        var jsonOptions = new JsonExportOptions
        {
            Pretty = options?.Pretty ?? false,
            DateFormat = options?.DateFormat,
            Columns = options?.Columns,
            Filter = options?.Filter,
            OrderBy = options?.OrderBy
        };

        var job = await _bulkService.StartJsonExportAsync(projectId, table, jsonOptions, cancellationToken);

        return AcceptedAtAction(
            nameof(GetExportJob),
            new { jobId = job.JobId },
            ToExportJobResponse(job));
    }

    /// <summary>
    /// Start an XLSX export.
    /// </summary>
    [HttpPost("{table}/export/xlsx")]
    [ProducesResponseType(typeof(ExportJobApiResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ExportXlsx(
        string table,
        [FromBody] XlsxExportApiRequest? options,
        CancellationToken cancellationToken = default)
    {
        var projectId = GetProjectId();

        var xlsxOptions = new XlsxExportOptions
        {
            SheetName = options?.SheetName ?? "Data",
            IncludeHeader = options?.IncludeHeader ?? true,
            Columns = options?.Columns,
            Filter = options?.Filter,
            OrderBy = options?.OrderBy
        };

        var job = await _bulkService.StartXlsxExportAsync(projectId, table, xlsxOptions, cancellationToken);

        return AcceptedAtAction(
            nameof(GetExportJob),
            new { jobId = job.JobId },
            ToExportJobResponse(job));
    }

    /// <summary>
    /// Get export job status.
    /// </summary>
    [HttpGet("export/{jobId:guid}")]
    [ProducesResponseType(typeof(ExportJobApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetExportJob(Guid jobId, CancellationToken cancellationToken = default)
    {
        var job = await _bulkService.GetExportJobAsync(jobId, cancellationToken);

        if (job == null)
        {
            return NotFound(new ErrorResponse
            {
                Error = "NotFound",
                Message = $"Export job with ID '{jobId}' not found",
                Code = "JOB_NOT_FOUND"
            });
        }

        return Ok(ToExportJobResponse(job));
    }

    /// <summary>
    /// Download export file.
    /// </summary>
    [HttpGet("export/{jobId:guid}/download")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> DownloadExport(Guid jobId, CancellationToken cancellationToken = default)
    {
        var job = await _bulkService.GetExportJobAsync(jobId, cancellationToken);

        if (job == null)
        {
            return NotFound(new ErrorResponse
            {
                Error = "NotFound",
                Message = $"Export job with ID '{jobId}' not found",
                Code = "JOB_NOT_FOUND"
            });
        }

        if (job.Status != BulkJobStatus.Completed)
        {
            return BadRequest(new ErrorResponse
            {
                Error = "BadRequest",
                Message = $"Export job is not completed. Current status: {job.Status}",
                Code = "JOB_NOT_COMPLETED"
            });
        }

        var contentType = job.Format switch
        {
            ExportFormat.Csv => "text/csv",
            ExportFormat.Json => "application/json",
            ExportFormat.Xlsx => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            _ => "application/octet-stream"
        };

        var extension = job.Format switch
        {
            ExportFormat.Csv => "csv",
            ExportFormat.Json => "json",
            ExportFormat.Xlsx => "xlsx",
            _ => "bin"
        };

        var fileName = $"{job.TableName}_{job.JobId:N}.{extension}";

        // Try to get stored export data first (processed by background service)
        var storedStream = await _bulkService.GetStoredExportDataAsync(jobId, cancellationToken);
        if (storedStream != null)
        {
            return File(storedStream, contentType, fileName);
        }

        // Fallback: stream export on-the-fly if not stored
        var stream = new MemoryStream();
        await _bulkService.StreamExportAsync(jobId, stream, cancellationToken);
        stream.Position = 0;

        return File(stream, contentType, fileName);
    }

    /// <summary>
    /// List export jobs for a project.
    /// </summary>
    [HttpGet("export")]
    [ProducesResponseType(typeof(IReadOnlyList<ExportJobApiResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListExportJobs(
        [FromQuery] int limit = 50,
        [FromQuery] int offset = 0,
        CancellationToken cancellationToken = default)
    {
        var projectId = GetProjectId();
        var jobs = await _bulkService.ListExportJobsAsync(projectId, limit, offset, cancellationToken);
        return Ok(jobs.Select(ToExportJobResponse).ToList());
    }

    #endregion

    #region Job Management

    /// <summary>
    /// Get job progress.
    /// </summary>
    [HttpGet("jobs/{jobId:guid}/progress")]
    [ProducesResponseType(typeof(JobProgressApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetJobProgress(Guid jobId, CancellationToken cancellationToken = default)
    {
        var progress = await _bulkService.GetJobProgressAsync(jobId, cancellationToken);

        if (progress == null)
        {
            return NotFound(new ErrorResponse
            {
                Error = "NotFound",
                Message = $"Job with ID '{jobId}' not found",
                Code = "JOB_NOT_FOUND"
            });
        }

        return Ok(new JobProgressApiResponse
        {
            JobId = progress.JobId,
            Status = progress.Status.ToString().ToLowerInvariant(),
            TotalRows = progress.TotalRows,
            ProcessedRows = progress.ProcessedRows,
            SuccessCount = progress.SuccessCount,
            ErrorCount = progress.ErrorCount,
            PercentComplete = progress.PercentComplete,
            EstimatedTimeRemaining = progress.EstimatedTimeRemaining
        });
    }

    /// <summary>
    /// Cancel a running job.
    /// </summary>
    [HttpPost("jobs/{jobId:guid}/cancel")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CancelJob(Guid jobId, CancellationToken cancellationToken = default)
    {
        var cancelled = await _bulkService.CancelJobAsync(jobId, cancellationToken);

        if (!cancelled)
        {
            return NotFound(new ErrorResponse
            {
                Error = "NotFound",
                Message = $"Job with ID '{jobId}' not found or cannot be cancelled",
                Code = "JOB_NOT_FOUND"
            });
        }

        return NoContent();
    }

    #endregion

    #region Helper Methods

    private static ImportJobApiResponse ToImportJobResponse(BulkImportJob job) => new()
    {
        JobId = job.JobId,
        TableName = job.TableName,
        Format = job.Format.ToString().ToLowerInvariant(),
        Status = job.Status.ToString().ToLowerInvariant(),
        TotalRows = job.TotalRows,
        ProcessedRows = job.ProcessedRows,
        SuccessCount = job.SuccessCount,
        ErrorCount = job.ErrorCount,
        ErrorMessage = job.ErrorMessage,
        CreatedAt = job.CreatedAt,
        StartedAt = job.StartedAt,
        CompletedAt = job.CompletedAt
    };

    private static ExportJobApiResponse ToExportJobResponse(BulkExportJob job) => new()
    {
        JobId = job.JobId,
        TableName = job.TableName,
        Format = job.Format.ToString().ToLowerInvariant(),
        Status = job.Status.ToString().ToLowerInvariant(),
        TotalRows = job.TotalRows,
        ProcessedRows = job.ProcessedRows,
        FileSize = job.FileSize,
        ErrorMessage = job.ErrorMessage,
        CreatedAt = job.CreatedAt,
        StartedAt = job.StartedAt,
        CompletedAt = job.CompletedAt,
        ExpiresAt = job.ExpiresAt
    };

    private static NullHandling ParseNullHandling(string? value) => value?.ToLowerInvariant().Replace("-", "").Replace("_", "") switch
    {
        "emptyasnull" => NullHandling.EmptyAsNull,
        "preserveempty" => NullHandling.PreserveEmpty,
        "nullstringasnull" => NullHandling.NullStringAsNull,
        _ => NullHandling.EmptyAsNull
    };

    private static DuplicateHandling ParseDuplicateHandling(string? value) => value?.ToLowerInvariant() switch
    {
        "insert" => DuplicateHandling.Insert,
        "update" => DuplicateHandling.Update,
        "upsert" => DuplicateHandling.Upsert,
        "skip" => DuplicateHandling.Skip,
        "error" => DuplicateHandling.Error,
        _ => DuplicateHandling.Insert
    };

    #endregion
}
