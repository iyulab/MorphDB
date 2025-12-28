using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Models;
using Npgsql;
using IOPath = System.IO.Path;

namespace MorphDB.Service.Services;

/// <summary>
/// PostgreSQL backup service using pg_dump and pg_restore.
/// </summary>
public sealed partial class BackupService : IBackupService, IDisposable
{
    private readonly IBackupRepository _repository;
    private readonly IProjectService _projectService;
    private readonly BackupOptions _options;
    private readonly string _connectionString;
    private readonly ILogger<BackupService> _logger;
    private readonly SemaphoreSlim _semaphore;

    public BackupService(
        IBackupRepository repository,
        IProjectService projectService,
        IOptions<BackupOptions> options,
        IConfiguration configuration,
        ILogger<BackupService> logger)
    {
        _repository = repository;
        _projectService = projectService;
        _options = options.Value;
        _connectionString = configuration.GetConnectionString("MorphDB")
            ?? throw new InvalidOperationException("Connection string 'MorphDB' not found.");
        _logger = logger;
        _semaphore = new SemaphoreSlim(_options.MaxConcurrentBackups);

        // Ensure backup directory exists
        Directory.CreateDirectory(_options.LocalStoragePath);
    }

    /// <inheritdoc/>
    public async Task<Backup> CreateBackupAsync(
        CreateBackupRequest request,
        CancellationToken cancellationToken = default)
    {
        var project = await _projectService.GetProjectAsync(request.ProjectId, cancellationToken)
            ?? throw new InvalidOperationException($"Project {request.ProjectId} not found.");

        var backupId = Guid.NewGuid();
        var startedAt = DateTimeOffset.UtcNow;
        var fileName = $"{project.Slug}_{startedAt:yyyyMMdd_HHmmss}_{backupId:N}.sql.gz";
        var filePath = IOPath.Combine(_options.LocalStoragePath, fileName);

        var backup = new Backup
        {
            BackupId = backupId,
            ProjectId = request.ProjectId,
            Name = request.Name,
            Description = request.Description,
            Type = request.Type,
            Status = BackupStatus.Pending,
            StoragePath = filePath,
            StorageType = BackupStorageType.Local,
            Compression = BackupCompression.Gzip,
            InitiatedBy = request.InitiatedBy,
            StartedAt = startedAt,
            ExpiresAt = request.ExpiresAt ?? (_options.DefaultExpirationDays > 0
                ? startedAt.AddDays(_options.DefaultExpirationDays)
                : null)
        };

        // Save initial backup record
        backup = await _repository.CreateAsync(backup, cancellationToken);

        LogBackupStarted(backupId, project.Slug, request.Type.ToString());

        // Execute backup asynchronously
        _ = ExecuteBackupAsync(backup, project, cancellationToken);

        return backup;
    }

    private async Task ExecuteBackupAsync(
        Backup backup,
        Project project,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            await _semaphore.WaitAsync(cancellationToken);

            // Update status to InProgress
            backup = await _repository.UpdateAsync(backup with { Status = BackupStatus.InProgress }, cancellationToken);

            // Build pg_dump arguments
            var schemas = new List<string> { project.SystemSchema, project.DataSchema };
            var args = BuildPgDumpArgs(backup.Type, schemas);

            // Execute pg_dump
            var (exitCode, stderr) = await ExecutePgDumpAsync(args, backup.StoragePath!, cancellationToken);

            stopwatch.Stop();

            if (exitCode != 0)
            {
                LogBackupFailed(backup.BackupId, exitCode, stderr);
                await _repository.UpdateAsync(backup with
                {
                    Status = BackupStatus.Failed,
                    ErrorMessage = stderr,
                    CompletedAt = DateTimeOffset.UtcNow,
                    Metadata = new BackupMetadata { DurationMs = stopwatch.ElapsedMilliseconds }
                }, cancellationToken);
                return;
            }

            // Calculate file size and checksum
            var fileInfo = new FileInfo(backup.StoragePath!);
            var checksum = await CalculateChecksumAsync(backup.StoragePath!, cancellationToken);

            // Update backup record with success
            await _repository.UpdateAsync(backup with
            {
                Status = BackupStatus.Completed,
                SizeBytes = fileInfo.Length,
                Checksum = checksum,
                CompletedAt = DateTimeOffset.UtcNow,
                Metadata = new BackupMetadata
                {
                    Schemas = schemas,
                    DurationMs = stopwatch.ElapsedMilliseconds
                }
            }, cancellationToken);

            LogBackupCompleted(backup.BackupId, fileInfo.Length, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            LogBackupException(backup.BackupId, ex);

            await _repository.UpdateAsync(backup with
            {
                Status = BackupStatus.Failed,
                ErrorMessage = ex.Message,
                CompletedAt = DateTimeOffset.UtcNow,
                Metadata = new BackupMetadata { DurationMs = stopwatch.ElapsedMilliseconds }
            }, cancellationToken);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private string BuildPgDumpArgs(BackupType type, List<string> schemas)
    {
        var args = new StringBuilder();

        // Connection string parsing
        var connBuilder = new NpgsqlConnectionStringBuilder(_connectionString);
        args.Append(CultureInfo.InvariantCulture, $"-h {connBuilder.Host} ");
        args.Append(CultureInfo.InvariantCulture, $"-p {connBuilder.Port} ");
        args.Append(CultureInfo.InvariantCulture, $"-U {connBuilder.Username} ");
        args.Append(CultureInfo.InvariantCulture, $"-d {connBuilder.Database} ");

        // Schema selection
        foreach (var schema in schemas)
        {
            args.Append(CultureInfo.InvariantCulture, $"-n {schema} ");
        }

        // Backup type options
        args.Append(type switch
        {
            BackupType.SchemaOnly => "--schema-only ",
            BackupType.DataOnly => "--data-only ",
            _ => ""
        });

        // Compression
        args.Append(CultureInfo.InvariantCulture, $"-Z {_options.CompressionLevel} ");

        // Additional options
        args.Append("--no-owner --no-privileges --if-exists --clean ");

        return args.ToString().Trim();
    }

    private async Task<(int ExitCode, string Stderr)> ExecutePgDumpAsync(
        string args,
        string outputPath,
        CancellationToken cancellationToken)
    {
        var pgDumpPath = _options.PgDumpPath ?? "pg_dump";

        // Parse connection string for password
        var connBuilder = new NpgsqlConnectionStringBuilder(_connectionString);

        var startInfo = new ProcessStartInfo
        {
            FileName = pgDumpPath,
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        // Set password via environment variable
        if (!string.IsNullOrEmpty(connBuilder.Password))
        {
            startInfo.Environment["PGPASSWORD"] = connBuilder.Password;
        }

        using var process = new Process { StartInfo = startInfo };

        var stderrBuilder = new StringBuilder();
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                stderrBuilder.AppendLine(e.Data);
            }
        };

        process.Start();
        process.BeginErrorReadLine();

        // Write stdout to file
        await using (var fileStream = File.Create(outputPath))
        {
            await process.StandardOutput.BaseStream.CopyToAsync(fileStream, cancellationToken);
        }

        await process.WaitForExitAsync(cancellationToken);

        return (process.ExitCode, stderrBuilder.ToString());
    }

    /// <inheritdoc/>
    public async Task<Backup?> GetBackupAsync(Guid backupId, CancellationToken cancellationToken = default)
    {
        return await _repository.GetByIdAsync(backupId, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Backup>> ListBackupsAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        return await _repository.ListByProjectAsync(projectId, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<RestoreResult> RestoreBackupAsync(
        RestoreBackupRequest request,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        var backup = await _repository.GetByIdAsync(request.BackupId, cancellationToken);
        if (backup is null)
        {
            return new RestoreResult
            {
                Success = false,
                ErrorMessage = "Backup not found."
            };
        }

        if (backup.Status != BackupStatus.Completed)
        {
            return new RestoreResult
            {
                Success = false,
                ErrorMessage = $"Backup is not in completed status. Current status: {backup.Status}"
            };
        }

        if (string.IsNullOrEmpty(backup.StoragePath) || !File.Exists(backup.StoragePath))
        {
            return new RestoreResult
            {
                Success = false,
                ErrorMessage = "Backup file not found."
            };
        }

        var project = await _projectService.GetProjectAsync(request.TargetProjectId, cancellationToken);
        if (project is null)
        {
            return new RestoreResult
            {
                Success = false,
                ErrorMessage = "Target project not found."
            };
        }

        LogRestoreStarted(request.BackupId, request.TargetProjectId);

        try
        {
            var (exitCode, stderr) = await ExecutePgRestoreAsync(
                backup.StoragePath,
                project,
                request.DropExisting,
                cancellationToken);

            stopwatch.Stop();

            if (exitCode != 0)
            {
                LogRestoreFailed(request.BackupId, exitCode, stderr);
                return new RestoreResult
                {
                    Success = false,
                    ErrorMessage = stderr,
                    DurationMs = stopwatch.ElapsedMilliseconds
                };
            }

            LogRestoreCompleted(request.BackupId, stopwatch.ElapsedMilliseconds);

            return new RestoreResult
            {
                Success = true,
                DurationMs = stopwatch.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            LogRestoreException(request.BackupId, ex);
            return new RestoreResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                DurationMs = stopwatch.ElapsedMilliseconds
            };
        }
    }

    private async Task<(int ExitCode, string Stderr)> ExecutePgRestoreAsync(
        string backupPath,
        Project project,
        bool dropExisting,
        CancellationToken cancellationToken)
    {
        // Since we use plain SQL format with compression, we use psql instead of pg_restore
        var psqlPath = _options.PgRestorePath?.Replace("pg_restore", "psql", StringComparison.Ordinal) ?? "psql";
        var connBuilder = new NpgsqlConnectionStringBuilder(_connectionString);

        var args = new StringBuilder();
        args.Append(CultureInfo.InvariantCulture, $"-h {connBuilder.Host} ");
        args.Append(CultureInfo.InvariantCulture, $"-p {connBuilder.Port} ");
        args.Append(CultureInfo.InvariantCulture, $"-U {connBuilder.Username} ");
        args.Append(CultureInfo.InvariantCulture, $"-d {connBuilder.Database} ");
        args.Append("-v ON_ERROR_STOP=1 ");

        var startInfo = new ProcessStartInfo
        {
            FileName = psqlPath,
            Arguments = args.ToString().Trim(),
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        if (!string.IsNullOrEmpty(connBuilder.Password))
        {
            startInfo.Environment["PGPASSWORD"] = connBuilder.Password;
        }

        using var process = new Process { StartInfo = startInfo };

        var stderrBuilder = new StringBuilder();
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                stderrBuilder.AppendLine(e.Data);
            }
        };

        process.Start();
        process.BeginErrorReadLine();

        // Decompress and pipe to stdin
        await using (var fileStream = File.OpenRead(backupPath))
        await using (var gzipStream = new System.IO.Compression.GZipStream(fileStream, System.IO.Compression.CompressionMode.Decompress))
        using (var reader = new StreamReader(gzipStream))
        {
            await process.StandardInput.WriteAsync(await reader.ReadToEndAsync(cancellationToken));
        }

        process.StandardInput.Close();
        await process.WaitForExitAsync(cancellationToken);

        return (process.ExitCode, stderrBuilder.ToString());
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteBackupAsync(Guid backupId, CancellationToken cancellationToken = default)
    {
        var backup = await _repository.GetByIdAsync(backupId, cancellationToken);
        if (backup is null)
        {
            return false;
        }

        // Delete file if exists
        if (!string.IsNullOrEmpty(backup.StoragePath) && File.Exists(backup.StoragePath))
        {
            try
            {
                File.Delete(backup.StoragePath);
                LogBackupFileDeleted(backupId, backup.StoragePath);
            }
            catch (Exception ex)
            {
                LogBackupFileDeleteFailed(backupId, backup.StoragePath, ex);
            }
        }

        return await _repository.DeleteAsync(backupId, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Stream?> DownloadBackupAsync(Guid backupId, CancellationToken cancellationToken = default)
    {
        var backup = await _repository.GetByIdAsync(backupId, cancellationToken);
        if (backup is null || string.IsNullOrEmpty(backup.StoragePath) || !File.Exists(backup.StoragePath))
        {
            return null;
        }

        return File.OpenRead(backup.StoragePath);
    }

    private static async Task<string> CalculateChecksumAsync(string filePath, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(filePath);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    #region Logging

    [LoggerMessage(Level = LogLevel.Information, Message = "Backup started: {BackupId} for project {ProjectSlug} (type: {BackupType})")]
    private partial void LogBackupStarted(Guid backupId, string projectSlug, string backupType);

    [LoggerMessage(Level = LogLevel.Information, Message = "Backup completed: {BackupId} (size: {SizeBytes} bytes, duration: {DurationMs}ms)")]
    private partial void LogBackupCompleted(Guid backupId, long sizeBytes, long durationMs);

    [LoggerMessage(Level = LogLevel.Error, Message = "Backup failed: {BackupId} (exit code: {ExitCode}, error: {Stderr})")]
    private partial void LogBackupFailed(Guid backupId, int exitCode, string stderr);

    [LoggerMessage(Level = LogLevel.Error, Message = "Backup exception: {BackupId}")]
    private partial void LogBackupException(Guid backupId, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Restore started: {BackupId} to project {TargetProjectId}")]
    private partial void LogRestoreStarted(Guid backupId, Guid targetProjectId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Restore completed: {BackupId} (duration: {DurationMs}ms)")]
    private partial void LogRestoreCompleted(Guid backupId, long durationMs);

    [LoggerMessage(Level = LogLevel.Error, Message = "Restore failed: {BackupId} (exit code: {ExitCode}, error: {Stderr})")]
    private partial void LogRestoreFailed(Guid backupId, int exitCode, string stderr);

    [LoggerMessage(Level = LogLevel.Error, Message = "Restore exception: {BackupId}")]
    private partial void LogRestoreException(Guid backupId, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Backup file deleted: {BackupId} at {FilePath}")]
    private partial void LogBackupFileDeleted(Guid backupId, string filePath);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to delete backup file: {BackupId} at {FilePath}")]
    private partial void LogBackupFileDeleteFailed(Guid backupId, string filePath, Exception exception);

    #endregion

    /// <summary>
    /// Disposes the backup service resources.
    /// </summary>
    public void Dispose()
    {
        _semaphore.Dispose();
    }
}
