using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OData.Edm;
using MorphDB.Core.Abstractions;

namespace MorphDB.Service.OData;

/// <summary>
/// Provides and caches EDM models per project.
/// </summary>
public interface IEdmModelProvider
{
    /// <summary>
    /// Gets the EDM model for the specified project.
    /// </summary>
    Task<IEdmModel> GetModelAsync(Guid projectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the EDM model with entity set to table name mapping for the specified project.
    /// </summary>
    Task<EdmModelBuildResult> GetModelWithMappingAsync(Guid projectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Invalidates the cached model for the specified project.
    /// </summary>
    void InvalidateModel(Guid projectId);

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

    public async Task<IEdmModel> GetModelAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var result = await GetModelWithMappingAsync(projectId, cancellationToken);
        return result.Model;
    }

    public async Task<EdmModelBuildResult> GetModelWithMappingAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(projectId, out var cached) && !cached.IsExpired)
        {
            return cached.Result;
        }

        // Create a scope to resolve scoped dependencies
        await using var scope = _scopeFactory.CreateAsyncScope();
        var schemaManager = scope.ServiceProvider.GetRequiredService<ISchemaManager>();
        var tables = await schemaManager.ListTablesAsync(projectId, cancellationToken);

        var result = DynamicEdmModelBuilder.BuildModelWithMapping(tables);
        _cache[projectId] = new CachedModel(result, DateTimeOffset.UtcNow.Add(_cacheExpiration));

        return result;
    }

    public void InvalidateModel(Guid projectId)
    {
        _cache.TryRemove(projectId, out _);
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
