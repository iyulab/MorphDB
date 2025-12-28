using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Encryption;
using MorphDB.Core.Security;
using MorphDB.Npgsql.Caching;
using MorphDB.Npgsql.Encryption;
using MorphDB.Npgsql.Infrastructure;
using MorphDB.Npgsql.Repositories;
using MorphDB.Npgsql.Schema;
using MorphDB.Npgsql.Security;
using MorphDB.Npgsql.Services;
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

        // Build connection string with optimized pooling settings
        var connStringBuilder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            MinPoolSize = options.ConnectionPoolOptions.MinPoolSize,
            MaxPoolSize = options.ConnectionPoolOptions.MaxPoolSize,
            ConnectionIdleLifetime = (int)options.ConnectionPoolOptions.ConnectionIdleLifetime.TotalSeconds,
            ConnectionPruningInterval = (int)options.ConnectionPoolOptions.ConnectionPruningInterval.TotalSeconds,
            Timeout = (int)options.ConnectionPoolOptions.ConnectionTimeout.TotalSeconds,
            CommandTimeout = (int)options.ConnectionPoolOptions.CommandTimeout.TotalSeconds,
            Pooling = options.ConnectionPoolOptions.Enabled,
            Multiplexing = options.ConnectionPoolOptions.Multiplexing
        };

        // Register NpgsqlDataSource with optimized settings
        var dataSourceBuilder = new NpgsqlDataSourceBuilder(connStringBuilder.ConnectionString);
        dataSourceBuilder.EnableDynamicJson();
        var dataSource = dataSourceBuilder.Build();

        services.AddSingleton(dataSource);

        // Register core infrastructure
        services.AddSingleton<INameHasher, Sha256NameHasher>();
        services.AddSingleton(options.AdvisoryLockOptions);
        services.AddSingleton<IAdvisoryLockManager, PostgresAdvisoryLockManager>();

        // Register repositories
        services.AddSingleton<IMetadataRepository, MetadataRepository>();

        // Register encryption services (if configured)
        if (options.EncryptionOptions is not null && !string.IsNullOrEmpty(options.EncryptionOptions.MasterKey))
        {
            services.Configure<DataEncryptionOptions>(opt =>
            {
                opt.Enabled = options.EncryptionOptions.Enabled;
                opt.MasterKey = options.EncryptionOptions.MasterKey;
                opt.KeyVersion = options.EncryptionOptions.KeyVersion;
                opt.Algorithm = options.EncryptionOptions.Algorithm;
                opt.EncryptAllByDefault = options.EncryptionOptions.EncryptAllByDefault;
                opt.ExcludedColumns = options.EncryptionOptions.ExcludedColumns;
            });

            services.AddSingleton<IKeyDerivationService, HkdfKeyDerivationService>();
            services.AddSingleton<IDataEncryptionService, AesGcmDataEncryptionService>();
            services.AddSingleton<IKeyRotationService, KeyRotationService>();
        }
        else
        {
            // Register default encryption options even when encryption is disabled
            services.Configure<DataEncryptionOptions>(opt => opt.Enabled = false);
        }

        // Register services
        services.AddSingleton<IChangeLogger, ChangeLogger>();
        services.AddSingleton(options.SchemaManagerOptions);

        // Register schema manager with optional caching decorator
        if (!string.IsNullOrEmpty(options.RedisConnectionString) && options.SchemaCacheOptions.Enabled)
        {
            // Register Redis distributed cache
            services.AddStackExchangeRedisCache(redisOptions =>
            {
                redisOptions.Configuration = options.RedisConnectionString;
                redisOptions.InstanceName = options.SchemaCacheOptions.KeyPrefix + ":";
            });

            // Configure schema cache options
            services.Configure<SchemaCacheOptions>(cacheOpts =>
            {
                cacheOpts.Enabled = options.SchemaCacheOptions.Enabled;
                cacheOpts.TableCacheDuration = options.SchemaCacheOptions.TableCacheDuration;
                cacheOpts.TableListCacheDuration = options.SchemaCacheOptions.TableListCacheDuration;
                cacheOpts.KeyPrefix = options.SchemaCacheOptions.KeyPrefix;
            });

            // Register cache and decorator
            services.AddSingleton<ISchemaCache, RedisSchemaCache>();
            services.AddSingleton<PostgresSchemaManager>();
            services.AddSingleton<ISchemaManager>(sp =>
            {
                var inner = sp.GetRequiredService<PostgresSchemaManager>();
                var cache = sp.GetRequiredService<ISchemaCache>();
                var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<CachingSchemaManagerDecorator>>();
                return new CachingSchemaManagerDecorator(inner, cache, logger);
            });
        }
        else
        {
            // No caching - use direct implementation
            services.AddSingleton<ISchemaManager, PostgresSchemaManager>();
        }

        services.AddSingleton<IMorphDataService, PostgresDataService>();
        services.AddSingleton<IWebhookManager, PostgresWebhookManager>();
        services.AddSingleton(options.BulkOperationOptions);
        services.AddSingleton<IBulkOperationService, PostgresBulkOperationService>();

        // Register security services
        services.AddSingleton<IApiKeyService, ApiKeyService>();
        services.AddSingleton<ISecurityPolicyService, SecurityPolicyService>();
        services.AddSingleton<ISecurityContextAccessor, SecurityContextAccessor>();
        services.AddSingleton<IJwtService, JwtService>();

        // Register project and schema layer services (Phase 17: Schema-based Layer Separation)
        services.AddSingleton<ISchemaNameResolver, PostgresSchemaNameResolver>();
        services.AddSingleton<ISchemaLayerService, PostgresSchemaLayerService>();
        services.AddSingleton<IProjectRepository, ProjectRepository>();
        services.AddSingleton<IProjectService, ProjectService>();

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
    /// Options for schema manager behavior.
    /// </summary>
    public SchemaManagerOptions SchemaManagerOptions { get; set; } = new();

    /// <summary>
    /// Options for data encryption.
    /// Set MasterKey to enable automatic encryption.
    /// </summary>
    public DataEncryptionOptions? EncryptionOptions { get; set; }

    /// <summary>
    /// Redis connection string for distributed caching.
    /// </summary>
    public string? RedisConnectionString { get; set; }

    /// <summary>
    /// Options for schema caching behavior.
    /// </summary>
    public SchemaCacheOptions SchemaCacheOptions { get; set; } = new();

    /// <summary>
    /// Options for connection pooling behavior.
    /// </summary>
    public ConnectionPoolOptions ConnectionPoolOptions { get; set; } = new();

    /// <summary>
    /// Options for bulk operations.
    /// </summary>
    public BulkOperationOptions BulkOperationOptions { get; set; } = new();
}

/// <summary>
/// Options for PostgreSQL connection pooling.
/// </summary>
public sealed class ConnectionPoolOptions
{
    /// <summary>
    /// Whether connection pooling is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Minimum number of connections in the pool.
    /// </summary>
    public int MinPoolSize { get; set; } = 5;

    /// <summary>
    /// Maximum number of connections in the pool.
    /// </summary>
    public int MaxPoolSize { get; set; } = 100;

    /// <summary>
    /// Time a connection can remain idle before being closed.
    /// </summary>
    public TimeSpan ConnectionIdleLifetime { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Interval between connection pruning cycles.
    /// </summary>
    public TimeSpan ConnectionPruningInterval { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Timeout for establishing new connections.
    /// </summary>
    public TimeSpan ConnectionTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Default command execution timeout.
    /// </summary>
    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Whether to use multiplexing for better connection utilization.
    /// </summary>
    public bool Multiplexing { get; set; }
}
