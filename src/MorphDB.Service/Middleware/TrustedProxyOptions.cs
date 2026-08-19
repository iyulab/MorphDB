using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace MorphDB.Service.Middleware;

/// <summary>
/// The allowlist of reverse proxies this service trusts to set <c>X-Forwarded-For</c>.
/// </summary>
/// <remarks>
/// Fail-safe by construction: when both lists are empty (the default — no configuration
/// present), the service does not wire up forwarded-header processing at all, so the header is
/// fully ignored and every client IP downstream is the immediate TCP peer. A deployment must
/// name its actual reverse proxy before the header is trusted; there is no "trust everyone"
/// state reachable by omission.
/// </remarks>
public sealed class TrustedProxyOptions
{
    /// <summary>
    /// Individual proxy IP addresses to trust (e.g. <c>"10.0.0.5"</c>).
    /// </summary>
    public List<string> KnownProxies { get; set; } = [];

    /// <summary>
    /// CIDR network ranges to trust (e.g. <c>"10.0.0.0/8"</c>).
    /// </summary>
    public List<string> KnownNetworks { get; set; } = [];
}

/// <summary>
/// Wires <c>X-Forwarded-For</c>/<c>X-Forwarded-Proto</c> processing from the configured
/// <see cref="TrustedProxyOptions"/> allowlist, or skips it entirely when unconfigured.
/// </summary>
public static class TrustedProxyExtensions
{
    /// <summary>
    /// Applies <see cref="ForwardedHeadersMiddleware"/> when at least one trusted proxy or
    /// network is configured. With no configuration, this is a no-op — <c>RemoteIpAddress</c>
    /// downstream stays the raw TCP peer and <c>X-Forwarded-For</c> is never consulted.
    /// </summary>
    public static IApplicationBuilder UseConfiguredForwardedHeaders(this IApplicationBuilder app)
    {
        var trustedProxies = app.ApplicationServices.GetRequiredService<IOptions<TrustedProxyOptions>>().Value;

        if (trustedProxies.KnownProxies.Count == 0 && trustedProxies.KnownNetworks.Count == 0)
        {
            return app;
        }

        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
        };

        // The framework default trusts loopback even with no configuration — clear it so an
        // empty allowlist means "trust nobody", not "trust localhost", matching this option's
        // fail-safe contract exactly.
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();

        foreach (var proxy in trustedProxies.KnownProxies)
        {
            options.KnownProxies.Add(IPAddress.Parse(proxy));
        }

        foreach (var network in trustedProxies.KnownNetworks)
        {
            var parts = network.Split('/', 2);
            options.KnownIPNetworks.Add(new System.Net.IPNetwork(IPAddress.Parse(parts[0]), int.Parse(parts[1], CultureInfo.InvariantCulture)));
        }

        return app.UseForwardedHeaders(options);
    }
}
