using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MorphDB.Core.Encryption;
using MorphDB.Npgsql.Repositories;
using Npgsql;

namespace MorphDB.Npgsql.Encryption;

/// <summary>
/// Service for managing encryption key rotation operations.
/// Handles re-encryption of data when key versions change.
/// </summary>
public sealed partial class KeyRotationService : IKeyRotationService
{
    private const string EncryptedPrefix = "$MORPH$v1$";

    private readonly NpgsqlDataSource _dataSource;
    private readonly IMetadataRepository _metadataRepository;
    private readonly IKeyDerivationService _keyDerivation;
    private readonly IDataEncryptionService _encryptionService;
    private readonly DataEncryptionOptions _options;
    private readonly ILogger<KeyRotationService> _logger;

    private readonly ConcurrentDictionary<string, KeyRotationStatus> _rotationStatus = new();

    public KeyRotationService(
        NpgsqlDataSource dataSource,
        IMetadataRepository metadataRepository,
        IKeyDerivationService keyDerivation,
        IDataEncryptionService encryptionService,
        IOptions<DataEncryptionOptions> options,
        ILogger<KeyRotationService> logger)
    {
        _dataSource = dataSource;
        _metadataRepository = metadataRepository;
        _keyDerivation = keyDerivation;
        _encryptionService = encryptionService;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public int CurrentKeyVersion => _keyDerivation.CurrentKeyVersion;

    /// <inheritdoc />
    public IReadOnlyList<int> AvailableKeyVersions => [CurrentKeyVersion];

    /// <inheritdoc />
    public async Task<KeyRotationResult> RotateTableKeyAsync(
        Guid projectId,
        string tableName,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var startedAt = DateTimeOffset.UtcNow;
        var statusKey = $"{projectId}:{tableName}";

        try
        {
            LogKeyRotationStarted(tableName, projectId);

            // Get table metadata
            var table = await _metadataRepository.GetTableByNameAsync(projectId, tableName, true, cancellationToken);
            if (table is null)
            {
                throw new MorphDB.Core.Exceptions.TableNotFoundException(tableName);
            }

            // Get encrypted columns
            var encryptedColumns = table.Columns
                .Where(c => c.IsEncrypted)
                .Select(c => new { c.LogicalName, c.PhysicalName })
                .ToList();

            if (encryptedColumns.Count == 0)
            {
                return new KeyRotationResult
                {
                    Success = true,
                    TableName = tableName,
                    PreviousKeyVersion = CurrentKeyVersion,
                    NewKeyVersion = CurrentKeyVersion,
                    RowsProcessed = 0,
                    ColumnsRotated = 0,
                    Duration = stopwatch.Elapsed,
                    StartedAt = startedAt,
                    CompletedAt = DateTimeOffset.UtcNow
                };
            }

            // Update status
            _rotationStatus[statusKey] = new KeyRotationStatus
            {
                State = KeyRotationState.InProgress,
                TableName = tableName,
                CurrentKeyVersion = CurrentKeyVersion,
                TargetKeyVersion = CurrentKeyVersion,
                StartedAt = startedAt
            };

            long rowsProcessed = 0;
            const int batchSize = 1000;

            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

            // Count total rows
            var countSql = $"SELECT COUNT(*) FROM \"{table.PhysicalName}\" WHERE project_id = @projectId";
            await using var countCmd = new NpgsqlCommand(countSql, connection);
            countCmd.Parameters.AddWithValue("projectId", projectId);
            var totalRows = (long)(await countCmd.ExecuteScalarAsync(cancellationToken))!;

            // Process in batches using cursor
            var columnList = string.Join(", ", encryptedColumns.Select(c => $"\"{c.PhysicalName}\""));
            var selectSql = $@"
                SELECT id, {columnList}
                FROM ""{table.PhysicalName}""
                WHERE project_id = @projectId
                ORDER BY id";

            await using var selectCmd = new NpgsqlCommand(selectSql, connection);
            selectCmd.Parameters.AddWithValue("projectId", projectId);

            await using var reader = await selectCmd.ExecuteReaderAsync(cancellationToken);

            var updates = new List<(Guid id, Dictionary<string, string> values)>();

            while (await reader.ReadAsync(cancellationToken))
            {
                var id = reader.GetGuid(0);
                var updatedValues = new Dictionary<string, string>();

                for (var i = 0; i < encryptedColumns.Count; i++)
                {
                    var column = encryptedColumns[i];
                    var value = reader.IsDBNull(i + 1) ? null : reader.GetString(i + 1);

                    if (value is not null && value.StartsWith(EncryptedPrefix, StringComparison.Ordinal))
                    {
                        // Decrypt with current key and re-encrypt
                        var decrypted = _encryptionService.Decrypt(projectId, tableName, column.LogicalName, value);
                        var reEncrypted = _encryptionService.Encrypt(projectId, tableName, column.LogicalName, decrypted);
                        updatedValues[column.PhysicalName] = reEncrypted;
                    }
                }

                if (updatedValues.Count > 0)
                {
                    updates.Add((id, updatedValues));
                }

                if (updates.Count >= batchSize)
                {
                    await ApplyBatchUpdatesAsync(connection, table.PhysicalName, updates, cancellationToken);
                    rowsProcessed += updates.Count;
                    updates.Clear();

                    // Update progress
                    _rotationStatus[statusKey] = _rotationStatus[statusKey] with
                    {
                        RowsProcessed = rowsProcessed,
                        TotalRows = totalRows,
                        ProgressPercent = totalRows > 0 ? (double)rowsProcessed / totalRows * 100 : 0
                    };
                }
            }

            // Process remaining
            if (updates.Count > 0)
            {
                await ApplyBatchUpdatesAsync(connection, table.PhysicalName, updates, cancellationToken);
                rowsProcessed += updates.Count;
            }

            stopwatch.Stop();

            var result = new KeyRotationResult
            {
                Success = true,
                TableName = tableName,
                PreviousKeyVersion = CurrentKeyVersion,
                NewKeyVersion = CurrentKeyVersion,
                RowsProcessed = rowsProcessed,
                ColumnsRotated = encryptedColumns.Count,
                Duration = stopwatch.Elapsed,
                StartedAt = startedAt,
                CompletedAt = DateTimeOffset.UtcNow
            };

            _rotationStatus[statusKey] = new KeyRotationStatus
            {
                State = KeyRotationState.Completed,
                TableName = tableName,
                CurrentKeyVersion = CurrentKeyVersion,
                RowsProcessed = rowsProcessed,
                TotalRows = totalRows,
                ProgressPercent = 100,
                LastRotatedAt = DateTimeOffset.UtcNow
            };

            LogKeyRotationCompleted(tableName, projectId, rowsProcessed, stopwatch.Elapsed);

            return result;
        }
        catch (Exception ex)
        {
            LogKeyRotationFailed(tableName, projectId, ex);

            _rotationStatus[statusKey] = new KeyRotationStatus
            {
                State = KeyRotationState.Failed,
                TableName = tableName,
                CurrentKeyVersion = CurrentKeyVersion
            };

            return new KeyRotationResult
            {
                Success = false,
                TableName = tableName,
                PreviousKeyVersion = CurrentKeyVersion,
                NewKeyVersion = CurrentKeyVersion,
                RowsProcessed = 0,
                ColumnsRotated = 0,
                Duration = stopwatch.Elapsed,
                ErrorMessage = ex.Message,
                StartedAt = startedAt,
                CompletedAt = DateTimeOffset.UtcNow
            };
        }
    }

    /// <inheritdoc />
    public async Task<KeyRotationResult> RotateProjectKeysAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var startedAt = DateTimeOffset.UtcNow;
        long totalRowsProcessed = 0;
        var totalColumnsRotated = 0;
        var tablesProcessed = new List<string>();

        try
        {
            var tables = await _metadataRepository.ListTablesAsync(projectId, true, cancellationToken);

            foreach (var table in tables)
            {
                var result = await RotateTableKeyAsync(projectId, table.LogicalName, cancellationToken);
                if (result.Success)
                {
                    totalRowsProcessed += result.RowsProcessed;
                    totalColumnsRotated += result.ColumnsRotated;
                    tablesProcessed.Add(table.LogicalName);
                }
                else
                {
                    // Return partial failure
                    return new KeyRotationResult
                    {
                        Success = false,
                        TableName = $"project:{projectId} (failed at {table.LogicalName})",
                        PreviousKeyVersion = CurrentKeyVersion,
                        NewKeyVersion = CurrentKeyVersion,
                        RowsProcessed = totalRowsProcessed,
                        ColumnsRotated = totalColumnsRotated,
                        Duration = stopwatch.Elapsed,
                        ErrorMessage = result.ErrorMessage,
                        StartedAt = startedAt,
                        CompletedAt = DateTimeOffset.UtcNow
                    };
                }
            }

            return new KeyRotationResult
            {
                Success = true,
                TableName = $"project:{projectId} ({tablesProcessed.Count} tables)",
                PreviousKeyVersion = CurrentKeyVersion,
                NewKeyVersion = CurrentKeyVersion,
                RowsProcessed = totalRowsProcessed,
                ColumnsRotated = totalColumnsRotated,
                Duration = stopwatch.Elapsed,
                StartedAt = startedAt,
                CompletedAt = DateTimeOffset.UtcNow
            };
        }
        catch (Exception ex)
        {
            return new KeyRotationResult
            {
                Success = false,
                TableName = $"project:{projectId}",
                PreviousKeyVersion = CurrentKeyVersion,
                NewKeyVersion = CurrentKeyVersion,
                RowsProcessed = totalRowsProcessed,
                ColumnsRotated = totalColumnsRotated,
                Duration = stopwatch.Elapsed,
                ErrorMessage = ex.Message,
                StartedAt = startedAt,
                CompletedAt = DateTimeOffset.UtcNow
            };
        }
    }

    /// <inheritdoc />
    public Task<KeyRotationStatus> GetRotationStatusAsync(
        Guid projectId,
        string tableName,
        CancellationToken cancellationToken = default)
    {
        var statusKey = $"{projectId}:{tableName}";

        if (_rotationStatus.TryGetValue(statusKey, out var status))
        {
            return Task.FromResult(status);
        }

        return Task.FromResult(new KeyRotationStatus
        {
            State = KeyRotationState.Idle,
            TableName = tableName,
            CurrentKeyVersion = CurrentKeyVersion
        });
    }

    /// <inheritdoc />
    public async Task<KeyValidationResult> ValidateEncryptionAsync(
        Guid projectId,
        string tableName,
        CancellationToken cancellationToken = default)
    {
        var table = await _metadataRepository.GetTableByNameAsync(projectId, tableName, true, cancellationToken);
        if (table is null)
        {
            throw new MorphDB.Core.Exceptions.TableNotFoundException(tableName);
        }

        var encryptedColumns = table.Columns
            .Where(c => c.IsEncrypted)
            .Select(c => c.PhysicalName)
            .ToList();

        if (encryptedColumns.Count == 0)
        {
            return new KeyValidationResult
            {
                IsValid = true,
                TableName = tableName,
                ExpectedKeyVersion = CurrentKeyVersion,
                TotalEncryptedValues = 0,
                CurrentVersionCount = 0,
                OldVersionCount = 0,
                UnencryptedCount = 0,
                VersionBreakdown = new Dictionary<int, long>()
            };
        }

        var versionCounts = new Dictionary<int, long>();
        long unencryptedCount = 0;
        long totalCount = 0;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        foreach (var column in encryptedColumns)
        {
            var sql = $@"SELECT ""{column}"" FROM ""{table.PhysicalName}"" WHERE project_id = @projectId AND ""{column}"" IS NOT NULL";
            await using var cmd = new NpgsqlCommand(sql, connection);
            cmd.Parameters.AddWithValue("projectId", projectId);

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var value = reader.GetString(0);
                totalCount++;

                if (value.StartsWith(EncryptedPrefix, StringComparison.Ordinal))
                {
                    // Extract key version from encrypted value
                    var base64Part = value[EncryptedPrefix.Length..];
                    var bytes = Convert.FromBase64String(base64Part);
                    if (bytes.Length >= 3)
                    {
                        var keyVersion = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(1, 2));
                        versionCounts[keyVersion] = versionCounts.GetValueOrDefault(keyVersion) + 1;
                    }
                }
                else
                {
                    unencryptedCount++;
                }
            }
        }

        var currentVersionCount = versionCounts.GetValueOrDefault(CurrentKeyVersion);
        var oldVersionCount = versionCounts.Where(kv => kv.Key != CurrentKeyVersion).Sum(kv => kv.Value);

        return new KeyValidationResult
        {
            IsValid = oldVersionCount == 0 && unencryptedCount == 0,
            TableName = tableName,
            ExpectedKeyVersion = CurrentKeyVersion,
            TotalEncryptedValues = totalCount,
            CurrentVersionCount = currentVersionCount,
            OldVersionCount = oldVersionCount,
            UnencryptedCount = unencryptedCount,
            VersionBreakdown = versionCounts
        };
    }

    private static async Task ApplyBatchUpdatesAsync(
        NpgsqlConnection connection,
        string physicalTableName,
        List<(Guid id, Dictionary<string, string> values)> updates,
        CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            foreach (var (id, values) in updates)
            {
                var setClauses = string.Join(", ", values.Select(kv => $"\"{kv.Key}\" = @{kv.Key}"));
                var sql = $"UPDATE \"{physicalTableName}\" SET {setClauses} WHERE id = @id";

                await using var cmd = new NpgsqlCommand(sql, connection, transaction);
                cmd.Parameters.AddWithValue("id", id);
                foreach (var kv in values)
                {
                    cmd.Parameters.AddWithValue(kv.Key, kv.Value);
                }

                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting key rotation for table {TableName} (project: {ProjectId})")]
    private partial void LogKeyRotationStarted(string tableName, Guid projectId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Key rotation completed for table {TableName} (project: {ProjectId}): {RowsProcessed} rows in {Duration}")]
    private partial void LogKeyRotationCompleted(string tableName, Guid projectId, long rowsProcessed, TimeSpan duration);

    [LoggerMessage(Level = LogLevel.Error, Message = "Key rotation failed for table {TableName} (project: {ProjectId})")]
    private partial void LogKeyRotationFailed(string tableName, Guid projectId, Exception ex);
}
