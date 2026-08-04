using System.Net;
using System.Net.Http.Json;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using MorphDB.Core.Abstractions;
using MorphDB.Service.Models.Api;
using MorphDB.Tests.Fixtures;
using Npgsql;

namespace MorphDB.Tests.Integration.Api;

/// <summary>
/// A retention window that nothing applies is a setting the caller can read back and still be
/// wrong about. These hold the three claims the setting makes: entries past the window are
/// removed, a project without a window keeps everything, and the window survives the round trip
/// through the settings column and the wire.
/// <para>
/// Retention is exercised through <see cref="IAuditService"/> directly rather than by waiting for
/// the background sweep — the sweep's schedule is an operational choice, while what it applies is
/// the contract.
/// </para>
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class AuditRetentionContractTests
{
    private readonly ApiIntegrationFixture _fixture;
    private readonly HttpClient _client;

    public AuditRetentionContractTests(ApiIntegrationFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.Api.Client;
    }

    [Fact]
    public async Task Entries_past_the_window_are_removed_and_recent_ones_are_kept()
    {
        var projectId = await CreateProjectAsync(retentionDays: 30);
        await SeedAuditEntryAsync(projectId, age: TimeSpan.FromDays(45));
        await SeedAuditEntryAsync(projectId, age: TimeSpan.FromDays(5));

        var removed = await ApplyRetentionAsync(projectId);

        removed.Should().Be(1, "only the entry past the 30-day window is expired");
        (await CountAuditEntriesAsync(projectId)).Should().Be(1,
            "the entry inside the window is history the project asked to keep");
    }

    [Fact]
    public async Task A_project_without_a_window_keeps_everything()
    {
        var projectId = await CreateProjectAsync(retentionDays: null);
        await SeedAuditEntryAsync(projectId, age: TimeSpan.FromDays(4000));

        var removed = await ApplyRetentionAsync(projectId);

        removed.Should().Be(0,
            "introducing a retention setting must not start deleting history from projects that " +
            "never asked for a window");
        (await CountAuditEntriesAsync(projectId)).Should().Be(1);
    }

    [Fact]
    public async Task The_window_survives_the_round_trip_through_the_wire()
    {
        var projectId = await CreateProjectAsync(retentionDays: 14);

        var response = await _client.GetAsync($"/api/projects/{projectId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var project = await response.Content.ReadFromJsonAsync<ProjectApiResponse>();
        project!.Settings!.AuditLogRetentionDays.Should().Be(14,
            "a setting the caller cannot read back is one they cannot verify was applied");
    }

    private async Task<Guid> CreateProjectAsync(int? retentionDays)
    {
        var response = await _client.PostAsJsonAsync("/api/projects", new
        {
            Name = $"ret_{Guid.NewGuid():N}"[..28],
            Settings = new ProjectSettingsApiModel { AuditLogRetentionDays = retentionDays },
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created,
            await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ProjectApiResponse>())!.Id;
    }

    private Task<int> ApplyRetentionAsync(Guid projectId)
    {
        using var scope = _fixture.Api.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IAuditService>()
            .ApplyRetentionAsync(projectId);
    }

    /// <summary>
    /// Writes an entry straight to the table with a backdated timestamp. The audit path queues
    /// writes and stamps them itself, so a test that went through it could only ever produce
    /// entries inside any window worth testing.
    /// </summary>
    private async Task SeedAuditEntryAsync(Guid projectId, TimeSpan age)
    {
        var schema = SystemSchemaFor(projectId);
        await using var connection = new NpgsqlConnection(_fixture.Postgres.ConnectionString);
        await connection.ExecuteAsync(
            $"""
            INSERT INTO "{schema}"."_audit_logs" ("id", "category", "action", "severity", "timestamp")
            VALUES (@id, 1, 'seeded', 1, @timestamp)
            """,
            new
            {
                id = Guid.NewGuid(),
                timestamp = DateTimeOffset.UtcNow - age,
            });
    }

    private async Task<int> CountAuditEntriesAsync(Guid projectId)
    {
        var schema = SystemSchemaFor(projectId);
        await using var connection = new NpgsqlConnection(_fixture.Postgres.ConnectionString);
        return await connection.ExecuteScalarAsync<int>(
            $"""SELECT count(*) FROM "{schema}"."_audit_logs" """);
    }

    private string SystemSchemaFor(Guid projectId)
    {
        using var scope = _fixture.Api.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<ISchemaNameResolver>()
            .GetSchemaNames(projectId).SystemSchema;
    }
}
