using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OData.Edm;
using MorphDB.Core.Abstractions;

namespace MorphDB.Service.OData;

/// <summary>
/// Provides and caches EDM models per tenant.
/// </summary>
public interface IEdmModelProvider
{
    /// <summary>
    /// Gets the EDM model for the specified tenant.
    /// </summary>
    Task<IEdmModel> GetModelAsync(Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the EDM model with entity set to table name mapping for the specified tenant.
    /// </summary>
    Task<EdmModelBuildResult> GetModelWithMappingAsync(Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Invalidates the cached model for the specified tenant.
    /// </summary>
    void InvalidateModel(Guid tenantId);

    /// <summary>
    /// Invalidates all cached models.
    /// </summary>
    void InvalidateAll();
}

/// <summary>
/// Default implementation of IEdmModelProvider with caching.
/// Uses IServiceScopeFactory to safely resolve scoped dependencies from a singleton.
/// </summary>
public sealed class CachingEdmModelProvider : IEdmModelProvider
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ConcurrentDictionary<Guid, CachedModel> _cache = new();
    private readonly TimeSpan _cacheExpiration;

    public CachingEdmModelProvider(
        IServiceScopeFactory scopeFactory,
        TimeSpan? cacheExpiration = null)
    {
        _scopeFactory = scopeFactory;
        _cacheExpiration = cacheExpiration ?? TimeSpan.FromMinutes(5);
    }

    public async Task<IEdmModel> GetModelAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var result = await GetModelWithMappingAsync(tenantId, cancellationToken);
        return result.Model;
    }

    public async Task<EdmModelBuildResult> GetModelWithMappingAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(tenantId, out var cached) && !cached.IsExpired)
        {
            return cached.Result;
        }

        // Create a scope to resolve scoped dependencies
        await using var scope = _scopeFactory.CreateAsyncScope();
        var schemaManager = scope.ServiceProvider.GetRequiredService<ISchemaManager>();
        var tables = await schemaManager.ListTablesAsync(tenantId, cancellationToken);

        var result = DynamicEdmModelBuilder.BuildModelWithMapping(tables);
        _cache[tenantId] = new CachedModel(result, DateTimeOffset.UtcNow.Add(_cacheExpiration));

        return result;
    }

    public void InvalidateModel(Guid tenantId)
    {
        _cache.TryRemove(tenantId, out _);
    }

    public void InvalidateAll()
    {
        _cache.Clear();
    }

    private sealed class CachedModel
    {
        public EdmModelBuildResult Result { get; }
        public DateTimeOffset ExpiresAt { get; }
        public bool IsExpired => DateTimeOffset.UtcNow >= ExpiresAt;

        public CachedModel(EdmModelBuildResult result, DateTimeOffset expiresAt)
        {
            Result = result;
            ExpiresAt = expiresAt;
        }
    }
}
