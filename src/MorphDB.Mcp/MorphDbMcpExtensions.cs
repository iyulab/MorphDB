using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace MorphDB.Mcp;

/// <summary>Turns the door on in a service that hosts it.</summary>
public static class MorphDbMcpExtensions
{
    /// <summary>
    /// Registers the door's settings, and — when <c>MorphDB:Mcp:Enabled</c> is set — its tools. The service registers
    /// its <see cref="IMorphReadPort"/> itself.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The service's configuration.</param>
    /// <param name="authenticationEnforced">
    /// Whether the service refuses requests that present no credential. Turning the door on while it does not is refused
    /// at start-up: agents call a door by themselves, and a service that enforces nothing would let any of them read.
    /// </param>
    /// <exception cref="InvalidOperationException">The door is enabled and the service enforces no authentication.</exception>
    public static IServiceCollection AddMorphDbMcp(this IServiceCollection services, IConfiguration configuration, bool authenticationEnforced)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection(MorphDbMcpOptions.SectionName);
        services.AddOptions<MorphDbMcpOptions>()
            .Bind(section)
            .Validate(o => o.MaxPageSize >= 1 && o.Path.StartsWith('/'), "MorphDB:Mcp:MaxPageSize must be at least 1 and MorphDB:Mcp:Path must start with '/'.")
            .ValidateOnStart();

        if (!section.GetValue<bool>(nameof(MorphDbMcpOptions.Enabled)))
        {
            return services;
        }

        if (!authenticationEnforced)
        {
            throw new InvalidOperationException(
                "MorphDB:Mcp:Enabled is set but the service enforces no authentication (Security:MasterSecret is not set). " +
                "The MCP door is not opened on a service that answers anyone: set the master secret, or leave the door off.");
        }

        services.AddMcpServer()
            .WithHttpTransport(http => http.Stateless = true)
            .WithTools<MorphDbMcpTools>();
        return services;
    }

    /// <summary>Maps the door at its path — and nothing at all when it is off.</summary>
    public static IEndpointRouteBuilder MapMorphDbMcp(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var options = endpoints.ServiceProvider.GetRequiredService<IOptions<MorphDbMcpOptions>>().Value;
        if (options.Enabled)
        {
            endpoints.MapMcp(options.Path);
        }

        return endpoints;
    }
}
