using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace MorphDB.Service.Health;

/// <summary>
/// Answers healthy when the database the service is actually using answers a trivial query.
/// It reads the same <see cref="NpgsqlDataSource"/> every repository reads, so the probe follows
/// whatever that source is bound to — a test host that swaps the source swaps the probe with it,
/// and a check registered from a separate connection string could not drift from the real one.
/// A failure surfaces as the thrown exception: the health service records it as unhealthy and
/// carries the message into the report.
/// </summary>
internal sealed class PostgresHealthCheck(NpgsqlDataSource dataSource) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        await using var command = dataSource.CreateCommand("SELECT 1");
        await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return HealthCheckResult.Healthy();
    }
}
