using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MorphDB.Service.Middleware;

namespace MorphDB.Tests.Unit;

/// <summary>
/// Exercises the fail-safe contract directly: <see cref="TrustedProxyOptions"/> unconfigured
/// means <c>X-Forwarded-For</c> is never consulted, even when a request supplies one.
/// </summary>
public sealed class TrustedProxyExtensionsTests
{
    [Fact]
    public async Task With_no_trusted_proxies_configured_the_forwarded_header_is_ignored()
    {
        using var server = BuildServer(configureOptions: null);
        using var client = server.CreateClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.7");

        var response = await client.GetStringAsync("/");

        // TestServer's simulated connection never presents 203.0.113.7 as the peer — if the
        // header had been trusted, that is the value that would come back.
        response.Should().NotBe("203.0.113.7");
    }

    [Fact]
    public async Task With_the_test_peer_in_the_known_networks_allowlist_the_forwarded_header_is_trusted()
    {
        using var server = BuildServer(configureOptions: o =>
        {
            // TestServer's simulated remote peer is 0.0.0.1 — see Microsoft.AspNetCore.TestHost's
            // ConnectionContext defaults. Trusting the widest possible range keeps this test from
            // depending on that undocumented default matching exactly.
            o.KnownNetworks.Add("0.0.0.0/0");
        });
        using var client = server.CreateClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.7");

        var response = await client.GetStringAsync("/");

        response.Should().Be("203.0.113.7");
    }

    private static TestServer BuildServer(Action<TrustedProxyOptions>? configureOptions)
    {
        var hostBuilder = new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services =>
                {
                    if (configureOptions is not null)
                    {
                        services.Configure(configureOptions);
                    }
                    else
                    {
                        services.Configure<TrustedProxyOptions>(_ => { });
                    }
                });
                webHost.Configure(app =>
                {
                    app.UseConfiguredForwardedHeaders();
                    app.Run(context =>
                        context.Response.WriteAsync(context.Connection.RemoteIpAddress?.ToString() ?? "null"));
                });
            });

        var host = hostBuilder.Start();
        return host.GetTestServer();
    }
}
