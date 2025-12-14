using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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
                    // Override output formatter for test compatibility
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
