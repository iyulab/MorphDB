using HotChocolate;
using Microsoft.Extensions.Logging;
using MorphDB.Core.Exceptions;
using MorphDB.Service.GraphQL;

namespace MorphDB.Tests.Unit;

/// <summary>
/// A GraphQL execution error is either the caller's — answered with its own words and code — or the server's,
/// which reaches the client without a cause. The server's own log has to hold what the client was not told.
/// </summary>
public class CallerErrorFilterTests
{
    private sealed class Capture : ILogger<CallerErrorFilter>
    {
        public List<(LogLevel Level, Exception? Exception)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, exception));
    }

    [Fact]
    public void An_error_the_server_does_not_explain_is_logged_with_its_exception()
    {
        var log = new Capture();
        var cause = new InvalidOperationException("boom");

        var answered = new CallerErrorFilter(log).OnError(ErrorBuilder.New().SetMessage("Unexpected Execution Error").SetException(cause).Build());

        answered.Code.Should().BeNull("the client is still told nothing about a server defect");
        log.Entries.Should().ContainSingle().Which.Should().Be((LogLevel.Error, (Exception?)cause));
    }

    [Fact]
    public void A_callers_mistake_is_answered_with_its_code_and_is_not_a_server_error_in_the_log()
    {
        var log = new Capture();

        var answered = new CallerErrorFilter(log).OnError(
            ErrorBuilder.New().SetMessage("x").SetException(new ValidationException("qty", "'ten' is not a value of the column's type (integer).")).Build());

        answered.Code.Should().Be("VALIDATION_ERROR");
        log.Entries.Should().BeEmpty();
    }
}
