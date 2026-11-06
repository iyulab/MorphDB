using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MorphDB.Core.Abstractions;
using MorphDB.Service.Models.Api;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration.Api;

/// <summary>
/// What a webhook receiver is sent is the row as the table holds it: the delivery body carries the stored row,
/// so a receiver that compares its own copy by <c>_version</c> sees the version the table gave the change.
/// An insert is version 1 and each change that finds the row — an upsert, a single update — moves it one on.
/// <para>
/// The API test host does not run the sender (it polls a system table that is not ready when the host starts),
/// so the body is read where it waits to be sent: the queued delivery's payload, which is what the sender
/// posts verbatim.
/// </para>
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class WebhookDeliveryBodyContractTests
{
    private static readonly string[] ByCode = ["code"];

    private readonly ApiIntegrationFixture _fixture;
    private readonly HttpClient _client;

    public WebhookDeliveryBodyContractTests(ApiIntegrationFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.Api.Client;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>The next payload queued for this webhook that this test has not read yet.</summary>
    private async Task<JsonDocument> NextQueuedAsync(Guid webhookId, HashSet<Guid> seen)
    {
        var manager = _fixture.Api.Services.GetRequiredService<IWebhookManager>();
        var until = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < until)
        {
            var pending = await manager.GetPendingDeliveriesAsync(200, Ct);
            if (pending.FirstOrDefault(d => d.WebhookId == webhookId && !seen.Contains(d.DeliveryId)) is { } next)
            {
                seen.Add(next.DeliveryId);
                return JsonDocument.Parse(next.Payload.RootElement.GetRawText());
            }

            await Task.Delay(100, Ct);
        }

        throw new TimeoutException("No delivery was queued for the webhook in 30 seconds.");
    }

    private static long VersionOf(JsonDocument delivery)
    {
        // The body is { event, table, recordId, timestamp, data: { ... } }; property case is the service's to choose.
        var data = delivery.RootElement.EnumerateObject().First(p => string.Equals(p.Name, "data", StringComparison.OrdinalIgnoreCase)).Value;
        return data.EnumerateObject().First(p => p.Name == "_version").Value.GetInt64();
    }

    [Fact]
    public async Task A_delivery_carries_the_version_the_table_gave_the_change_whichever_door_made_it()
    {
        var table = $"hookver_{Guid.NewGuid():N}"[..30];
        (await _client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = table,
            Columns =
            [
                new CreateColumnApiRequest { Name = "code", Type = "text", Nullable = false, Unique = true },
                new CreateColumnApiRequest { Name = "label", Type = "text", Nullable = false },
            ],
        }, Ct)).EnsureSuccessStatusCode();
        var registered = await _client.PostAsJsonAsync("/api/webhooks", new CreateWebhookApiRequest
        {
            Name = $"hookver_{Guid.NewGuid():N}"[..30],
            Table = table,
            Url = "https://example.invalid/webhook",
            Events = ["insert", "update"],
        }, Ct);
        registered.EnsureSuccessStatusCode();
        var hookId = (await registered.Content.ReadFromJsonAsync<WebhookApiResponse>(Ct))!.Id;
        var seen = new HashSet<Guid>();

        async Task<(Guid Id, long Version)> StoredAsync()
        {
            using var doc = JsonDocument.Parse(await _client.GetStringAsync($"/api/data/{table}?filter=code:eq:k", Ct));
            var d = doc.RootElement.GetProperty("data")[0].GetProperty("data");
            return (d.GetProperty("_id").GetGuid(), d.GetProperty("_version").GetInt64());
        }

        var problems = new List<string>();

        async Task CheckAsync(string door, long expected)
        {
            using var delivered = await NextQueuedAsync(hookId, seen);
            var stored = await StoredAsync();
            if (VersionOf(delivered) != expected || stored.Version != expected)
                problems.Add($"{door}: delivered version {VersionOf(delivered)}, stored {stored.Version}, expected {expected}");
        }

        (await _client.PostAsJsonAsync($"/api/data/{table}", new { code = "k", label = "orig" }, Ct)).EnsureSuccessStatusCode();
        await CheckAsync("insert", 1);

        (await _client.PutAsJsonAsync($"/api/batch/data/{table}", new { data = new { code = "k", label = "upserted" }, keyColumns = ByCode }, Ct)).EnsureSuccessStatusCode();
        await CheckAsync("upsert that finds the row", 2);

        var id = (await StoredAsync()).Id;
        (await _client.PatchAsJsonAsync($"/api/data/{table}/{id}", new { label = "patched" }, Ct)).EnsureSuccessStatusCode();
        await CheckAsync("single update", 3);

        problems.Should().BeEmpty(string.Join("\n", problems));
    }
}
