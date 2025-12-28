using MorphDB.Core.Models;

namespace MorphDB.Core.Abstractions;

/// <summary>
/// Provides caching for schema metadata with tenant isolation.
/// </summary>
public interface ISchemaCache
{
    /// <summary>
    /// Gets cached table metadata by tenant and logical name.
    /// </summary>
    Task<TableMetadata?> GetTableAsync(
        Guid tenantId,
        string logicalName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets cached table metadata by ID.
    /// </summary>
    Task<TableMetadata?> GetTableByIdAsync(
        Guid tableId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets cached list of tables for a tenant.
    /// </summary>
    Task<IReadOnlyList<TableMetadata>?> GetTablesAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets table metadata in cache.
    /// </summary>
    Task SetTableAsync(
        TableMetadata table,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets list of tables in cache.
    /// </summary>
    Task SetTablesAsync(
        Guid tenantId,
        IReadOnlyList<TableMetadata> tables,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Invalidates cached table by ID.
    /// </summary>
    Task InvalidateTableAsync(
        Guid tableId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Invalidates cached table by tenant and logical name.
    /// </summary>
    Task InvalidateTableAsync(
        Guid tenantId,
        string logicalName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Invalidates all cached tables for a tenant.
    /// </summary>
    Task InvalidateTenantTablesAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Invalidates all cache entries.
    /// </summary>
    Task InvalidateAllAsync(
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Configuration options for schema caching.
/// </summary>
public sealed class SchemaCacheOptions
{
    /// <summary>
    /// Time-to-live for cached table metadata.
    /// </summary>
    public TimeSpan TableCacheDuration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Time-to-live for cached table lists.
    /// </summary>
    public TimeSpan TableListCacheDuration { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Whether caching is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Key prefix for Redis keys.
    /// </summary>
    public string KeyPrefix { get; set; } = "morphdb:schema";
}
