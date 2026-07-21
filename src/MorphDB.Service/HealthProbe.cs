namespace MorphDB.Service;

/// <summary>
/// The container HEALTHCHECK runs the service's own assembly with <see cref="Argument"/> instead of
/// shelling out to wget or curl, because the runtime image ships neither. A healthcheck that depends
/// on the base image's package set starts lying the day that package set changes — and a lying
/// healthcheck is worse than none, because orchestrators wait on it.
/// </summary>
internal static class HealthProbe
{
    internal const string Argument = "--health-check";

    /// <summary>Returns 0 when the service answers healthy, 1 otherwise — the exit codes Docker reads.</summary>
    internal static async Task<int> RunAsync()
    {
        var uri = ProbeUri();

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            using var response = await http.GetAsync(uri).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                return 0;
            }

            await Console.Error.WriteLineAsync(
                $"Health probe: {uri} returned {(int)response.StatusCode}.").ConfigureAwait(false);
            return 1;
        }
        catch (HttpRequestException ex)
        {
            await Console.Error.WriteLineAsync($"Health probe: {uri} is unreachable — {ex.Message}").ConfigureAwait(false);
            return 1;
        }
        catch (TaskCanceledException)
        {
            await Console.Error.WriteLineAsync($"Health probe: {uri} did not answer within 5s.").ConfigureAwait(false);
            return 1;
        }
    }

    /// <summary>
    /// Follows the port the host was told to bind, so the probe survives a consumer overriding
    /// ASPNETCORE_URLS. The host part is not reusable: a binding is a listening pattern
    /// (<c>http://+:8080</c>), not an address anything can dial.
    /// </summary>
    private static Uri ProbeUri()
    {
        var bound = Environment.GetEnvironmentVariable("ASPNETCORE_URLS")
            ?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(url => url.StartsWith("http://", StringComparison.OrdinalIgnoreCase));

        var port = bound is not null
            && Uri.TryCreate(
                bound.Replace("+", "localhost", StringComparison.Ordinal)
                     .Replace("*", "localhost", StringComparison.Ordinal),
                UriKind.Absolute,
                out var parsed)
            ? parsed.Port
            : 8080;

        return new Uri($"http://localhost:{port}/health");
    }
}
