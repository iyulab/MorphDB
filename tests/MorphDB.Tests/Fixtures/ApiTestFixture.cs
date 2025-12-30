using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Dapper;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Models;
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
        // Pre-provision the test tenant's project schema directly in the database.
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

                    // Add test authentication scheme
                    services.AddAuthentication(TestAuthHandler.SchemeName)
                        .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                            TestAuthHandler.SchemeName, _ => { });
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
    /// Creates an authenticated HttpClient for testing [Authorize] endpoints.
    /// </summary>
    public HttpClient CreateAuthenticatedClient()
    {
        var client = _factory!.CreateClient();
        client.DefaultRequestHeaders.Add("X-Tenant-Id", TenantId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Auth", "true");
        return client;
    }

    /// <summary>
    /// Creates an authenticated HttpClient with a specific tenant ID.
    /// </summary>
    public HttpClient CreateAuthenticatedClientWithTenant(Guid tenantId)
    {
        var client = _factory!.CreateClient();
        client.DefaultRequestHeaders.Add("X-Tenant-Id", tenantId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Auth", "true");
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

    /// <summary>
    /// Pre-provisions a project for the test tenant.
    /// This creates the project entry, system/data schemas, and all system tables
    /// before the web application starts, avoiding the chicken-and-egg problem
    /// where audit middleware tries to log before tables exist.
    /// </summary>
    private async Task ProvisionTestProjectAsync()
    {
        // Use the same schema naming convention as PostgresSchemaNameResolver
        var shortId = TenantId.ToString("N")[..8];
        var systemSchema = $"p_{shortId}_sys";
        var dataSchema = $"p_{shortId}_dat";
        var slug = $"test-project-{shortId}";

        await using var connection = new NpgsqlConnection(_postgresFixture.ConnectionString);
        await connection.OpenAsync();

        // Insert project record into global _morph_projects table
        await connection.ExecuteAsync(
            """
            INSERT INTO morphdb._morph_projects
                (project_id, name, slug, system_schema, data_schema, status, created_at, updated_at)
            VALUES
                (@ProjectId, @Name, @Slug, @SystemSchema, @DataSchema, 1, NOW(), NOW())
            ON CONFLICT (project_id) DO NOTHING
            """,
            new
            {
                ProjectId = TenantId,
                Name = $"Test Project {shortId}",
                Slug = slug,
                SystemSchema = systemSchema,
                DataSchema = dataSchema
            });

        // Create system schema
        await connection.ExecuteAsync(
            $"CREATE SCHEMA IF NOT EXISTS \"{systemSchema}\"");

        // Create data schema
        await connection.ExecuteAsync(
            $"CREATE SCHEMA IF NOT EXISTS \"{dataSchema}\"");

        // Create all system tables in the system schema using DdlBuilder
        var systemTablesDdl = MorphDB.Npgsql.Ddl.DdlBuilder.BuildSystemTablesDdl(systemSchema);
        await connection.ExecuteAsync(systemTablesDdl);

        // Enable uuid-ossp extension in data schema
        await connection.ExecuteAsync(
            $"CREATE EXTENSION IF NOT EXISTS \"uuid-ossp\" WITH SCHEMA \"{dataSchema}\"");
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

/// <summary>
/// Test authentication handler that bypasses real authentication.
/// All requests with the TestAuth header are authenticated as a test user.
/// </summary>
public sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "TestAuth";
    public const string TestUserId = "test-user-id";
    public const string TestUserEmail = "test@example.com";

    public TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // Check for X-Test-Auth header or Authorization header
        var hasTestAuth = Request.Headers.ContainsKey("X-Test-Auth") ||
                          Request.Headers.Authorization.FirstOrDefault()?.StartsWith("Bearer test-", StringComparison.OrdinalIgnoreCase) == true;

        if (!hasTestAuth)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        // Get tenant ID from header for context
        var tenantId = Guid.TryParse(Request.Headers["X-Tenant-Id"].FirstOrDefault(), out var parsedTenantId)
            ? parsedTenantId
            : Guid.Empty;

        var claims = new List<Claim>
        {
            new("sub", TestUserId),
            new("email", TestUserEmail),
            new(ClaimTypes.Name, "Test User"),
            new("tenant_id", tenantId.ToString()),
            new(ClaimTypes.Role, "admin")
        };

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
