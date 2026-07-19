using Dapper;
using Microsoft.Extensions.Logging;
using MorphDB.Core.Abstractions;
using MorphDB.Npgsql.Ddl;
using Npgsql;

namespace MorphDB.Npgsql.Schema;

/// <summary>
/// PostgreSQL implementation of schema layer management.
/// Handles creation, deletion, and management of project schemas.
/// </summary>
public sealed partial class PostgresSchemaLayerService : ISchemaLayerService
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly ISchemaNameResolver _schemaNameResolver;
    private readonly ILogger<PostgresSchemaLayerService> _logger;

    public PostgresSchemaLayerService(
        NpgsqlDataSource dataSource,
        ISchemaNameResolver schemaNameResolver,
        ILogger<PostgresSchemaLayerService> logger)
    {
        _dataSource = dataSource;
        _schemaNameResolver = schemaNameResolver;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task EnsureGlobalSchemaAsync(CancellationToken cancellationToken = default)
    {
        LogEnsuringGlobalSchema(_logger);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        // Repair first: the canonical DDL is IF NOT EXISTS throughout, so it can neither remove nor
        // reshape anything an older version left behind.
        await connection.ExecuteAsync(GlobalSchemaMigrations.BuildPreBootstrapDdl());

        var ddl = DdlBuilder.BuildGlobalSystemSchemaDdl();
        await connection.ExecuteAsync(ddl);

        LogGlobalSchemaEnsured(_logger);
    }

    /// <inheritdoc/>
    public async Task<SchemaNames> ProvisionProjectSchemasAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var schemaNames = _schemaNameResolver.GetSchemaNames(projectId);

        LogProvisioningSchemas(_logger, projectId, schemaNames.SystemSchema, schemaNames.DataSchema);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            // Create system schema
            var createSystemSchemaSql = DdlBuilder.BuildCreateSchema(schemaNames.SystemSchema);
            await connection.ExecuteAsync(createSystemSchemaSql, transaction: transaction);

            // Create data schema
            var createDataSchemaSql = DdlBuilder.BuildCreateSchema(schemaNames.DataSchema);
            await connection.ExecuteAsync(createDataSchemaSql, transaction: transaction);

            // Create system tables in system schema
            var systemTablesSql = DdlBuilder.BuildSystemTablesDdl(schemaNames.SystemSchema);
            await connection.ExecuteAsync(systemTablesSql, transaction: transaction);

            // No extension is enabled here on purpose. Provisioning a project must not require
            // CREATE EXTENSION: managed PostgreSQL gates it behind a server-parameter allow-list,
            // so a tenant could not be created at all on Azure, Cloud SQL or RDS. Nothing needs it —
            // system tables default to the built-in gen_random_uuid(), and DdlBuilder only accepts
            // function defaults from a fixed set that excludes the uuid-ossp ones.

            await transaction.CommitAsync(cancellationToken);

            LogSchemasProvisioned(_logger, projectId);

            return schemaNames;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);

            LogSchemaProvisioningFailed(_logger, projectId, ex);

            throw;
        }
    }

    /// <inheritdoc/>
    public async Task DropProjectSchemasAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var schemaNames = _schemaNameResolver.GetSchemaNames(projectId);

        LogDroppingSchemas(_logger, projectId, schemaNames.SystemSchema, schemaNames.DataSchema);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            // Drop data schema first (no dependencies)
            var dropDataSchemaSql = DdlBuilder.BuildDropSchema(schemaNames.DataSchema, cascade: true);
            await connection.ExecuteAsync(dropDataSchemaSql, transaction: transaction);

            // Drop system schema
            var dropSystemSchemaSql = DdlBuilder.BuildDropSchema(schemaNames.SystemSchema, cascade: true);
            await connection.ExecuteAsync(dropSystemSchemaSql, transaction: transaction);

            await transaction.CommitAsync(cancellationToken);

            LogSchemasDropped(_logger, projectId);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);

            LogSchemaDropFailed(_logger, projectId, ex);

            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> SchemaExistsAsync(
        string schemaName,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        var exists = await connection.ExecuteScalarAsync<bool>(
            DdlBuilder.BuildSchemaExistsQuery(),
            new { schemaName });

        return exists;
    }

    /// <inheritdoc/>
    public async Task<SchemaExistenceResult> ProjectSchemasExistAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var schemaNames = _schemaNameResolver.GetSchemaNames(projectId);

        var systemExists = await SchemaExistsAsync(schemaNames.SystemSchema, cancellationToken);
        var dataExists = await SchemaExistsAsync(schemaNames.DataSchema, cancellationToken);

        return new SchemaExistenceResult(systemExists, dataExists);
    }

    /// <inheritdoc/>
    public async Task<SchemaStats> GetSchemaStatsAsync(
        string schemaName,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        var result = await connection.QuerySingleOrDefaultAsync<SchemaStatsDto>(
            DdlBuilder.BuildSchemaStatsQuery(),
            new { schemaName });

        if (result is null)
        {
            return new SchemaStats
            {
                SchemaName = schemaName,
                TableCount = 0,
                IndexCount = 0,
                TotalSizeBytes = 0,
                DataSizeBytes = 0,
                IndexSizeBytes = 0
            };
        }

        return new SchemaStats
        {
            SchemaName = schemaName,
            TableCount = result.TableCount,
            IndexCount = result.IndexCount,
            TotalSizeBytes = result.TotalSize,
            DataSizeBytes = result.DataSize,
            IndexSizeBytes = result.IndexSize
        };
    }

    /// <inheritdoc/>
    public async Task<ProjectSchemaStats> GetProjectStatsAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var schemaNames = _schemaNameResolver.GetSchemaNames(projectId);

        var systemStats = await GetSchemaStatsAsync(schemaNames.SystemSchema, cancellationToken);
        var dataStats = await GetSchemaStatsAsync(schemaNames.DataSchema, cancellationToken);

        return new ProjectSchemaStats
        {
            ProjectId = projectId,
            SystemSchemaStats = systemStats,
            DataSchemaStats = dataStats
        };
    }

    /// <inheritdoc/>
    public async Task RenameSchemaAsync(
        string oldName,
        string newName,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        var renameSql = DdlBuilder.BuildRenameSchema(oldName, newName);
        await connection.ExecuteAsync(renameSql);

        LogSchemaRenamed(_logger, oldName, newName);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ManagedSchemaInfo>> ListManagedSchemasAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        const string sql = """
            SELECT
                n.nspname AS schema_name,
                COUNT(DISTINCT c.relname) FILTER (WHERE c.relkind = 'r') AS table_count,
                COALESCE(SUM(pg_total_relation_size(c.oid)) FILTER (WHERE c.relkind = 'r'), 0) AS size_bytes,
                (SELECT min(s.statime) FROM pg_stat_user_tables s WHERE s.schemaname = n.nspname) AS created_at
            FROM pg_namespace n
            LEFT JOIN pg_class c ON c.relnamespace = n.oid
            WHERE n.nspname LIKE 'p_%_sys'
               OR n.nspname LIKE 'p_%_dat'
               OR n.nspname = 'morphdb'
            GROUP BY n.nspname
            ORDER BY n.nspname
            """;

        var results = await connection.QueryAsync<ManagedSchemaDto>(sql);

        return results.Select(r =>
        {
            var schemaType = _schemaNameResolver.GetSchemaType(r.SchemaName);
            Guid? projectId = null;
            if (schemaType is SchemaType.ProjectSystem or SchemaType.ProjectData)
            {
                if (_schemaNameResolver.TryParseSchemaName(r.SchemaName, out var parsedProjectId))
                {
                    projectId = parsedProjectId;
                }
            }

            return new ManagedSchemaInfo
            {
                SchemaName = r.SchemaName,
                SchemaType = schemaType,
                ProjectId = projectId,
                TableCount = r.TableCount,
                SizeBytes = r.SizeBytes,
                CreatedAt = r.CreatedAt ?? DateTimeOffset.MinValue
            };
        }).ToList();
    }

    /// <inheritdoc/>
    public async Task<SchemaHealthReport> ValidateSchemaHealthAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var issues = new List<SchemaHealthIssue>();
        var schemaNames = _schemaNameResolver.GetSchemaNames(projectId);

        // Check if schemas exist
        var existence = await ProjectSchemasExistAsync(projectId, cancellationToken);

        if (!existence.SystemSchemaExists)
        {
            issues.Add(new SchemaHealthIssue
            {
                Code = "MISSING_SYSTEM_SCHEMA",
                Message = $"System schema '{schemaNames.SystemSchema}' does not exist",
                Severity = SchemaHealthSeverity.Critical,
                AffectedObject = schemaNames.SystemSchema
            });
        }

        if (!existence.DataSchemaExists)
        {
            issues.Add(new SchemaHealthIssue
            {
                Code = "MISSING_DATA_SCHEMA",
                Message = $"Data schema '{schemaNames.DataSchema}' does not exist",
                Severity = SchemaHealthSeverity.Critical,
                AffectedObject = schemaNames.DataSchema
            });
        }

        if (existence.PartiallyExists)
        {
            issues.Add(new SchemaHealthIssue
            {
                Code = "PARTIAL_SCHEMA_STATE",
                Message = "Only one of the two project schemas exists - inconsistent state",
                Severity = SchemaHealthSeverity.Critical
            });
        }

        // If schemas exist, validate system tables
        if (existence.BothExist)
        {
            await ValidateSystemTablesAsync(schemaNames.SystemSchema, issues, cancellationToken);
        }

        return new SchemaHealthReport
        {
            ProjectId = projectId,
            IsHealthy = issues.Count == 0,
            Issues = issues,
            CheckedAt = DateTimeOffset.UtcNow
        };
    }

    private async Task ValidateSystemTablesAsync(
        string systemSchema,
        List<SchemaHealthIssue> issues,
        CancellationToken cancellationToken)
    {
        var requiredTables = new[] { "_tables", "_columns", "_indexes", "_relations", "_schema_changelog" };

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        foreach (var tableName in requiredTables)
        {
            var exists = await connection.ExecuteScalarAsync<bool>(
                """
                SELECT EXISTS(
                    SELECT 1 FROM information_schema.tables
                    WHERE table_schema = @schemaName AND table_name = @tableName
                )
                """,
                new { schemaName = systemSchema, tableName });

            if (!exists)
            {
                issues.Add(new SchemaHealthIssue
                {
                    Code = "MISSING_SYSTEM_TABLE",
                    Message = $"Required system table '{tableName}' is missing",
                    Severity = SchemaHealthSeverity.Error,
                    AffectedObject = $"{systemSchema}.{tableName}"
                });
            }
        }
    }

    // DTOs for Dapper mapping
    private sealed class SchemaStatsDto
    {
        public string SchemaName { get; init; } = default!;
        public int TableCount { get; init; }
        public int IndexCount { get; init; }
        public long TotalSize { get; init; }
        public long DataSize { get; init; }
        public long IndexSize { get; init; }
    }

    private sealed class ManagedSchemaDto
    {
        public string SchemaName { get; init; } = default!;
        public int TableCount { get; init; }
        public long SizeBytes { get; init; }
        public DateTimeOffset? CreatedAt { get; init; }
    }

    // LoggerMessage delegates for high-performance logging
    [LoggerMessage(LogLevel.Information, "Ensuring global morphdb schema exists")]
    private static partial void LogEnsuringGlobalSchema(ILogger logger);

    [LoggerMessage(LogLevel.Information, "Global morphdb schema ensured")]
    private static partial void LogGlobalSchemaEnsured(ILogger logger);

    [LoggerMessage(LogLevel.Information, "Provisioning schemas for project {ProjectId}: {SystemSchema}, {DataSchema}")]
    private static partial void LogProvisioningSchemas(ILogger logger, Guid projectId, string systemSchema, string dataSchema);

    [LoggerMessage(LogLevel.Information, "Successfully provisioned schemas for project {ProjectId}")]
    private static partial void LogSchemasProvisioned(ILogger logger, Guid projectId);

    [LoggerMessage(LogLevel.Error, "Failed to provision schemas for project {ProjectId}")]
    private static partial void LogSchemaProvisioningFailed(ILogger logger, Guid projectId, Exception exception);

    [LoggerMessage(LogLevel.Warning, "Dropping schemas for project {ProjectId}: {SystemSchema}, {DataSchema}")]
    private static partial void LogDroppingSchemas(ILogger logger, Guid projectId, string systemSchema, string dataSchema);

    [LoggerMessage(LogLevel.Information, "Successfully dropped schemas for project {ProjectId}")]
    private static partial void LogSchemasDropped(ILogger logger, Guid projectId);

    [LoggerMessage(LogLevel.Error, "Failed to drop schemas for project {ProjectId}")]
    private static partial void LogSchemaDropFailed(ILogger logger, Guid projectId, Exception exception);

    [LoggerMessage(LogLevel.Information, "Renamed schema {OldName} to {NewName}")]
    private static partial void LogSchemaRenamed(ILogger logger, string oldName, string newName);
}
