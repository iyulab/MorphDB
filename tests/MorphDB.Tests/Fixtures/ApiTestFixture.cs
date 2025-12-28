using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
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
                // Use UseSetting to override connection string at the highest priority level
                // This ensures it takes precedence over environment variables from CI
                builder.UseSetting("ConnectionStrings:MorphDB", _postgresFixture.ConnectionString);

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
                    // CRITICAL: Replace the NpgsqlDataSource singleton with one using the test connection string.
                    // The original DataSource was built at startup with the CI environment's connection string,
                    // but we need to use the Testcontainers PostgreSQL connection string.
                    services.RemoveAll<NpgsqlDataSource>();
                    var testDataSourceBuilder = new NpgsqlDataSourceBuilder(_postgresFixture.ConnectionString);
                    testDataSourceBuilder.EnableDynamicJson();
                    var testDataSource = testDataSourceBuilder.Build();
                    services.AddSingleton(testDataSource);

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
