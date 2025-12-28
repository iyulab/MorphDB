using System.Text.Json;
using Dapper;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Models;
using Npgsql;

namespace MorphDB.Npgsql.Backup;

/// <summary>
/// PostgreSQL repository for backup metadata.
/// </summary>
public sealed class BackupRepository : IBackupRepository
{
    private readonly NpgsqlDataSource _dataSource;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public BackupRepository(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    /// <inheritdoc/>
    public async Task<Core.Models.Backup> CreateAsync(
        Core.Models.Backup backup,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            INSERT INTO morphdb._morph_backups (
                backup_id, project_id, name, description, backup_type, status,
                size_bytes, storage_path, storage_type, compression, checksum,
                error_message, initiated_by, metadata, started_at, completed_at, expires_at
            )
            VALUES (
                @BackupId, @ProjectId, @Name, @Description, @BackupType, @Status,
                @SizeBytes, @StoragePath, @StorageType, @Compression, @Checksum,
                @ErrorMessage, @InitiatedBy, @Metadata::jsonb, @StartedAt, @CompletedAt, @ExpiresAt
            )
            RETURNING *
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        var entity = await connection.QuerySingleAsync<BackupEntity>(sql, new
        {
            backup.BackupId,
            backup.ProjectId,
            backup.Name,
            backup.Description,
            BackupType = (int)backup.Type,
            Status = (int)backup.Status,
            backup.SizeBytes,
            backup.StoragePath,
            StorageType = (int)backup.StorageType,
            Compression = (int)backup.Compression,
            backup.Checksum,
            backup.ErrorMessage,
            backup.InitiatedBy,
            Metadata = backup.Metadata is not null ? JsonSerializer.Serialize(backup.Metadata, JsonOptions) : null,
            backup.StartedAt,
            backup.CompletedAt,
            backup.ExpiresAt
        });

        return MapToBackup(entity);
    }

    /// <inheritdoc/>
    public async Task<Core.Models.Backup?> GetByIdAsync(
        Guid backupId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT * FROM morphdb._morph_backups
            WHERE backup_id = @BackupId
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        var entity = await connection.QuerySingleOrDefaultAsync<BackupEntity>(sql, new { BackupId = backupId });

        return entity is null ? null : MapToBackup(entity);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Core.Models.Backup>> ListByProjectAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT * FROM morphdb._morph_backups
            WHERE project_id = @ProjectId
            ORDER BY started_at DESC
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        var entities = await connection.QueryAsync<BackupEntity>(sql, new { ProjectId = projectId });

        return entities.Select(MapToBackup).ToList();
    }

    /// <inheritdoc/>
    public async Task<Core.Models.Backup> UpdateAsync(
        Core.Models.Backup backup,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE morphdb._morph_backups
            SET name = @Name,
                description = @Description,
                backup_type = @BackupType,
                status = @Status,
                size_bytes = @SizeBytes,
                storage_path = @StoragePath,
                storage_type = @StorageType,
                compression = @Compression,
                checksum = @Checksum,
                error_message = @ErrorMessage,
                metadata = @Metadata::jsonb,
                completed_at = @CompletedAt,
                expires_at = @ExpiresAt
            WHERE backup_id = @BackupId
            RETURNING *
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        var entity = await connection.QuerySingleAsync<BackupEntity>(sql, new
        {
            backup.BackupId,
            backup.Name,
            backup.Description,
            BackupType = (int)backup.Type,
            Status = (int)backup.Status,
            backup.SizeBytes,
            backup.StoragePath,
            StorageType = (int)backup.StorageType,
            Compression = (int)backup.Compression,
            backup.Checksum,
            backup.ErrorMessage,
            Metadata = backup.Metadata is not null ? JsonSerializer.Serialize(backup.Metadata, JsonOptions) : null,
            backup.CompletedAt,
            backup.ExpiresAt
        });

        return MapToBackup(entity);
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteAsync(
        Guid backupId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            DELETE FROM morphdb._morph_backups
            WHERE backup_id = @BackupId
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        var rowsAffected = await connection.ExecuteAsync(sql, new { BackupId = backupId });

        return rowsAffected > 0;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Core.Models.Backup>> ListExpiredAsync(
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT * FROM morphdb._morph_backups
            WHERE expires_at IS NOT NULL
              AND expires_at < NOW()
              AND status NOT IN (@ExpiredStatus)
            ORDER BY expires_at ASC
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        var entities = await connection.QueryAsync<BackupEntity>(sql, new
        {
            ExpiredStatus = (int)BackupStatus.Expired
        });

        return entities.Select(MapToBackup).ToList();
    }

    private static Core.Models.Backup MapToBackup(BackupEntity entity)
    {
        return new Core.Models.Backup
        {
            BackupId = entity.backup_id,
            ProjectId = entity.project_id,
            Name = entity.name,
            Description = entity.description,
            Type = (BackupType)entity.backup_type,
            Status = (BackupStatus)entity.status,
            SizeBytes = entity.size_bytes,
            StoragePath = entity.storage_path,
            StorageType = (BackupStorageType)entity.storage_type,
            Compression = (BackupCompression)entity.compression,
            Checksum = entity.checksum,
            ErrorMessage = entity.error_message,
            InitiatedBy = entity.initiated_by,
            Metadata = entity.metadata is not null
                ? JsonSerializer.Deserialize<BackupMetadata>(entity.metadata, JsonOptions)
                : null,
            StartedAt = entity.started_at,
            CompletedAt = entity.completed_at,
            ExpiresAt = entity.expires_at
        };
    }

    private sealed class BackupEntity
    {
        public Guid backup_id { get; init; }
        public Guid project_id { get; init; }
        public string name { get; init; } = "";
        public string? description { get; init; }
        public int backup_type { get; init; }
        public int status { get; init; }
        public long size_bytes { get; init; }
        public string? storage_path { get; init; }
        public int storage_type { get; init; }
        public int compression { get; init; }
        public string? checksum { get; init; }
        public string? error_message { get; init; }
        public string? initiated_by { get; init; }
        public string? metadata { get; init; }
        public DateTimeOffset started_at { get; init; }
        public DateTimeOffset? completed_at { get; init; }
        public DateTimeOffset? expires_at { get; init; }
    }
}
