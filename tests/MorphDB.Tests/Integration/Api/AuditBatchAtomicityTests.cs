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
