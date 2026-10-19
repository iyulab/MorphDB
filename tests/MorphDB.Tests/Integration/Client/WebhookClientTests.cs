using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MorphDB.Client;
using MorphDB.Client.Models;
using MorphDB.Tests.Fixtures;
using CoreAbstractions = MorphDB.Core.Abstractions;
using CoreModels = MorphDB.Core.Models;

namespace MorphDB.Tests.Integration.Client;

/// <summary>
/// The client's webhooks over the real service. The client's models used to carry their own field
/// names — <c>tableName</c> where the service reads <c>table</c>, <c>webhookId</c> where it writes
/// <c>id</c> — so no webhook could be created through it, and the listing filter by table was ignored.
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class WebhookClientTests(ApiIntegrationFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // Never resolves: a delivery is attempted and fails without reaching anything.
    private const string Nowhere = "https://webhook.invalid/hook";

    private MorphDBClient NewClient()
    {
        var http = new HttpClient(fixture.Api.CreateHandler()) { BaseAddress = fixture.Api.BaseAddress };
        var client = new MorphDBClient(http);
        client.SetProjectId(fixture.Api.ProjectId);
        return client;
    }

    private static async Task<string> NewTableAsync(MorphDBClient client)
    {
        var table = await client.Schema.CreateTableAsync(new CreateTableRequest
        {
            Name = $"wh_{Guid.NewGuid():N}"[..24],
            Columns = [new CreateColumnRequest { Name = "status", Type = "text" }],
        }, Ct);
        return table.Name;
    }

    [Fact]
    public async Task A_webhook_is_created_read_listed_by_table_and_deleted()
    {
        await using var client = NewClient();
        var table = await NewTableAsync(client);
        var other = await NewTableAsync(client);

        var created = await client.Webhooks.CreateAsync(new CreateWebhookRequest
        {
            Name = "orders done",
            Table = table,
            Url = Nowhere,
            Events = ["insert", "update"],
            Filter = new Dictionary<string, object?> { ["status"] = "done" },
            Headers = new Dictionary<string, string> { ["X-Origin"] = "test" },
        }, Ct);
        await client.Webhooks.CreateAsync(new CreateWebhookRequest { Name = "other", Table = other, Url = Nowhere }, Ct);

        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.Equal(table, created.Table);
        Assert.False(string.IsNullOrEmpty(created.Secret)); // returned once, at creation
        Assert.Equal(["insert", "update"], created.Events);
        Assert.Equal("done", created.Filter!["status"]?.ToString());
        Assert.Equal("test", created.Headers!["X-Origin"]);

        var read = await client.Webhooks.GetByIdAsync(created.Id, Ct);
        Assert.NotNull(read);
        Assert.Equal("orders done", read.Name);
        Assert.Null(read.Secret);

        Assert.Contains(await client.Webhooks.GetAllAsync(Ct), w => w.Id == created.Id);
        var byTable = await client.Webhooks.GetByTableAsync(table, Ct);
        Assert.Equal([created.Id], byTable.Select(w => w.Id));

        await client.Webhooks.DeleteAsync(created.Id, Ct);
        Assert.Null(await client.Webhooks.GetByIdAsync(created.Id, Ct));
    }

    [Fact]
    public async Task A_change_on_the_table_shows_up_among_the_webhooks_deliveries()
    {
        await using var client = NewClient();
        var table = await NewTableAsync(client);
        var webhook = await client.Webhooks.CreateAsync(new CreateWebhookRequest
        {
            Name = "every insert",
            Table = table,
            Url = Nowhere,
            Events = ["insert"],
        }, Ct);

        Assert.Empty(await client.Webhooks.GetDeliveriesAsync(webhook.Id, cancellationToken: Ct));

        var row = await client.Data.InsertAsync(table, new Dictionary<string, object?> { ["status"] = "new" }, Ct);

        IReadOnlyList<WebhookDelivery> deliveries = [];
        for (var i = 0; i < 50 && deliveries.Count == 0; i++)
        {
            await Task.Delay(200, Ct);
            deliveries = await client.Webhooks.GetDeliveriesAsync(webhook.Id, cancellationToken: Ct);
        }

        await Task.Delay(1500, Ct); // let every listener that would queue one do so
        deliveries = await client.Webhooks.GetDeliveriesAsync(webhook.Id, cancellationToken: Ct);
        var delivery = Assert.Single(deliveries);
        Assert.NotEqual(Guid.Empty, delivery.Id);
        Assert.Equal(row.Id, delivery.RecordId);
        Assert.Equal("insert", delivery.Event, ignoreCase: true);
        Assert.False(string.IsNullOrEmpty(delivery.Status));
    }

    [Fact]
    public async Task A_webhook_is_changed_and_its_secret_replaced()
    {
        await using var client = NewClient();
        var table = await NewTableAsync(client);
        var created = await client.Webhooks.CreateAsync(new CreateWebhookRequest { Name = "to change", Table = table, Url = Nowhere }, Ct);

        var changed = await client.Webhooks.UpdateAsync(created.Id, new UpdateWebhookRequest
        {
            Url = "https://webhook.invalid/other",
            Events = ["delete"],
            IsActive = false,
        }, Ct);
        Assert.Equal("https://webhook.invalid/other", changed.Url);
        Assert.Equal(["delete"], changed.Events);
        Assert.False(changed.IsActive);
        Assert.Equal("to change", (await client.Webhooks.GetByIdAsync(created.Id, Ct))!.Name);

        var secret = await client.Webhooks.RegenerateSecretAsync(created.Id, Ct);
        Assert.False(string.IsNullOrEmpty(secret));
        Assert.NotEqual(created.Secret, secret);
    }

    [Fact]
    public async Task Dead_letters_are_listed_counted_replayed_resolved_and_archived()
    {
        await using var client = NewClient();
        var table = await NewTableAsync(client);
        var webhook = await client.Webhooks.CreateAsync(new CreateWebhookRequest { Name = "dead", Table = table, Url = Nowhere }, Ct);

        // Three deliveries that gave up, put where the delivery processor puts them.
        var manager = fixture.Api.Services.GetRequiredService<CoreAbstractions.IWebhookManager>();
        var dead = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            var delivery = await manager.CreateDeliveryAsync(new CoreAbstractions.CreateDeliveryRequest
            {
                WebhookId = webhook.Id,
                Event = CoreModels.WebhookEvent.Insert,
                Payload = JsonDocument.Parse("{}"),
            }, Ct);
            dead.Add((await manager.MoveToDlqAsync(new CoreAbstractions.MoveToDlqRequest
            {
                DeliveryId = delivery!.DeliveryId,
                Reason = CoreModels.DlqReason.MaxRetriesExceeded,
            }, Ct)).DlqId);
        }

        var listed = await client.Webhooks.GetDeadLettersAsync(webhook.Id, WebhookDeadLetterStatus.PendingReview, cancellationToken: Ct);
        Assert.Equal(dead.Order(), listed.Select(d => d.DlqId).Order());
        Assert.All(listed, d => Assert.Equal("MaxRetriesExceeded", d.Reason));
        Assert.Null(await client.Webhooks.GetDeadLetterAsync(Guid.NewGuid(), Ct));

        var stats = await client.Webhooks.GetDeadLetterStatisticsAsync(Ct);
        Assert.Equal(3, stats.ByWebhook[webhook.Id]);
        Assert.True(stats.PendingReviewCount >= 3);

        var replayed = await client.Webhooks.ReplayDeadLetterAsync(dead[0], Ct);
        Assert.NotEqual(Guid.Empty, replayed.Id);
        Assert.Equal("Replayed", (await client.Webhooks.GetDeadLetterAsync(dead[0], Ct))!.Status);

        await client.Webhooks.ResolveDeadLetterAsync(dead[1], "receiver was down; handled by hand", cancellationToken: Ct);
        var resolved = await client.Webhooks.GetDeadLetterAsync(dead[1], Ct);
        Assert.Equal("Resolved", resolved!.Status);
        Assert.Equal("receiver was down; handled by hand", resolved.ResolutionNotes);

        Assert.Equal(2, await client.Webhooks.ArchiveDeadLettersAsync(webhook.Id, cancellationToken: Ct)); // resolved + pending
        Assert.Equal("Archived", (await client.Webhooks.GetDeadLetterAsync(dead[2], Ct))!.Status);
        Assert.Empty(await client.Webhooks.GetDeadLettersAsync(webhook.Id, WebhookDeadLetterStatus.PendingReview, cancellationToken: Ct));
    }
}
