using System.Net.Sockets;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Audit;
using MorphDB.Core.Diagnostics;
using MorphDB.Core.Encryption;
using MorphDB.Core.Pipeline;
using MorphDB.Core.Security;
using MorphDB.Npgsql.Audit;
using MorphDB.Npgsql.Caching;
using MorphDB.Npgsql.Diagnostics;
using MorphDB.Npgsql.Encryption;
using MorphDB.Npgsql.Infrastructure;
using MorphDB.Npgsql.Pipeline;
using MorphDB.Npgsql.Pipeline.Transformers;
using MorphDB.Npgsql.Pipeline.Validators;
using MorphDB.Npgsql.Repositories;
using MorphDB.Npgsql.Schema;
using MorphDB.Npgsql.Security;
using MorphDB.Npgsql.Services;
using Npgsql;
using StackExchange.Redis;

namespace MorphDB.Npgsql;

/// <summary>
/// Extension methods for configuring MorphDB.Npgsql services.
/// </summary>
public static partial class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds MorphDB PostgreSQL services to the service collection.
    /// </summary>
    public static IServiceCollection AddMorphDbNpgsql(
        this IServiceCollection services,
        string connectionString,
        Action<MorphDbNpgsqlOptions>? configure = null)
    {
        // Dapper's snake_case mapping is applied by DapperConventions (module initializer), not
        // here: it must hold for any code that touches this assembly, not only for DI-booted apps.

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
        services.AddSingleton<IViewMetadataRepository, ViewMetadataRepository>();

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

            // Raw multiplexer for SCAN-based bulk invalidation, which IDistributedCache's
            // key-at-a-time contract cannot express.
            services.AddSingleton<IConnectionMultiplexer>(
                _ => ConnectionMultiplexer.Connect(options.RedisConnectionString));

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
                var metadataRepo = sp.GetRequiredService<IMetadataRepository>();
                var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<CachingSchemaManagerDecorator>>();
                return new CachingSchemaManagerDecorator(inner, cache, metadataRepo, logger);
            });
        }
        else
        {
            // No caching - use direct implementation
            services.AddSingleton<ISchemaManager, PostgresSchemaManager>();
        }

        services.AddSingleton<IMorphDataService, PostgresDataService>();
        services.AddSingleton<IViewManager, PostgresViewManager>();
        services.AddSingleton<ILookupResolver, PostgresLookupResolver>();
        services.AddSingleton<IRollupResolver, PostgresRollupResolver>();
        services.AddSingleton<IFormulaResolver, PostgresFormulaResolver>();
        services.AddSingleton<IAggregationService, PostgresAggregationService>();
        services.AddSingleton<IHierarchyQueryService, PostgresHierarchyQueryService>();
        services.AddSingleton<IWebhookManager, PostgresWebhookManager>();
        services.AddSingleton(options.BulkOperationOptions);
        services.AddSingleton<IBulkOperationService, PostgresBulkOperationService>();

        // Register security services
        services.AddSingleton<ISecurityPolicyService, SecurityPolicyService>();
        services.AddSingleton<ISecurityContextAccessor, SecurityContextAccessor>();

        // Register project and schema layer services (Phase 17: Schema-based Layer Separation)
        services.AddSingleton<ISchemaNameResolver, PostgresSchemaNameResolver>();
        services.AddSingleton<ISchemaLayerService, PostgresSchemaLayerService>();
        services.AddSingleton<IProjectRepository, ProjectRepository>();
        services.AddSingleton<IProjectService, ProjectService>();

        // Register audit services (Phase 19: Audit Logging)
        // PII masking is configured via options pattern
        services.Configure<PiiMaskingOptions>(opt => { });
        services.AddSingleton<IPiiMaskingService, PiiMaskingService>();
        services.AddSingleton<IAuditService, PostgresAuditService>();

        // Register query diagnostics (Phase 24: Production Hardening)
        services.Configure<QueryDiagnosticsOptions>(opt =>
        {
            opt.Enabled = options.QueryDiagnosticsOptions.Enabled;
            opt.SlowQueryThresholdMs = options.QueryDiagnosticsOptions.SlowQueryThresholdMs;
            opt.MaxSlowQueryEntries = options.QueryDiagnosticsOptions.MaxSlowQueryEntries;
            opt.LogSlowQueries = options.QueryDiagnosticsOptions.LogSlowQueries;
            opt.IncludeQueryPatterns = options.QueryDiagnosticsOptions.IncludeQueryPatterns;
            opt.StatisticsRetentionPeriod = options.QueryDiagnosticsOptions.StatisticsRetentionPeriod;
        });
        services.AddSingleton<IQueryDiagnostics, QueryDiagnosticsService>();

        // Register Write Pipeline components (Virtual Constraints)
        services.AddWritePipeline();

        // Register Transaction Service (Phase 28: Cross-Entity Transactions & Row-State)
        services.AddSingleton<ITransactionService, PostgresTransactionService>();

        return services;
    }

    /// <summary>
    /// Ensures the global morphdb schema and all system control-plane tables exist.
    /// Call this after building the service provider, typically in application startup.
    /// Safe to call repeatedly — uses IF NOT EXISTS internally.
    /// </summary>
    /// <remarks>
    /// Waits for the database to accept connections rather than failing on the first refusal. Under an
    /// orchestrator the database routinely takes a few seconds longer to accept connections than this
    /// process takes to start, and treating that as fatal exits the container — which then never
    /// recovers, even though the database comes up moments later. Only connection-level failures are
    /// retried; a schema error is a real fault and surfaces immediately.
    /// </remarks>
    public static async Task EnsureMorphDbSchemaAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        var schemaLayerService = services.GetRequiredService<ISchemaLayerService>();
        var logger = services.GetService<ILoggerFactory>()?.CreateLogger("MorphDB.Startup");

        var deadline = DateTimeOffset.UtcNow + SchemaBootstrapTimeout;
        var delay = TimeSpan.FromSeconds(1);

        while (true)
        {
            try
            {
                await schemaLayerService.EnsureGlobalSchemaAsync(cancellationToken);
                return;
            }
            catch (Exception ex) when (IsDatabaseUnreachable(ex) && DateTimeOffset.UtcNow + delay < deadline)
            {
                if (logger is not null)
                {
                    LogDatabaseNotReachable(logger, ex.Message, delay);
                }

                await Task.Delay(delay, cancellationToken);
                delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, MaxSchemaBootstrapDelay.Ticks));
            }
        }
    }

    [LoggerMessage(LogLevel.Warning, "Database not reachable yet ({Reason}); retrying schema bootstrap in {Delay}.")]
    private static partial void LogDatabaseNotReachable(ILogger logger, string reason, TimeSpan delay);

    /// <summary>How long startup waits for the database to accept connections before giving up.</summary>
    private static readonly TimeSpan SchemaBootstrapTimeout = TimeSpan.FromSeconds(60);

    /// <summary>Caps the exponential backoff so late retries stay responsive.</summary>
    private static readonly TimeSpan MaxSchemaBootstrapDelay = TimeSpan.FromSeconds(5);

    /// <summary>
    /// True when the failure is the database not being reachable yet, as opposed to a fault in the
    /// schema itself. Npgsql reports a refused or unresolvable endpoint as a transient
    /// <see cref="NpgsqlException"/> wrapping a socket error; a <see cref="PostgresException"/> means
    /// the server answered and rejected something, which retrying cannot fix.
    /// </summary>
    private static bool IsDatabaseUnreachable(Exception exception) => exception switch
    {
        PostgresException => false,
        NpgsqlException { IsTransient: true } => true,
        NpgsqlException => exception.InnerException is SocketException or TimeoutException,
        SocketException => true,
        _ => false,
    };

    /// <summary>
    /// Registers Write Pipeline components for Virtual Constraint enforcement.
    /// </summary>
    public static IServiceCollection AddWritePipeline(this IServiceCollection services)
    {
        // Register Transformers (data modification before write)
        services.AddSingleton<ITransformer, IdApplier>();         // UUID v7 generation
        services.AddSingleton<ITransformer, DefaultValueApplier>();
        services.AddSingleton<ITransformer, TimestampApplier>();
        services.AddSingleton<ITransformer, VersionApplier>();
        services.AddSingleton<ITransformer, AuditFieldApplier>();
        services.AddSingleton<ITransformer, OwnerApplier>();      // Ownership: _owner_id
        services.AddSingleton<ITransformer, SortOrderApplier>();  // Hierarchy: _sort_order
        services.AddSingleton<ITransformer, SoftDeleteApplier>();
        services.AddSingleton<ITransformer, RowStateApplier>(); // Row state for draft mode

        // Register Validators (virtual constraint enforcement)
        services.AddSingleton<IValidator, UnknownFieldValidator>();
        services.AddSingleton<IValidator, RequiredValidator>();
        services.AddSingleton<IValidator, UniqueValidator>();
        services.AddSingleton<IValidator, ForeignKeyValidator>();
        services.AddSingleton<IValidator, CheckValidator>();

        // Register Write Executor (executes actual DB operations)
        services.AddSingleton<IWriteExecutor, PostgresWriteExecutor>();

        // Register Write Pipeline (orchestrates transformers and validators)
        services.AddSingleton<IWritePipeline, WritePipeline>();

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

    /// <summary>
    /// Options for query diagnostics and slow query detection.
    /// </summary>
    public QueryDiagnosticsOptions QueryDiagnosticsOptions { get; set; } = new();
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
