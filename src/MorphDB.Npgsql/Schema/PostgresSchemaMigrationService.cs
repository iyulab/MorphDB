using System.Security.Cryptography;
using System.Text;
using Dapper;
using Microsoft.Extensions.Logging;
using MorphDB.Core.Abstractions;
using Npgsql;

namespace MorphDB.Npgsql.Schema;

/// <summary>
/// PostgreSQL implementation of schema migration management.
/// Handles versioned schema changes with locking and rollback support.
/// </summary>
public sealed partial class PostgresSchemaMigrationService : ISchemaMigrationService
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly ISchemaNameResolver _schemaNameResolver;
    private readonly ILogger<PostgresSchemaMigrationService> _logger;
    private readonly List<MigrationDefinition> _registeredMigrations = [];

    // Advisory lock ID for migration operations (prevents concurrent migrations)
    private const int MigrationLockId = 0x4D4F5250; // "MORP" in hex

    public PostgresSchemaMigrationService(
        NpgsqlDataSource dataSource,
        ISchemaNameResolver schemaNameResolver,
        ILogger<PostgresSchemaMigrationService> logger)
    {
        _dataSource = dataSource;
        _schemaNameResolver = schemaNameResolver;
        _logger = logger;
    }

    /// <summary>
    /// Registers a migration definition to be applied.
    /// </summary>
    /// <param name="migration">The migration definition.</param>
    public void RegisterMigration(MigrationDefinition migration)
    {
        if (_registeredMigrations.Any(m => m.Version == migration.Version))
        {
            throw new InvalidOperationException(
                $"Migration version {migration.Version} is already registered");
        }

        _registeredMigrations.Add(migration);
        _registeredMigrations.Sort((a, b) => a.Version.CompareTo(b.Version));

        LogMigrationRegistered(_logger, migration.Version, migration.Name);
    }

    /// <inheritdoc/>
    public async Task<int> GetCurrentVersionAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var schemaNames = _schemaNameResolver.GetSchemaNames(projectId);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        var sql = $"""
            SELECT COALESCE(MAX("version"), 0)
            FROM {QuoteIdentifier(schemaNames.SystemSchema)}."_migrations"
            WHERE "is_rolled_back" = false
            """;

        var version = await connection.ExecuteScalarAsync<int>(sql);

        return version;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<MigrationRecord>> GetAppliedMigrationsAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var schemaNames = _schemaNameResolver.GetSchemaNames(projectId);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        var sql = $"""
            SELECT
                "id" AS Id,
                "version" AS Version,
                "name" AS Name,
                "applied_at" AS AppliedAt,
                "duration_ms" AS DurationMs,
                "checksum" AS Checksum,
                "is_rolled_back" AS IsRolledBack,
                "rolled_back_at" AS RolledBackAt
            FROM {QuoteIdentifier(schemaNames.SystemSchema)}."_migrations"
            ORDER BY "version"
            """;

        var results = await connection.QueryAsync<MigrationRecordDto>(sql);

        return results.Select(r => new MigrationRecord
        {
            Id = r.Id,
            Version = r.Version,
            Name = r.Name,
            AppliedAt = r.AppliedAt,
            DurationMs = r.DurationMs,
            Checksum = r.Checksum,
            IsRolledBack = r.IsRolledBack,
            RolledBackAt = r.RolledBackAt
        }).ToList();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<MigrationInfo>> GetPendingMigrationsAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var currentVersion = await GetCurrentVersionAsync(projectId, cancellationToken);

        return _registeredMigrations
            .Where(m => m.Version > currentVersion)
            .Select(m => new MigrationInfo
            {
                Version = m.Version,
                Name = m.Name,
                Description = m.Description,
                IsReversible = m.DownScript is not null,
                CreatedAt = m.CreatedAt
            })
            .ToList();
    }

    /// <inheritdoc/>
    public async Task<MigrationResult> MigrateAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var targetVersion = _registeredMigrations.Count > 0
            ? _registeredMigrations.Max(m => m.Version)
            : 0;

        return await MigrateToVersionAsync(projectId, targetVersion, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<MigrationResult> MigrateToVersionAsync(
        Guid projectId,
        int targetVersion,
        CancellationToken cancellationToken = default)
    {
        var schemaNames = _schemaNameResolver.GetSchemaNames(projectId);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var steps = new List<MigrationStepResult>();

        LogMigrationStarted(_logger, projectId, targetVersion);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        // Acquire advisory lock
        if (!await TryAcquireMigrationLockAsync(connection, projectId, cancellationToken))
        {
            return new MigrationResult
            {
                Success = false,
                FromVersion = 0,
                ToVersion = targetVersion,
                Steps = steps,
                TotalDurationMs = sw.ElapsedMilliseconds,
                ErrorMessage = "Could not acquire migration lock. Another migration may be in progress."
            };
        }

        try
        {
            var currentVersion = await GetCurrentVersionAsync(projectId, cancellationToken);
            var fromVersion = currentVersion;

            if (targetVersion == currentVersion)
            {
                return new MigrationResult
                {
                    Success = true,
                    FromVersion = fromVersion,
                    ToVersion = targetVersion,
                    Steps = steps,
                    TotalDurationMs = sw.ElapsedMilliseconds
                };
            }

            if (targetVersion > currentVersion)
            {
                // Forward migration
                var migrationsToApply = _registeredMigrations
                    .Where(m => m.Version > currentVersion && m.Version <= targetVersion)
                    .OrderBy(m => m.Version)
                    .ToList();

                foreach (var migration in migrationsToApply)
                {
                    var stepResult = await ApplyMigrationAsync(
                        connection, schemaNames.SystemSchema, migration, cancellationToken);

                    steps.Add(stepResult);

                    if (!stepResult.Success)
                    {
                        LogMigrationFailed(_logger, projectId, migration.Version, stepResult.ErrorMessage);

                        return new MigrationResult
                        {
                            Success = false,
                            FromVersion = fromVersion,
                            ToVersion = targetVersion,
                            Steps = steps,
                            TotalDurationMs = sw.ElapsedMilliseconds,
                            ErrorMessage = stepResult.ErrorMessage,
                            FailedAtVersion = migration.Version
                        };
                    }
                }
            }
            else
            {
                // Rollback to target version
                var result = await RollbackToVersionInternalAsync(
                    connection, schemaNames.SystemSchema, projectId, targetVersion, steps, cancellationToken);

                if (!result.success)
                {
                    return new MigrationResult
                    {
                        Success = false,
                        FromVersion = fromVersion,
                        ToVersion = targetVersion,
                        Steps = steps,
                        TotalDurationMs = sw.ElapsedMilliseconds,
                        ErrorMessage = result.errorMessage,
                        FailedAtVersion = result.failedVersion
                    };
                }
            }

            sw.Stop();

            LogMigrationCompleted(_logger, projectId, fromVersion, targetVersion, sw.ElapsedMilliseconds);

            return new MigrationResult
            {
                Success = true,
                FromVersion = fromVersion,
                ToVersion = targetVersion,
                Steps = steps,
                TotalDurationMs = sw.ElapsedMilliseconds
            };
        }
        finally
        {
            await ReleaseMigrationLockAsync(connection, projectId, cancellationToken);
        }
    }

    /// <inheritdoc/>
    public async Task<MigrationResult> RollbackAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var currentVersion = await GetCurrentVersionAsync(projectId, cancellationToken);

        if (currentVersion <= 0)
        {
            return new MigrationResult
            {
                Success = true,
                FromVersion = 0,
                ToVersion = 0,
                Steps = [],
                TotalDurationMs = 0
            };
        }

        return await RollbackToVersionAsync(projectId, currentVersion - 1, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<MigrationResult> RollbackToVersionAsync(
        Guid projectId,
        int targetVersion,
        CancellationToken cancellationToken = default)
    {
        var schemaNames = _schemaNameResolver.GetSchemaNames(projectId);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var steps = new List<MigrationStepResult>();

        LogRollbackStarted(_logger, projectId, targetVersion);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        // Acquire advisory lock
        if (!await TryAcquireMigrationLockAsync(connection, projectId, cancellationToken))
        {
            return new MigrationResult
            {
                Success = false,
                FromVersion = 0,
                ToVersion = targetVersion,
                Steps = steps,
                TotalDurationMs = sw.ElapsedMilliseconds,
                ErrorMessage = "Could not acquire migration lock. Another migration may be in progress."
            };
        }

        try
        {
            var currentVersion = await GetCurrentVersionAsync(projectId, cancellationToken);
            var fromVersion = currentVersion;

            if (targetVersion >= currentVersion)
            {
                return new MigrationResult
                {
                    Success = true,
                    FromVersion = fromVersion,
                    ToVersion = fromVersion,
                    Steps = steps,
                    TotalDurationMs = sw.ElapsedMilliseconds
                };
            }

            var result = await RollbackToVersionInternalAsync(
                connection, schemaNames.SystemSchema, projectId, targetVersion, steps, cancellationToken);

            sw.Stop();

            if (result.success)
            {
                LogRollbackCompleted(_logger, projectId, fromVersion, targetVersion, sw.ElapsedMilliseconds);
            }

            return new MigrationResult
            {
                Success = result.success,
                FromVersion = fromVersion,
                ToVersion = targetVersion,
                Steps = steps,
                TotalDurationMs = sw.ElapsedMilliseconds,
                ErrorMessage = result.errorMessage,
                FailedAtVersion = result.failedVersion
            };
        }
        finally
        {
            await ReleaseMigrationLockAsync(connection, projectId, cancellationToken);
        }
    }

    /// <inheritdoc/>
    public async Task<MigrationValidationResult> ValidateMigrationAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var issues = new List<MigrationValidationIssue>();
        var currentVersion = await GetCurrentVersionAsync(projectId, cancellationToken);
        var pendingMigrations = await GetPendingMigrationsAsync(projectId, cancellationToken);
        var appliedMigrations = await GetAppliedMigrationsAsync(projectId, cancellationToken);

        // Validate migration sequence
        var expectedVersion = 0;
        foreach (var migration in _registeredMigrations.OrderBy(m => m.Version))
        {
            expectedVersion++;
            if (migration.Version != expectedVersion)
            {
                issues.Add(new MigrationValidationIssue
                {
                    Code = "GAP_IN_VERSIONS",
                    Message = $"Expected version {expectedVersion} but found {migration.Version}",
                    Severity = MigrationIssueSeverity.Error,
                    AffectedVersion = migration.Version
                });
            }
        }

        // Validate checksums of applied migrations
        foreach (var appliedMigration in appliedMigrations.Where(m => !m.IsRolledBack))
        {
            var registeredMigration = _registeredMigrations.FirstOrDefault(m => m.Version == appliedMigration.Version);

            if (registeredMigration is null)
            {
                issues.Add(new MigrationValidationIssue
                {
                    Code = "MISSING_MIGRATION_DEFINITION",
                    Message = $"Applied migration {appliedMigration.Version} has no registered definition",
                    Severity = MigrationIssueSeverity.Warning,
                    AffectedVersion = appliedMigration.Version
                });
            }
            else if (appliedMigration.Checksum is not null)
            {
                var expectedChecksum = ComputeChecksum(registeredMigration.UpScript);
                if (expectedChecksum != appliedMigration.Checksum)
                {
                    issues.Add(new MigrationValidationIssue
                    {
                        Code = "CHECKSUM_MISMATCH",
                        Message = $"Migration {appliedMigration.Version} checksum does not match registered script",
                        Severity = MigrationIssueSeverity.Warning,
                        AffectedVersion = appliedMigration.Version
                    });
                }
            }
        }

        // Check for irreversible pending migrations
        foreach (var pending in pendingMigrations.Where(m => !m.IsReversible))
        {
            issues.Add(new MigrationValidationIssue
            {
                Code = "IRREVERSIBLE_MIGRATION",
                Message = $"Migration {pending.Version} ({pending.Name}) has no rollback script",
                Severity = MigrationIssueSeverity.Info,
                AffectedVersion = pending.Version
            });
        }

        var targetVersion = _registeredMigrations.Count > 0
            ? _registeredMigrations.Max(m => m.Version)
            : currentVersion;

        return new MigrationValidationResult
        {
            IsValid = !issues.Any(i => i.Severity == MigrationIssueSeverity.Error),
            CurrentVersion = currentVersion,
            TargetVersion = targetVersion,
            PendingMigrationCount = pendingMigrations.Count,
            Issues = issues
        };
    }

    private async Task<MigrationStepResult> ApplyMigrationAsync(
        NpgsqlConnection connection,
        string systemSchema,
        MigrationDefinition migration,
        CancellationToken cancellationToken)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            try
            {
                // Execute the up script
                await connection.ExecuteAsync(migration.UpScript, transaction: transaction);

                // Record the migration
                var checksum = ComputeChecksum(migration.UpScript);
                var insertSql = $"""
                    INSERT INTO {QuoteIdentifier(systemSchema)}."_migrations"
                        ("version", "name", "checksum", "duration_ms")
                    VALUES (@version, @name, @checksum, @durationMs)
                    """;

                sw.Stop();

                await connection.ExecuteAsync(insertSql, new
                {
                    version = migration.Version,
                    name = migration.Name,
                    checksum,
                    durationMs = sw.ElapsedMilliseconds
                }, transaction);

                await transaction.CommitAsync(cancellationToken);

                LogMigrationApplied(_logger, migration.Version, migration.Name, sw.ElapsedMilliseconds);

                return new MigrationStepResult
                {
                    Version = migration.Version,
                    Name = migration.Name,
                    Success = true,
                    DurationMs = sw.ElapsedMilliseconds
                };
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }
        catch (Exception ex)
        {
            sw.Stop();

            return new MigrationStepResult
            {
                Version = migration.Version,
                Name = migration.Name,
                Success = false,
                DurationMs = sw.ElapsedMilliseconds,
                ErrorMessage = ex.Message
            };
        }
    }

    private async Task<(bool success, string? errorMessage, int? failedVersion)> RollbackToVersionInternalAsync(
        NpgsqlConnection connection,
        string systemSchema,
        Guid projectId,
        int targetVersion,
        List<MigrationStepResult> steps,
        CancellationToken cancellationToken)
    {
        var appliedMigrations = await GetAppliedMigrationsAsync(projectId, cancellationToken);

        var migrationsToRollback = appliedMigrations
            .Where(m => !m.IsRolledBack && m.Version > targetVersion)
            .OrderByDescending(m => m.Version)
            .ToList();

        foreach (var appliedMigration in migrationsToRollback)
        {
            var registeredMigration = _registeredMigrations.FirstOrDefault(m => m.Version == appliedMigration.Version);

            if (registeredMigration?.DownScript is null)
            {
                var errorMessage = $"Migration {appliedMigration.Version} has no rollback script";

                steps.Add(new MigrationStepResult
                {
                    Version = appliedMigration.Version,
                    Name = appliedMigration.Name,
                    Success = false,
                    DurationMs = 0,
                    ErrorMessage = errorMessage
                });

                LogRollbackFailed(_logger, projectId, appliedMigration.Version, errorMessage);

                return (false, errorMessage, appliedMigration.Version);
            }

            var stepResult = await RollbackMigrationAsync(
                connection, systemSchema, appliedMigration, registeredMigration, cancellationToken);

            steps.Add(stepResult);

            if (!stepResult.Success)
            {
                LogRollbackFailed(_logger, projectId, appliedMigration.Version, stepResult.ErrorMessage);
                return (false, stepResult.ErrorMessage, appliedMigration.Version);
            }
        }

        return (true, null, null);
    }

    private async Task<MigrationStepResult> RollbackMigrationAsync(
        NpgsqlConnection connection,
        string systemSchema,
        MigrationRecord appliedMigration,
        MigrationDefinition registeredMigration,
        CancellationToken cancellationToken)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            try
            {
                // Execute the down script
                await connection.ExecuteAsync(registeredMigration.DownScript!, transaction: transaction);

                // Mark the migration as rolled back
                var updateSql = $"""
                    UPDATE {QuoteIdentifier(systemSchema)}."_migrations"
                    SET "is_rolled_back" = true,
                        "rolled_back_at" = NOW()
                    WHERE "version" = @version
                    """;

                await connection.ExecuteAsync(updateSql, new { version = appliedMigration.Version }, transaction);

                await transaction.CommitAsync(cancellationToken);

                sw.Stop();

                LogMigrationRolledBack(_logger, appliedMigration.Version, appliedMigration.Name, sw.ElapsedMilliseconds);

                return new MigrationStepResult
                {
                    Version = appliedMigration.Version,
                    Name = appliedMigration.Name,
                    Success = true,
                    DurationMs = sw.ElapsedMilliseconds
                };
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }
        catch (Exception ex)
        {
            sw.Stop();

            return new MigrationStepResult
            {
                Version = appliedMigration.Version,
                Name = appliedMigration.Name,
                Success = false,
                DurationMs = sw.ElapsedMilliseconds,
                ErrorMessage = ex.Message
            };
        }
    }

    private static async Task<bool> TryAcquireMigrationLockAsync(
        NpgsqlConnection connection,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        // Use a combination of migration lock ID and project ID for project-specific locking
        var lockKey = projectId.GetHashCode();

        var acquired = await connection.ExecuteScalarAsync<bool>(
            "SELECT pg_try_advisory_lock(@lockId, @lockKey)",
            new { lockId = MigrationLockId, lockKey });

        return acquired;
    }

    private static async Task ReleaseMigrationLockAsync(
        NpgsqlConnection connection,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var lockKey = projectId.GetHashCode();

        await connection.ExecuteAsync(
            "SELECT pg_advisory_unlock(@lockId, @lockKey)",
            new { lockId = MigrationLockId, lockKey });
    }

    private static string ComputeChecksum(string script)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(script));
        return Convert.ToHexString(bytes)[..16]; // First 16 chars of SHA256
    }

    private static string QuoteIdentifier(string identifier)
    {
        return $"\"{identifier.Replace("\"", "\"\"")}\"";
    }

    // DTOs for Dapper mapping
    private sealed class MigrationRecordDto
    {
        public Guid Id { get; init; }
        public int Version { get; init; }
        public string Name { get; init; } = default!;
        public DateTimeOffset AppliedAt { get; init; }
        public long DurationMs { get; init; }
        public string? Checksum { get; init; }
        public bool IsRolledBack { get; init; }
        public DateTimeOffset? RolledBackAt { get; init; }
    }

    // LoggerMessage delegates for high-performance logging
    [LoggerMessage(LogLevel.Debug, "Registered migration {Version}: {Name}")]
    private static partial void LogMigrationRegistered(ILogger logger, int version, string name);

    [LoggerMessage(LogLevel.Information, "Starting migration for project {ProjectId} to version {TargetVersion}")]
    private static partial void LogMigrationStarted(ILogger logger, Guid projectId, int targetVersion);

    [LoggerMessage(LogLevel.Information, "Migration completed for project {ProjectId}: {FromVersion} → {ToVersion} in {DurationMs}ms")]
    private static partial void LogMigrationCompleted(ILogger logger, Guid projectId, int fromVersion, int toVersion, long durationMs);

    [LoggerMessage(LogLevel.Error, "Migration failed for project {ProjectId} at version {Version}: {ErrorMessage}")]
    private static partial void LogMigrationFailed(ILogger logger, Guid projectId, int version, string? errorMessage);

    [LoggerMessage(LogLevel.Debug, "Applied migration {Version} ({Name}) in {DurationMs}ms")]
    private static partial void LogMigrationApplied(ILogger logger, int version, string name, long durationMs);

    [LoggerMessage(LogLevel.Information, "Starting rollback for project {ProjectId} to version {TargetVersion}")]
    private static partial void LogRollbackStarted(ILogger logger, Guid projectId, int targetVersion);

    [LoggerMessage(LogLevel.Information, "Rollback completed for project {ProjectId}: {FromVersion} → {ToVersion} in {DurationMs}ms")]
    private static partial void LogRollbackCompleted(ILogger logger, Guid projectId, int fromVersion, int toVersion, long durationMs);

    [LoggerMessage(LogLevel.Error, "Rollback failed for project {ProjectId} at version {Version}: {ErrorMessage}")]
    private static partial void LogRollbackFailed(ILogger logger, Guid projectId, int version, string? errorMessage);

    [LoggerMessage(LogLevel.Debug, "Rolled back migration {Version} ({Name}) in {DurationMs}ms")]
    private static partial void LogMigrationRolledBack(ILogger logger, int version, string name, long durationMs);
}

/// <summary>
/// Definition of a database migration.
/// </summary>
public sealed class MigrationDefinition
{
    /// <summary>
    /// Version number of the migration (must be sequential starting from 1).
    /// </summary>
    public int Version { get; init; }

    /// <summary>
    /// Name/description of the migration.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Optional detailed description.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// SQL script to apply the migration.
    /// </summary>
    public required string UpScript { get; init; }

    /// <summary>
    /// SQL script to reverse the migration. If null, migration cannot be rolled back.
    /// </summary>
    public string? DownScript { get; init; }

    /// <summary>
    /// Timestamp when this migration was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
