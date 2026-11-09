using System.Net;
using System.Net.Http.Json;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using MorphDB.Core.Abstractions;
using MorphDB.Npgsql.Audit;
using MorphDB.Service.Models.Api;
using MorphDB.Tests.Fixtures;
using Npgsql;

namespace MorphDB.Tests.Integration.Api;

/// <summary>
/// The audit writer stores the entries of one project's batch in one transaction. What that buys is
/// visible on the failure side: an entry the database refuses must not leave the entries queued
/// before it stored — otherwise the log holds part of a batch while the drop counter reports all of
/// it lost, and a count of lost entries stops meaning what the documentation says it means.
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class AuditBatchAtomicityTests
{
    private readonly ApiIntegrationFixture _fixture;
    private readonly HttpClient _client;

    public AuditBatchAtomicityTests(ApiIntegrationFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.Api.Client;
    }

    [Fact]
    public async Task A_batch_the_database_refuses_midway_stores_none_of_it_and_counts_all_of_it()
    {
        var projectId = await CreateProjectAsync();
        await SettleAsync(projectId);
        var before = await CountAuditEntriesAsync(projectId);
        using var counts = new DropCounts();
        var failedBefore = counts.Of(AuditMetrics.WriteFailed);

        // PostgreSQL text cannot hold a NUL, so the middle entry is refused after the first was written.
        await LogAsync(projectId, "first", "middle\0refused", "last");

        await WaitUntilAsync(() => counts.Of(AuditMetrics.WriteFailed) - failedBefore >= 3);

        (await CountAuditEntriesAsync(projectId)).Should().Be(before,
            "the entry written before the refused one is rolled back with it");
        (counts.Of(AuditMetrics.WriteFailed) - failedBefore).Should().Be(3,
            "all three entries of the batch were lost, and the counter says so");
    }

    [Fact]
    public async Task A_batch_the_database_accepts_is_stored_whole()
    {
        var projectId = await CreateProjectAsync();
        await SettleAsync(projectId);
        var before = await CountAuditEntriesAsync(projectId);

        await LogAsync(projectId, "one", "two", "three");

        await WaitUntilAsync(async () => await CountAuditEntriesAsync(projectId) - before >= 3);
        (await CountAuditEntriesAsync(projectId)).Should().Be(before + 3);
    }

    [Fact]
    public async Task Every_field_of_a_stored_entry_lands_in_its_own_column()
    {
        var projectId = await CreateProjectAsync();
        await SettleAsync(projectId);
        var tag = Guid.NewGuid().ToString("N")[..12];
        var when = new DateTimeOffset(2026, 11, 9, 3, 4, 5, 678, TimeSpan.Zero);

        // One entry with every field set and one with none, in the same group — the columns are written
        // as arrays, so a field filed under the wrong array would show here and nowhere else.
        var audit = _fixture.Api.Services.GetRequiredService<IAuditService>();
        await audit.LogBatchAsync(
        [
            new AuditEvent
            {
                ProjectId = projectId,
                Category = AuditCategory.Data,
                Action = $"full-{tag}",
                Severity = AuditSeverity.Warning,
                ActorId = "actor-1",
                ActorType = "user",
                ActedBy = "agent-1",
                ResourceType = "table",
                ResourceId = "res-1",
                HttpMethod = "PATCH",
                RequestPath = "/api/data/things/1",
                StatusCode = 412,
                IpAddress = "203.0.113.9",
                UserAgent = "fidelity-test/1.0",
                DurationMs = 1234567890123,
                Metadata = new Dictionary<string, object?> { ["note"] = "kept", ["count"] = 3 },
                ErrorMessage = "precondition failed",
                Timestamp = when,
            },
            new AuditEvent { ProjectId = projectId, Action = $"bare-{tag}" },
        ], TestContext.Current.CancellationToken);

        await WaitUntilAsync(async () => await RowAsync(projectId, $"bare-{tag}") is not null);
        var full = (await RowAsync(projectId, $"full-{tag}"))!.Value;
        var bare = (await RowAsync(projectId, $"bare-{tag}"))!.Value;

        full.GetProperty("category").GetInt32().Should().Be((int)AuditCategory.Data);
        full.GetProperty("severity").GetInt32().Should().Be((int)AuditSeverity.Warning);
        full.GetProperty("actor_id").GetString().Should().Be("actor-1");
        full.GetProperty("actor_type").GetString().Should().Be("user");
        full.GetProperty("acted_by").GetString().Should().Be("agent-1");
        full.GetProperty("resource_type").GetString().Should().Be("table");
        full.GetProperty("resource_id").GetString().Should().Be("res-1");
        full.GetProperty("http_method").GetString().Should().Be("PATCH");
        full.GetProperty("request_path").GetString().Should().Be("/api/data/things/1");
        full.GetProperty("status_code").GetInt32().Should().Be(412);
        full.GetProperty("ip_address").GetString().Should().Be("203.0.113.9");
        full.GetProperty("user_agent").GetString().Should().Be("fidelity-test/1.0");
        full.GetProperty("duration_ms").GetInt64().Should().Be(1234567890123);
        full.GetProperty("metadata").GetProperty("note").GetString().Should().Be("kept");
        full.GetProperty("metadata").GetProperty("count").GetInt32().Should().Be(3);
        full.GetProperty("error_message").GetString().Should().Be("precondition failed");
        full.GetProperty("timestamp").GetDateTimeOffset().Should().Be(when);

        bare.GetProperty("category").GetInt32().Should().Be((int)AuditCategory.Auth, "the default category is 0");
        bare.GetProperty("severity").GetInt32().Should().Be((int)AuditSeverity.Info);
        foreach (var nullable in new[]
                 {
                     "actor_id", "actor_type", "acted_by", "resource_type", "resource_id", "http_method", "request_path",
                     "status_code", "ip_address", "user_agent", "duration_ms", "metadata", "error_message",
                 })
        {
            bare.GetProperty(nullable).ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null,
                $"an entry that never set {nullable} stores no value for it");
        }
    }

    private async Task LogAsync(Guid projectId, params string[] actions)
    {
        var audit = _fixture.Api.Services.GetRequiredService<IAuditService>();
        await audit.LogBatchAsync(
            actions.Select(a => new AuditEvent { ProjectId = projectId, Action = a }),
            TestContext.Current.CancellationToken);
    }

    private async Task<Guid> CreateProjectAsync()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/projects",
            new { Name = $"atom_{Guid.NewGuid():N}"[..28] },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created,
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return (await response.Content.ReadFromJsonAsync<ProjectApiResponse>(TestContext.Current.CancellationToken))!.Id;
    }

    /// <summary>Waits for the entries the project's own creation queued, so a count taken now is a baseline.</summary>
    private async Task SettleAsync(Guid projectId)
    {
        var last = -1;
        var stableSince = DateTime.UtcNow;
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            var now = await CountAuditEntriesAsync(projectId);
            if (now != last)
            {
                last = now;
                stableSince = DateTime.UtcNow;
            }
            else if (DateTime.UtcNow - stableSince > TimeSpan.FromMilliseconds(700))
            {
                return;
            }

            await Task.Delay(100, TestContext.Current.CancellationToken);
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!await condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }
    }

    private async Task<System.Text.Json.JsonElement?> RowAsync(Guid projectId, string action)
    {
        using var scope = _fixture.Api.Services.CreateScope();
        var names = await scope.ServiceProvider.GetRequiredService<IProjectRepository>()
            .GetSchemaNamesAsync(projectId, TestContext.Current.CancellationToken);
        await using var connection = new NpgsqlConnection(_fixture.Postgres.ConnectionString);
        var json = await connection.ExecuteScalarAsync<string?>(
            $"""SELECT to_jsonb(t)::text FROM "{names!.Value.SystemSchema}"."_audit_logs" t WHERE "action" = @action""",
            new { action });
        return json is null ? null : System.Text.Json.JsonDocument.Parse(json).RootElement.Clone();
    }

    private async Task<int> CountAuditEntriesAsync(Guid projectId)
    {
        using var scope = _fixture.Api.Services.CreateScope();
        var names = await scope.ServiceProvider.GetRequiredService<IProjectRepository>()
            .GetSchemaNamesAsync(projectId, TestContext.Current.CancellationToken);
        await using var connection = new NpgsqlConnection(_fixture.Postgres.ConnectionString);
        return await connection.ExecuteScalarAsync<int>(
            $"""SELECT count(*) FROM "{names!.Value.SystemSchema}"."_audit_logs" """);
    }
}
