using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Encryption;
using MorphDB.Core.Security;
using MorphDB.Npgsql.Encryption;
using MorphDB.Npgsql.Infrastructure;
using MorphDB.Npgsql.Repositories;
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

        // Register NpgsqlDataSource
        var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
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
        }
        else
        {
            // Register default encryption options even when encryption is disabled
            services.Configure<DataEncryptionOptions>(opt => opt.Enabled = false);
        }

        // Register services
        services.AddSingleton<IChangeLogger, ChangeLogger>();
        services.AddSingleton(options.SchemaManagerOptions);
        services.AddSingleton<ISchemaManager, PostgresSchemaManager>();
        services.AddSingleton<IMorphDataService, PostgresDataService>();
        services.AddSingleton<IWebhookManager, PostgresWebhookManager>();
        services.AddSingleton<IBulkOperationService, PostgresBulkOperationService>();

        // Register security services
        services.AddSingleton<IApiKeyService, ApiKeyService>();
        services.AddSingleton<ISecurityPolicyService, SecurityPolicyService>();
        services.AddSingleton<ISecurityContextAccessor, SecurityContextAccessor>();
        services.AddSingleton<IJwtService, JwtService>();

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
    /// Cache expiration time for schema mappings.
    /// </summary>
    public TimeSpan SchemaCacheExpiration { get; set; } = TimeSpan.FromMinutes(5);
}
