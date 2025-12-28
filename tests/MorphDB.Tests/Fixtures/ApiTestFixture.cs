using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Security;
using MorphDB.Npgsql.Audit;
using MorphDB.Npgsql.Backup;
using MorphDB.Npgsql.Infrastructure;
using MorphDB.Npgsql.Organization;
using MorphDB.Npgsql.Repositories;
using MorphDB.Npgsql.Schema;
using MorphDB.Npgsql.Security;
using MorphDB.Npgsql.Services;
using Npgsql;

namespace MorphDB.Tests.Fixtures;

/// <summary>
/// Web application factory for API integration tests.
/// Uses the PostgreSQL container from PostgresFixture.
/// </summary>
public sealed class ApiTestFixture : IAsyncLifetime
{
    private readonly PostgresFixture _postgresFixture;
    private WebApplicationFactory<Program>? _factory;

    public ApiTestFixture(PostgresFixture postgresFixture)
    {
        _postgresFixture = postgresFixture;
    }

    public HttpClient Client { get; private set; } = null!;
    public Guid TenantId { get; } = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                // Override configuration with in-memory collection (highest priority)
                // This ensures our test connection string overrides CI environment variables
                builder.ConfigureAppConfiguration((context, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:MorphDB"] = _postgresFixture.ConnectionString
                    });
                });

                builder.ConfigureServices(services =>
                {
                    // Configure JSON options to avoid PipeWriter.UnflushedBytes issue in .NET 10
                    services.Configure<Microsoft.AspNetCore.Mvc.JsonOptions>(options =>
                    {
                        options.JsonSerializerOptions.WriteIndented = false;
                    });
                });

                builder.ConfigureTestServices(services =>
                {
                    // CRITICAL: Replace NpgsqlDataSource and ALL dependent singletons.
                    // The original services were registered during AddMorphDbNpgsql with the CI connection string.
                    // We must replace all singletons that directly or indirectly depend on NpgsqlDataSource.

                    // Step 1: Create new NpgsqlDataSource with test connection string
                    var testDataSourceBuilder = new NpgsqlDataSourceBuilder(_postgresFixture.ConnectionString);
                    testDataSourceBuilder.EnableDynamicJson();
                    var testDataSource = testDataSourceBuilder.Build();

                    // Step 2: Remove and re-register NpgsqlDataSource
                    services.RemoveAll<NpgsqlDataSource>();
                    services.AddSingleton(testDataSource);

                    // Step 3: Remove and re-register all services that directly depend on NpgsqlDataSource
                    // These services capture DataSource in their constructors, so they must be re-created

                    // Core repositories and services
                    services.RemoveAll<IMetadataRepository>();
                    services.AddSingleton<IMetadataRepository, MetadataRepository>();

                    services.RemoveAll<IAdvisoryLockManager>();
                    services.AddSingleton<IAdvisoryLockManager, PostgresAdvisoryLockManager>();

                    services.RemoveAll<IChangeLogger>();
                    services.AddSingleton<IChangeLogger, ChangeLogger>();

                    // Security services
                    services.RemoveAll<ISecurityPolicyService>();
                    services.AddSingleton<ISecurityPolicyService, SecurityPolicyService>();

                    services.RemoveAll<IApiKeyService>();
                    services.AddSingleton<IApiKeyService, ApiKeyService>();

                    // Schema and data services
                    services.RemoveAll<PostgresSchemaManager>();
                    services.RemoveAll<ISchemaManager>();
                    services.AddSingleton<ISchemaManager, PostgresSchemaManager>();

                    services.RemoveAll<IMorphDataService>();
                    services.AddSingleton<IMorphDataService, PostgresDataService>();

                    services.RemoveAll<IWebhookManager>();
                    services.AddSingleton<IWebhookManager, PostgresWebhookManager>();

                    services.RemoveAll<IBulkOperationService>();
                    services.AddSingleton<IBulkOperationService, PostgresBulkOperationService>();

                    // Project and schema layer services
                    services.RemoveAll<ISchemaNameResolver>();
                    services.AddSingleton<ISchemaNameResolver, PostgresSchemaNameResolver>();

                    services.RemoveAll<ISchemaLayerService>();
                    services.AddSingleton<ISchemaLayerService, PostgresSchemaLayerService>();

                    services.RemoveAll<IProjectRepository>();
                    services.AddSingleton<IProjectRepository, ProjectRepository>();

                    services.RemoveAll<IProjectService>();
                    services.AddSingleton<IProjectService, ProjectService>();

                    // Audit service
                    services.RemoveAll<IAuditService>();
                    services.AddSingleton<IAuditService, PostgresAuditService>();

                    // Organization repositories
                    services.RemoveAll<IOrganizationRepository>();
                    services.AddSingleton<IOrganizationRepository, OrganizationRepository>();

                    services.RemoveAll<IMembershipRepository>();
                    services.AddSingleton<IMembershipRepository, MembershipRepository>();

                    // SSO repository
                    services.RemoveAll<ISsoConfigurationRepository>();
                    services.AddSingleton<ISsoConfigurationRepository, SsoConfigurationRepository>();

                    // Backup repository
                    services.RemoveAll<IBackupRepository>();
                    services.AddSingleton<IBackupRepository, BackupRepository>();

                    // Remove background services that poll specific system tables
                    // These services start before the test fixture can initialize the schema
                    // Note: PostgresChangeListener is NOT removed because it's needed for realtime tests
                    var hostedServiceDescriptors = services
                        .Where(d => d.ServiceType == typeof(IHostedService))
                        .ToList();

                    foreach (var descriptor in hostedServiceDescriptors)
                    {
                        var implementationType = descriptor.ImplementationType?.Name ?? descriptor.ServiceType.Name;
                        // Only remove services that poll _morph_webhook_deliveries and _morph_export_jobs tables
                        if (implementationType.Contains("WebhookProcessor") ||
                            implementationType.Contains("BulkJobProcessor"))
                        {
                            services.Remove(descriptor);
                        }
                    }
                });
            });

        Client = _factory.CreateClient();
        Client.DefaultRequestHeaders.Add("X-Tenant-Id", TenantId.ToString());

        await Task.CompletedTask;
    }

    public HttpClient CreateClientWithTenant(Guid tenantId)
    {
        var client = _factory!.CreateClient();
        client.DefaultRequestHeaders.Add("X-Tenant-Id", tenantId.ToString());
        return client;
    }

    /// <summary>
    /// Creates an HttpMessageHandler for SignalR HubConnection tests.
    /// This handler routes requests through the test server.
    /// </summary>
    public HttpMessageHandler CreateHandler()
    {
        return _factory!.Server.CreateHandler();
    }

    /// <summary>
    /// Gets the base address of the test server.
    /// </summary>
    public Uri BaseAddress => _factory!.Server.BaseAddress;

    public Task DisposeAsync()
    {
        Client.Dispose();
        _factory?.Dispose();
        return Task.CompletedTask;
    }
}

/// <summary>
/// Collection fixture that combines PostgreSQL and API testing.
/// </summary>
public sealed class ApiIntegrationFixture : IAsyncLifetime
{
    public PostgresFixture Postgres { get; } = new();
    public ApiTestFixture Api { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await Postgres.InitializeAsync();
        Api = new ApiTestFixture(Postgres);
        await Api.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        await Api.DisposeAsync();
        await Postgres.DisposeAsync();
    }
}

[CollectionDefinition("API")]
public class ApiCollection : ICollectionFixture<ApiIntegrationFixture>
{
}
