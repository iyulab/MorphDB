using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using MorphDB.Tests.Fixtures;
using Serilog.Core;
using Serilog.Events;

namespace MorphDB.Tests.Integration.Api;

/// <summary>
/// The request log records the status the client received.
/// <para>
/// Regression. The request log sat inside the exception handler, so an error the handler answers with a
/// 4xx — a validation failure, a missing row, a refused secret — reached the log as an exception and was
/// written as <c>responded 500</c> at Error level with its stack. Every client mistake counted as a server
/// failure, and the real ones were lost among them.
/// </para>
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public sealed class RequestLogStatusTests(ApiIntegrationFixture fixture)
{
    private sealed class CapturingSink : ILogEventSink
    {
        public ConcurrentQueue<LogEvent> Events { get; } = new();

        public void Emit(LogEvent logEvent) => Events.Enqueue(logEvent);
    }

    [Fact]
    public async Task An_error_answered_with_a_4xx_is_logged_with_that_status_below_error_level()
    {
        var sink = new CapturingSink();
        await using var factory = fixture.Api.Derive(services => services.AddSingleton<ILogEventSink>(sink));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Project-Id", fixture.Api.ProjectId.ToString());

        // A table that does not exist: the service throws, and the exception handler answers 404.
        var path = $"/api/data/no_such_{Guid.NewGuid():N}"[..40];
        var response = await client.GetAsync(path, TestContext.Current.CancellationToken);
        response.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.BadRequest);

        var logged = sink.Events.Where(e =>
                e.Properties.TryGetValue("RequestPath", out var p) && p.ToString().Trim('"') == path
                && e.Properties.ContainsKey("StatusCode"))
            .ToList();

        var line = logged.Should().ContainSingle("one request, one request-log line").Subject;
        line.Properties["StatusCode"].ToString().Should().Be(((int)response.StatusCode).ToString(System.Globalization.CultureInfo.InvariantCulture),
            "the log records the status the client received");
        ((int)line.Level).Should().BeLessThan((int)LogEventLevel.Error, "a client mistake is not a server failure");
        line.Exception.Should().BeNull();
    }
}
