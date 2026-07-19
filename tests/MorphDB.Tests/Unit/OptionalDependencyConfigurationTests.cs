using System.Text.Json;

namespace MorphDB.Tests.Unit;

/// <summary>
/// Keeps optional dependencies out of the shipped default configuration.
/// <para>
/// Redis is optional: the schema cache only registers when a connection string is present, and the
/// health check follows the same condition. A default value defeats both — the service then looks
/// configured for a Redis nobody deployed, so readiness probes fail against a dependency it is not
/// using and the cache errors on first use. Shipping <c>localhost:6379</c> as a default is exactly
/// what made <c>/health</c> report unhealthy in a container that had no Redis at all.
/// </para>
/// </summary>
public class OptionalDependencyConfigurationTests
{
    [Fact]
    public void The_default_configuration_does_not_point_optional_dependencies_at_localhost()
    {
        var settings = LoadDefaultAppSettings();

        settings.TryGetProperty("ConnectionStrings", out var connectionStrings)
            .Should().BeTrue("the service ships a default connection-string section");

        connectionStrings.TryGetProperty("Redis", out _)
            .Should().BeFalse("Redis is optional — a default value makes an absent cache look configured");
    }

    [Fact]
    public void The_default_configuration_still_declares_the_required_database()
    {
        // Guards the fix above from over-reaching: the database is not optional and must keep its default.
        var settings = LoadDefaultAppSettings();

        settings.GetProperty("ConnectionStrings").TryGetProperty("MorphDB", out var morphDb).Should().BeTrue();
        morphDb.GetString().Should().NotBeNullOrWhiteSpace();
    }

    private static JsonElement LoadDefaultAppSettings()
    {
        // The file ships next to the service assembly, which the test project references.
        var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        File.Exists(path).Should().BeTrue($"expected the service's default configuration at {path}");

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.Clone();
    }
}
