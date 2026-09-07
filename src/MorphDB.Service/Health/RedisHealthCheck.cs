using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace MorphDB.Service.Health;

/// <summary>
/// Pings Redis over the multiplexer the schema cache already holds open. It is registered only
/// when a Redis connection string is configured, because the cache itself is — see the health
/// check registration in <c>Program.cs</c> for why an unconfigured cache must not be probed.
/// The round-trip time is reported as data so a slow cache is visible before it is a dead one.
/// </summary>
internal sealed class RedisHealthCheck(IConnectionMultiplexer multiplexer) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var latency = await multiplexer.GetDatabase().PingAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        return HealthCheckResult.Healthy(data: new Dictionary<string, object>
        {
            ["latencyMs"] = latency.TotalMilliseconds
        });
    }
}
