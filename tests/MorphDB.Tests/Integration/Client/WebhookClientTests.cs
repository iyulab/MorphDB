using MorphDB.Client;
using MorphDB.Client.Models;
using MorphDB.Tests.Fixtures;

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
}
