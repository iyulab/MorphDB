using Microsoft.Extensions.DependencyInjection;
using MorphDB.Core.Abstractions;
using MorphDB.Npgsql.Infrastructure;
using Npgsql;

namespace MorphDB.Npgsql;

/// <summary>
/// Extension methods for configuring MorphDB.Npgsql services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds MorphDB PostgreSQL services to the service collection.
    /// </summary>
    public static IServiceCollection AddMorphDbNpgsql(
        this IServiceCollection services,
        string connectionString,
        Action<MorphDbNpgsqlOptions>? configure = null)
    {
        var options = new MorphDbNpgsqlOptions();
        configure?.Invoke(options);

        // Register NpgsqlDataSource
        var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
        dataSourceBuilder.EnableDynamicJson();
        var dataSource = dataSourceBuilder.Build();

        services.AddSingleton(dataSource);

        // Register core services
        services.AddSingleton<INameHasher, Sha256NameHasher>();
        services.AddSingleton(options.AdvisoryLockOptions);
        services.AddSingleton<IAdvisoryLockManager, PostgresAdvisoryLockManager>();

        // TODO: Register SchemaManager, SchemaMapping, QueryBuilder when implemented

        return services;
    }
}

/// <summary>
/// Options for configuring MorphDB.Npgsql.
/// </summary>
public sealed class MorphDbNpgsqlOptions
{
    /// <summary>
    /// Options for advisory lock behavior.
    /// </summary>
    public AdvisoryLockOptions AdvisoryLockOptions { get; set; } = new();

    /// <summary>
    /// Redis connection string for distributed caching.
    /// </summary>
    public string? RedisConnectionString { get; set; }

    /// <summary>
    /// Cache expiration time for schema mappings.
    /// </summary>
    public TimeSpan SchemaCacheExpiration { get; set; } = TimeSpan.FromMinutes(5);
}
