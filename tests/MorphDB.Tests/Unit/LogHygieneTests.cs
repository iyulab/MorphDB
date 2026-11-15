using System.Text.Json;
using Microsoft.AspNetCore.Http;
using MorphDB.Service;
using MorphDB.Service.Infrastructure;
using MorphDB.Tests.Fixtures;
using Serilog.Events;

namespace MorphDB.Tests.Unit;

/// <summary>
/// What the published image writes to its log. Three defects came from running it as its README says: every line
/// was written twice (a console sink in appsettings.json beside the one Program.cs adds), the healthcheck's own
/// <c>/health</c> request was a line every few seconds, and the image set both ASPNETCORE_URLS and the base image's
/// ASPNETCORE_HTTP_PORTS, so every start warned that it was overriding one with the other.
/// </summary>
public class LogHygieneTests
{
    [Fact]
    public void The_default_configuration_declares_no_console_sink_because_the_program_adds_the_one()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));

        var sinks = document.RootElement.GetProperty("Serilog").TryGetProperty("WriteTo", out var writeTo)
            ? writeTo.EnumerateArray().Select(s => s.GetProperty("Name").GetString()).ToList()
            : [];

        sinks.Should().NotContain("Console", "Program.cs writes to the console itself, so a second declaration writes every line twice");
    }

    [Theory]
    [InlineData("/health", 200, null, LogEventLevel.Verbose)]
    [InlineData("/health/ready", 200, null, LogEventLevel.Verbose)]
    [InlineData("/HEALTH", 200, null, LogEventLevel.Verbose)]
    [InlineData("/health", 503, null, LogEventLevel.Error)]
    [InlineData("/health", 200, "boom", LogEventLevel.Error)]
    [InlineData("/api/projects", 200, null, LogEventLevel.Information)]
    [InlineData("/api/projects", 404, null, LogEventLevel.Information)]
    [InlineData("/api/projects", 500, null, LogEventLevel.Error)]
    [InlineData("/healthy-projects", 200, null, LogEventLevel.Information)]
    public void A_probe_that_answers_is_not_logged_at_the_service_level_and_everything_else_keeps_its_level(
        string path, int status, string? exception, LogEventLevel expected)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Response.StatusCode = status;

        RequestLogLevel.For(context, 1.0, exception is null ? null : new InvalidOperationException(exception)).Should().Be(expected);
    }

    [Theory]
    [InlineData(null, null, 8080)]
    [InlineData("http://+:5400", null, 5400)]
    [InlineData("http://+:8081;https://+:8443", null, 8081)]
    [InlineData("https://+:8443", null, 8080)]
    [InlineData(null, "9090", 9090)]
    [InlineData(null, "8081;8082", 8081)]
    [InlineData(null, "abc", 8080)]
    [InlineData("http://+:5400", "9090", 5400)]
    public void The_healthcheck_follows_the_port_the_host_was_told_to_bind(string? urls, string? httpPorts, int expected)
    {
        var uri = HealthProbe.ProbeUri(name => name switch
        {
            "ASPNETCORE_URLS" => urls,
            "ASPNETCORE_HTTP_PORTS" => httpPorts,
            _ => null,
        });

        uri.Should().Be(new Uri($"http://localhost:{expected}/health"), "ASP.NET Core reads URLS first, then HTTP_PORTS, so the probe does too");
    }

    [Fact]
    public void The_image_names_its_port_by_one_setting_the_healthcheck_reads()
    {
        var dockerfile = ConstraintBoundaryDoc.ReadRepoFile("src/MorphDB.Service/Dockerfile");

        dockerfile.Split('\n').Select(l => l.Trim()).Where(l => l.StartsWith("ENV ", StringComparison.Ordinal))
            .Should().NotContain(l => l.Contains("ASPNETCORE_URLS", StringComparison.Ordinal),
                "the warning at start is the host saying the image set the port twice");
        dockerfile.Should().Contain("ENV ASPNETCORE_HTTP_PORTS=8080");
    }
}
