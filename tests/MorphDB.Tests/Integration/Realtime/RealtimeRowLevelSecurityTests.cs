using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.SignalR.Client;
using MorphDB.Service.Controllers;
using MorphDB.Service.Models.Api;
using MorphDB.Service.Realtime;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration.Realtime;

/// <summary>
/// A change is sent to each subscriber that may read the row, as that subscriber, and to nobody else.
/// The subscribers come from a host that enforces secrets — that is where one project has callers that
/// differ: two issued secrets under a policy that shows each its own rows, and the master secret, which
/// bypasses row-level security.
/// </summary>
[Collection("API")]
[Trait("Category", "RealtimeIntegration")]
public sealed class RealtimeRowLevelSecurityTests(ApiIntegrationFixture fixture)
{
    private const string MasterSecret = "mdb_test_master_secret_value";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class Subscriber(HubConnection connection) : IAsyncDisposable
    {
        public HubConnection Connection { get; } = connection;

        public ConcurrentQueue<(string Kind, string? Name, Guid? RecordId)> Heard { get; } = new();

        public ValueTask DisposeAsync() => Connection.DisposeAsync();
    }

    private HttpClient Enforced(string secret)
    {
        var client = fixture.Api.WithMasterSecret(MasterSecret).CreateClient();
        client.DefaultRequestHeaders.Add("X-Project-Id", fixture.Api.ProjectId.ToString());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secret);
        return client;
    }

    private async Task<Subscriber> SubscribeAsync(string secret, string table)
    {
        var host = fixture.Api.WithMasterSecret(MasterSecret);
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(host.Server.BaseAddress, "hubs/morph"), options =>
            {
                options.Headers.Add("X-Project-Id", fixture.Api.ProjectId.ToString());
                options.Headers.Add("Authorization", $"Bearer {secret}");
                options.HttpMessageHandlerFactory = _ => host.Server.CreateHandler();
            })
            .Build();
        var subscriber = new Subscriber(connection);
        connection.On<RecordChangedMessage>("RecordCreated", m => subscriber.Heard.Enqueue(("created", m.Data.TryGetValue("name", out var n) ? n?.ToString() : null, m.RecordId)));
        connection.On<RecordChangedMessage>("RecordUpdated", m => subscriber.Heard.Enqueue(("updated", m.Data.TryGetValue("name", out var n) ? n?.ToString() : null, m.RecordId)));
        connection.On<RecordDeletedMessage>("RecordDeleted", m => subscriber.Heard.Enqueue(("deleted", null, m.RecordId)));
        await connection.StartAsync(Ct);
        await connection.InvokeAsync("Subscribe", table, Ct);
        return subscriber;
    }

    private static async Task<IssuedSecretResponse> IssueAsync(HttpClient master, string label)
    {
        var response = await master.PostAsJsonAsync("/api/security/secrets", new IssueSecretApiRequest { Name = $"{label}-{Guid.NewGuid():N}", Role = "writer" }, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<IssuedSecretResponse>(Ct))!;
    }

    private static async Task<Guid> InsertAsync(HttpClient client, string table, string name, string owner)
    {
        var response = await client.PostAsJsonAsync($"/api/data/{table}", new Dictionary<string, object?> { ["name"] = name, ["owner"] = owner }, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<DataRecordResponse>(Ct))!.Id;
    }

    private static async Task UntilAsync(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(100, Ct);
        }

        condition().Should().BeTrue(what);
    }

    [Fact]
    public async Task A_change_reaches_the_subscribers_that_may_read_the_row_with_its_contents_and_no_one_else()
    {
        var master = Enforced(MasterSecret);
        var a = await IssueAsync(master, "a");
        var b = await IssueAsync(master, "b");

        var table = $"rt_rls_{Guid.NewGuid():N}"[..30];
        (await master.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = table,
            Columns =
            [
                new CreateColumnApiRequest { Name = "name", Type = "text", Nullable = false },
                new CreateColumnApiRequest { Name = "owner", Type = "text", Nullable = true },
            ],
        }, Ct)).StatusCode.Should().Be(HttpStatusCode.Created);
        (await master.PostAsJsonAsync("/api/security/policies", new { name = "own_rows", tableName = table, policyType = "Select", expression = "owner = {{user_id}}" }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        await using var asA = await SubscribeAsync(a.Secret, table);
        await using var asB = await SubscribeAsync(b.Secret, table);
        await using var asMaster = await SubscribeAsync(MasterSecret, table);

        // A's row, then B's: changes are sent in order, so once B hears its own row, it would already
        // have heard A's had it been sent to B.
        var aRow = await InsertAsync(master, table, "a-row", a.SecretId.ToString());
        var bRow = await InsertAsync(master, table, "b-row", b.SecretId.ToString());

        await UntilAsync(() => asB.Heard.Any(h => h.RecordId == bRow), "B hears its own row");
        await UntilAsync(() => asA.Heard.Any(h => h.RecordId == aRow), "A hears its own row");
        await UntilAsync(() => asMaster.Heard.Count(h => h.Kind == "created") == 2, "the master secret hears both");

        asA.Heard.Should().ContainSingle(h => h.Kind == "created").Which.Name.Should().Be("a-row");
        asB.Heard.Should().ContainSingle(h => h.Kind == "created").Which.Name.Should().Be("b-row");
        asMaster.Heard.Where(h => h.Kind == "created").Select(h => h.Name).Should().BeEquivalentTo(["a-row", "b-row"]);

        // A deletion has no row left to check against a policy: every subscriber hears its id alone.
        (await master.DeleteAsync($"/api/data/{table}/{aRow}", Ct)).IsSuccessStatusCode.Should().BeTrue();
        await UntilAsync(() => asB.Heard.Any(h => h.Kind == "deleted" && h.RecordId == aRow), "B hears the deletion's id");
    }
}
