using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MorphDB.Mcp;

namespace MorphDB.Tests.Unit;

/// <summary>
/// The door is not opened on a service that enforces no authentication: the REST API
/// may be an open one by choice, but an open door that agents call by themselves is not a choice to make by omission.
/// </summary>
public class McpEnablingTests
{
    private static IConfiguration Configuration(bool enabled) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["MorphDB:Mcp:Enabled"] = enabled ? "true" : "false" }).Build();

    [Fact]
    public void Enabling_the_door_on_a_service_that_enforces_nothing_is_refused_at_start_up()
    {
        var act = () => new ServiceCollection().AddMorphDbMcp(Configuration(enabled: true), authenticationEnforced: false);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Security:MasterSecret*");
    }

    [Fact]
    public void A_door_that_is_off_asks_nothing_of_the_service()
    {
        var services = new ServiceCollection();

        services.AddMorphDbMcp(Configuration(enabled: false), authenticationEnforced: false);

        services.Should().NotContain(d => d.ServiceType.FullName!.StartsWith("ModelContextProtocol", StringComparison.Ordinal));
    }

    [Fact]
    public void An_enforcing_service_may_open_the_door()
    {
        var act = () => new ServiceCollection().AddMorphDbMcp(Configuration(enabled: true), authenticationEnforced: true);

        act.Should().NotThrow();
    }
}
