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
        if (_cache.TryGetValue(tenantId, out var cached) && !cached.IsExpired)
        {
            return cached.Model;
        }

        // Create a scope to resolve scoped dependencies
        await using var scope = _scopeFactory.CreateAsyncScope();
        var schemaManager = scope.ServiceProvider.GetRequiredService<ISchemaManager>();
        var tables = await schemaManager.ListTablesAsync(tenantId, cancellationToken);

        var model = DynamicEdmModelBuilder.BuildModel(tables);
        _cache[tenantId] = new CachedModel(model, DateTimeOffset.UtcNow.Add(_cacheExpiration));

        return model;
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
        public IEdmModel Model { get; }
        public DateTimeOffset ExpiresAt { get; }
        public bool IsExpired => DateTimeOffset.UtcNow >= ExpiresAt;

        public CachedModel(IEdmModel model, DateTimeOffset expiresAt)
        {
            Model = model;
            ExpiresAt = expiresAt;
        }
    }
}
