using System.Security.Cryptography;
using Dapper;
using MorphDB.Core.Security;
using Npgsql;

namespace MorphDB.Npgsql.Security;

/// <summary>
/// PostgreSQL implementation of API key service.
/// </summary>
public sealed class ApiKeyService : IApiKeyService
{
    private readonly NpgsqlDataSource _dataSource;
    private const int KeyLength = 32;
    private const string KeyPrefix = "morphdb_";

    public ApiKeyService(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<(ApiKey Key, string RawKey)> CreateKeyAsync(
        Guid projectId,
        ApiKeyType keyType,
        string name,
        string? description = null,
        DateTimeOffset? expiresAt = null,
        CancellationToken cancellationToken = default)
    {
        // Generate a secure random key
        var rawKeyBytes = RandomNumberGenerator.GetBytes(KeyLength);
        var rawKeyBase64 = Convert.ToBase64String(rawKeyBytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

        var typePrefix = keyType == ApiKeyType.Anon ? "anon_" : "svc_";
        var rawKey = $"{KeyPrefix}{typePrefix}{rawKeyBase64}";
        var keyPrefixPart = rawKey[..Math.Min(16, rawKey.Length)];
        var keyHash = BCrypt.Net.BCrypt.HashPassword(rawKey);

        var apiKey = new ApiKey
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            KeyType = keyType,
            KeyHash = keyHash,
            KeyPrefix = keyPrefixPart,
            Name = name,
            Description = description,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = expiresAt
        };

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(
            """
            INSERT INTO morphdb._morph_api_keys (id, project_id, key_type, key_hash, key_prefix, name, description, is_active, created_at, expires_at)
            VALUES (@Id, @ProjectId, @KeyType, @KeyHash, @KeyPrefix, @Name, @Description, @IsActive, @CreatedAt, @ExpiresAt)
            """,
            new
            {
                apiKey.Id,
                apiKey.ProjectId,
                KeyType = (int)apiKey.KeyType,
                apiKey.KeyHash,
                apiKey.KeyPrefix,
                apiKey.Name,
                apiKey.Description,
                apiKey.IsActive,
                apiKey.CreatedAt,
                apiKey.ExpiresAt
            });

        return (apiKey, rawKey);
    }

    public async Task<ApiKeyValidationResult> ValidateKeyAsync(
        string rawKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawKey))
        {
            return ApiKeyValidationResult.Failure("API key is required");
        }

        if (!rawKey.StartsWith(KeyPrefix, StringComparison.Ordinal))
        {
            return ApiKeyValidationResult.Failure("Invalid API key format");
        }

        var keyPrefix = rawKey[..Math.Min(16, rawKey.Length)];

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var keys = await connection.QueryAsync<ApiKeyRecord>(
            """
            SELECT id AS "Id", project_id AS "ProjectId", key_type AS "KeyType", key_hash AS "KeyHash",
                   key_prefix AS "KeyPrefix", name AS "Name", description AS "Description",
                   is_active AS "IsActive", created_at AS "CreatedAt", expires_at AS "ExpiresAt",
                   last_used_at AS "LastUsedAt"
            FROM morphdb._morph_api_keys
            WHERE key_prefix = @KeyPrefix AND is_active = true
            """,
            new { KeyPrefix = keyPrefix });

        foreach (var record in keys)
        {
            if (BCrypt.Net.BCrypt.Verify(rawKey, record.KeyHash))
            {
                if (record.ExpiresAt.HasValue && record.ExpiresAt.Value < DateTimeOffset.UtcNow)
                {
                    return ApiKeyValidationResult.Failure("API key has expired");
                }

                var apiKey = MapToApiKey(record);

                // Update last used timestamp (fire and forget)
                _ = UpdateLastUsedAsync(apiKey.Id, cancellationToken);

                return ApiKeyValidationResult.Success(apiKey);
            }
        }

        return ApiKeyValidationResult.Failure("Invalid API key");
    }

    public async Task<IReadOnlyList<ApiKey>> GetKeysAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var records = await connection.QueryAsync<ApiKeyRecord>(
            """
            SELECT id AS "Id", project_id AS "ProjectId", key_type AS "KeyType", key_hash AS "KeyHash",
                   key_prefix AS "KeyPrefix", name AS "Name", description AS "Description",
                   is_active AS "IsActive", created_at AS "CreatedAt", expires_at AS "ExpiresAt",
                   last_used_at AS "LastUsedAt"
            FROM morphdb._morph_api_keys
            WHERE project_id = @ProjectId
            ORDER BY created_at DESC
            """,
            new { ProjectId = projectId });

        return records.Select(MapToApiKey).ToList();
    }

    public async Task RevokeKeyAsync(
        Guid projectId,
        Guid keyId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(
            """
            UPDATE morphdb._morph_api_keys
            SET is_active = false
            WHERE id = @KeyId AND project_id = @ProjectId
            """,
            new { KeyId = keyId, ProjectId = projectId });
    }

    public async Task<(ApiKey Key, string RawKey)> RotateKeyAsync(
        Guid projectId,
        Guid keyId,
        bool revokeOld = true,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var oldKey = await connection.QueryFirstOrDefaultAsync<ApiKeyRecord>(
            """
            SELECT id AS "Id", project_id AS "ProjectId", key_type AS "KeyType", key_hash AS "KeyHash",
                   key_prefix AS "KeyPrefix", name AS "Name", description AS "Description",
                   is_active AS "IsActive", created_at AS "CreatedAt", expires_at AS "ExpiresAt",
                   last_used_at AS "LastUsedAt"
            FROM morphdb._morph_api_keys
            WHERE id = @KeyId AND project_id = @ProjectId
            """,
            new { KeyId = keyId, ProjectId = projectId });

        if (oldKey == null)
        {
            throw new InvalidOperationException($"API key {keyId} not found");
        }

        // Create new key with same properties
        var result = await CreateKeyAsync(
            projectId,
            (ApiKeyType)oldKey.KeyType,
            $"{oldKey.Name} (rotated)",
            oldKey.Description,
            oldKey.ExpiresAt,
            cancellationToken);

        // Optionally revoke old key
        if (revokeOld)
        {
            await RevokeKeyAsync(projectId, keyId, cancellationToken);
        }

        return result;
    }

    public async Task UpdateLastUsedAsync(
        Guid keyId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            await connection.ExecuteAsync(
                """
                UPDATE morphdb._morph_api_keys
                SET last_used_at = @LastUsedAt
                WHERE id = @KeyId
                """,
                new { KeyId = keyId, LastUsedAt = DateTimeOffset.UtcNow });
        }
        catch
        {
            // Ignore errors for last used update
        }
    }

    private static ApiKey MapToApiKey(ApiKeyRecord record)
    {
        return new ApiKey
        {
            Id = record.Id,
            ProjectId = record.ProjectId,
            KeyType = (ApiKeyType)record.KeyType,
            KeyHash = record.KeyHash,
            KeyPrefix = record.KeyPrefix,
            Name = record.Name,
            Description = record.Description,
            IsActive = record.IsActive,
            CreatedAt = record.CreatedAt,
            ExpiresAt = record.ExpiresAt,
            LastUsedAt = record.LastUsedAt
        };
    }

    private sealed class ApiKeyRecord
    {
        public Guid Id { get; set; }
        public Guid ProjectId { get; set; }
        public int KeyType { get; set; }
        public string KeyHash { get; set; } = string.Empty;
        public string KeyPrefix { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public bool IsActive { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset? ExpiresAt { get; set; }
        public DateTimeOffset? LastUsedAt { get; set; }
    }
}
