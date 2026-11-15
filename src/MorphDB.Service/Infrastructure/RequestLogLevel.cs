using Serilog.Events;

namespace MorphDB.Service.Infrastructure;

/// <summary>
/// The level a request's log line is written at. The orchestrator and the image's own healthcheck ask
/// <c>/health</c> every few seconds, and a line per probe would make the log mostly probes -- so a probe that
/// answers is written at Verbose, below the level the service logs at. One that fails (or any request that
/// throws or answers 5xx) keeps its Error line: the probe that matters is the one that tells you something.
/// </summary>
internal static class RequestLogLevel
{
    internal static LogEventLevel For(HttpContext context, double elapsedMilliseconds, Exception? exception)
    {
        if (exception is not null || context.Response.StatusCode > 499)
        {
            return LogEventLevel.Error;
        }

        return context.Request.Path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase)
            ? LogEventLevel.Verbose
            : LogEventLevel.Information;
    }
}
