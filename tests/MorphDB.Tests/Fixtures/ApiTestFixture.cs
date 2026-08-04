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
using MorphDB.Core.Audit;
using MorphDB.Core.Models;
using MorphDB.Core.Security;
using MorphDB.Npgsql.Audit;
using MorphDB.Npgsql.Infrastructure;
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
    public Guid ProjectId { get; } = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        // Pre-provision the test project's project schema directly in the database.
        // This avoids the chicken-and-egg problem where the audit middleware tries
        // to log to a project's _audit_logs table before the project is created.
        await ProvisionTestProjectAsync();

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

                    // Schema and data services
                    services.RemoveAll<PostgresSchemaManager>();
                    services.RemoveAll<ISchemaManager>();
                    services.AddSingleton<ISchemaManager, PostgresSchemaManager>();

                    services.RemoveAll<IMorphDataService>();
                    services.AddSingleton<IMorphDataService, PostgresDataService>();

                    services.RemoveAll<IWebhookManager>();
                    services.AddSingleton<IWebhookManager, PostgresWebhookManager>();

                    services.RemoveAll<BulkOperationOptions>();
                    services.AddSingleton(new BulkOperationOptions());
                    services.RemoveAll<IBulkOperationService>();
                    services.AddSingleton<IBulkOperationService, PostgresBulkOperationService>();

                    // View services
                    services.RemoveAll<IViewMetadataRepository>();
                    services.AddSingleton<IViewMetadataRepository, ViewMetadataRepository>();

                    services.RemoveAll<IViewManager>();
                    services.AddSingleton<IViewManager, PostgresViewManager>();

                    // Project and schema layer services
                    services.RemoveAll<ISchemaNameResolver>();
                    services.AddSingleton<ISchemaNameResolver, PostgresSchemaNameResolver>();

                    services.RemoveAll<ISchemaLayerService>();
                    services.AddSingleton<ISchemaLayerService, PostgresSchemaLayerService>();

                    services.RemoveAll<IProjectRepository>();
                    services.AddSingleton<IProjectRepository, ProjectRepository>();

                    services.RemoveAll<IProjectService>();
                    services.AddSingleton<IProjectService, ProjectService>();

                    // Audit service with PII masking
                    services.RemoveAll<IPiiMaskingService>();
                    services.AddSingleton<IPiiMaskingService, PiiMaskingService>();
                    services.RemoveAll<IAuditService>();
                    services.AddSingleton<IAuditService, PostgresAuditService>();

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

                    // Add test authentication scheme
                });
            });

        Client = _factory.CreateClient();
        Client.DefaultRequestHeaders.Add("X-Project-Id", ProjectId.ToString());

        await Task.CompletedTask;
    }

    public HttpClient CreateClientWithProject(Guid projectId)
    {
        var client = _factory!.CreateClient();
        client.DefaultRequestHeaders.Add("X-Project-Id", projectId.ToString());
        return client;
    }

    /// <summary>
    /// Returns a factory identical to this one except that a master secret is injected, which is
    /// what turns secret enforcement on.
    /// <para>
    /// It layers onto the configured factory rather than standing up a second one, so the enforced
    /// server is the same server — same database, same service replacements — differing only in the
    /// one option under test. A hand-built second fixture would be free to drift from this one, and
    /// then it would be verifying itself rather than the production wiring.
    /// </para>
    /// </summary>
    public WebApplicationFactory<Program> WithMasterSecret(string masterSecret) =>
        _factory!.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<SecretOptions>();
                services.AddSingleton(new SecretOptions { MasterSecret = masterSecret });
            }));

    /// <summary>
    /// Gets the service provider of the configured (unenforced) server.
    /// </summary>
    public IServiceProvider Services => _factory!.Services;

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

    /// <summary>
    /// Pre-provisions the test project before the web application starts, avoiding the
    /// chicken-and-egg problem where audit middleware tries to log before tables exist.
    /// <para>
    /// It runs the production provisioning path — <see cref="ProjectService.CreateProjectAsync"/>,
    /// the same repository, naming and schema-layer code the API runs. An earlier version mirrored
    /// that path by hand (its own INSERT, its own <c>p_{id}_sys</c> convention, its own
    /// CREATE SCHEMA), which is the fixture-verifies-itself structure this suite has been burned
    /// by twice: change how provisioning works and the hand-copy keeps passing over a service
    /// that provisions differently.
    /// </para>
    /// </summary>
    private async Task ProvisionTestProjectAsync()
    {
        var shortId = ProjectId.ToString("N")[..8];

        var resolver = new PostgresSchemaNameResolver();
        var projectService = new ProjectService(
            new ProjectRepository(_postgresFixture.DataSource, resolver),
            new PostgresSchemaLayerService(
                _postgresFixture.DataSource,
                resolver,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<PostgresSchemaLayerService>.Instance),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ProjectService>.Instance);

        await projectService.CreateProjectAsync(new CreateProjectRequest
        {
            ProjectId = ProjectId,
            Name = $"Test Project {shortId}",
            Slug = $"test-project-{shortId}"
        });
    }

    public Task DisposeAsync()
    {
        Client?.Dispose();
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
