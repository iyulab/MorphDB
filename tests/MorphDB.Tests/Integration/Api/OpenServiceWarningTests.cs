using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using MorphDB.Tests.Fixtures;
using Serilog.Core;
using Serilog.Events;

namespace MorphDB.Tests.Integration.Api;

/// <summary>
/// A service that enforces no credential says so as it starts — an open service is a deployment choice, and one nobody made on
/// purpose belongs in the first lines of its log (the Formbase host does the same).
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public sealed class OpenServiceWarningTests(ApiIntegrationFixture fixture)
{
    private sealed class CapturingSink : ILogEventSink
    {
        public ConcurrentQueue<LogEvent> Events { get; } = new();

        public void Emit(LogEvent logEvent) => Events.Enqueue(logEvent);
    }

    [Fact]
    public async Task A_service_without_a_master_secret_warns_as_it_starts()
    {
        var sink = new CapturingSink();
        await using var factory = fixture.Api.Derive(services => services.AddSingleton<ILogEventSink>(sink));

        using var client = factory.CreateClient();

        sink.Events.Should().Contain(e => e.Level == LogEventLevel.Warning && e.RenderMessage(null).Contains("Security__MasterSecret", StringComparison.Ordinal));
    }
}
