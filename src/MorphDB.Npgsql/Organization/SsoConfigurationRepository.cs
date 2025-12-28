using System.Text.Json;
using Dapper;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Models;
using Npgsql;

namespace MorphDB.Npgsql.Organization;

/// <summary>
/// PostgreSQL repository for SSO configuration persistence.
/// </summary>
public sealed class SsoConfigurationRepository : ISsoConfigurationRepository
{
    private readonly NpgsqlDataSource _dataSource;

    public SsoConfigurationRepository(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    /// <inheritdoc/>
    public async Task<SsoConfiguration?> GetByIdAsync(
        Guid ssoConfigId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT * FROM morphdb._morph_sso_configurations
            WHERE sso_config_id = @SsoConfigId
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var entity = await connection.QuerySingleOrDefaultAsync<SsoConfigEntity>(sql, new { SsoConfigId = ssoConfigId });
        return entity is null ? null : MapToConfig(entity);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<SsoConfiguration>> ListByOrganizationAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT * FROM morphdb._morph_sso_configurations
            WHERE organization_id = @OrganizationId
            ORDER BY created_at DESC
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var entities = await connection.QueryAsync<SsoConfigEntity>(sql, new { OrganizationId = organizationId });
        return entities.Select(MapToConfig).ToList();
    }

    /// <inheritdoc/>
    public async Task<SsoConfiguration?> GetActiveByOrganizationAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT * FROM morphdb._morph_sso_configurations
            WHERE organization_id = @OrganizationId AND status = @ActiveStatus
            LIMIT 1
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var entity = await connection.QuerySingleOrDefaultAsync<SsoConfigEntity>(sql, new
        {
            OrganizationId = organizationId,
            ActiveStatus = (int)SsoConfigStatus.Active
        });
        return entity is null ? null : MapToConfig(entity);
    }

    /// <inheritdoc/>
    public async Task<SsoConfiguration> CreateAsync(
        SsoConfiguration config,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            INSERT INTO morphdb._morph_sso_configurations (
                sso_config_id, organization_id, name, provider_type, authority,
                client_id, client_secret_encrypted, scopes, allowed_domains,
                claim_mappings, auto_provision_users, default_role, status,
                last_error, created_at, updated_at
            )
            VALUES (
                @SsoConfigId, @OrganizationId, @Name, @ProviderType, @Authority,
                @ClientId, @ClientSecretEncrypted, @Scopes, @AllowedDomains,
                @ClaimMappings::jsonb, @AutoProvisionUsers, @DefaultRole, @Status,
                @LastError, NOW(), NOW()
            )
            RETURNING *
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var entity = await connection.QuerySingleAsync<SsoConfigEntity>(sql, new
        {
            SsoConfigId = config.SsoConfigId == Guid.Empty ? Guid.NewGuid() : config.SsoConfigId,
            config.OrganizationId,
            config.Name,
            ProviderType = (int)config.ProviderType,
            config.Authority,
            config.ClientId,
            ClientSecretEncrypted = config.ClientSecret, // TODO: Encrypt in service layer
            Scopes = config.Scopes.ToArray(),
            AllowedDomains = config.AllowedDomains?.ToArray(),
            ClaimMappings = config.ClaimMappings is not null
                ? JsonSerializer.Serialize(config.ClaimMappings)
                : null,
            config.AutoProvisionUsers,
            DefaultRole = (int)config.DefaultRole,
            Status = (int)config.Status,
            config.LastError
        });

        return MapToConfig(entity);
    }

    /// <inheritdoc/>
    public async Task<SsoConfiguration> UpdateAsync(
        SsoConfiguration config,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE morphdb._morph_sso_configurations
            SET name = @Name,
                provider_type = @ProviderType,
                authority = @Authority,
                client_id = @ClientId,
                client_secret_encrypted = COALESCE(@ClientSecretEncrypted, client_secret_encrypted),
                scopes = @Scopes,
                allowed_domains = @AllowedDomains,
                claim_mappings = @ClaimMappings::jsonb,
                auto_provision_users = @AutoProvisionUsers,
                default_role = @DefaultRole,
                updated_at = NOW()
            WHERE sso_config_id = @SsoConfigId
            RETURNING *
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var entity = await connection.QuerySingleAsync<SsoConfigEntity>(sql, new
        {
            config.SsoConfigId,
            config.Name,
            ProviderType = (int)config.ProviderType,
            config.Authority,
            config.ClientId,
            ClientSecretEncrypted = config.ClientSecret,
            Scopes = config.Scopes.ToArray(),
            AllowedDomains = config.AllowedDomains?.ToArray(),
            ClaimMappings = config.ClaimMappings is not null
                ? JsonSerializer.Serialize(config.ClaimMappings)
                : null,
            config.AutoProvisionUsers,
            DefaultRole = (int)config.DefaultRole
        });

        return MapToConfig(entity);
    }

    /// <inheritdoc/>
    public async Task UpdateStatusAsync(
        Guid ssoConfigId,
        SsoConfigStatus status,
        string? errorMessage = null,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE morphdb._morph_sso_configurations
            SET status = @Status,
                last_error = @LastError,
                updated_at = NOW()
            WHERE sso_config_id = @SsoConfigId
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(sql, new
        {
            SsoConfigId = ssoConfigId,
            Status = (int)status,
            LastError = errorMessage
        });
    }

    /// <inheritdoc/>
    public async Task RecordUsageAsync(
        Guid ssoConfigId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE morphdb._morph_sso_configurations
            SET last_used_at = NOW()
            WHERE sso_config_id = @SsoConfigId
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(sql, new { SsoConfigId = ssoConfigId });
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(
        Guid ssoConfigId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            DELETE FROM morphdb._morph_sso_configurations
            WHERE sso_config_id = @SsoConfigId
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(sql, new { SsoConfigId = ssoConfigId });
    }

    private static SsoConfiguration MapToConfig(SsoConfigEntity entity)
    {
        return new SsoConfiguration
        {
            SsoConfigId = entity.sso_config_id,
            OrganizationId = entity.organization_id,
            Name = entity.name,
            ProviderType = (SsoProviderType)entity.provider_type,
            Authority = entity.authority,
            ClientId = entity.client_id,
            ClientSecret = entity.client_secret_encrypted,
            Scopes = entity.scopes ?? ["openid", "profile", "email"],
            AllowedDomains = entity.allowed_domains,
            ClaimMappings = entity.claim_mappings is not null
                ? JsonSerializer.Deserialize<SsoClaimMappings>(entity.claim_mappings)
                : null,
            AutoProvisionUsers = entity.auto_provision_users,
            DefaultRole = (OrganizationRole)entity.default_role,
            Status = (SsoConfigStatus)entity.status,
            LastError = entity.last_error,
            CreatedAt = entity.created_at,
            UpdatedAt = entity.updated_at,
            LastUsedAt = entity.last_used_at
        };
    }

    private sealed record SsoConfigEntity(
        Guid sso_config_id,
        Guid organization_id,
        string name,
        int provider_type,
        string authority,
        string client_id,
        string? client_secret_encrypted,
        string[]? scopes,
        string[]? allowed_domains,
        string? claim_mappings,
        bool auto_provision_users,
        int default_role,
        int status,
        string? last_error,
        DateTimeOffset created_at,
        DateTimeOffset updated_at,
        DateTimeOffset? last_used_at);
}
