using System.Collections.Concurrent;
using System.Diagnostics;
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
/// A change is read back once per distinct caller among its table's subscribers, so a table many
/// callers subscribe to takes a while to deliver one change. Changes to another table do not wait for
/// it: the order a subscriber is promised is per table.
/// </summary>
[Collection("API")]
[Trait("Category", "RealtimeIntegration")]
public sealed class RealtimeTableQueueTests(ApiIntegrationFixture fixture)
{
    private const string MasterSecret = "mdb_test_master_secret_value";
    private const int BusyCallers = 60;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Enforced(string secret)
    {
        var client = fixture.Api.WithMasterSecret(MasterSecret).CreateClient();
        client.DefaultRequestHeaders.Add("X-Project-Id", fixture.Api.ProjectId.ToString());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secret);
        return client;
    }

    private async Task<(HubConnection Connection, ConcurrentDictionary<Guid, long> Heard)> SubscribeAsync(string secret, string table)
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
        var heard = new ConcurrentDictionary<Guid, long>();
        connection.On<RecordChangedMessage>("RecordCreated", m => heard.TryAdd(m.RecordId!.Value, Stopwatch.GetTimestamp()));
        await connection.StartAsync(Ct);
        await connection.InvokeAsync("Subscribe", table, Ct);
        return (connection, heard);
    }

    private static async Task<string> TableAsync(HttpClient master)
    {
        var table = $"rt_queue_{Guid.NewGuid():N}"[..30];
        (await master.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = table,
            Columns = [new CreateColumnApiRequest { Name = "name", Type = "text", Nullable = false }],
        }, Ct)).StatusCode.Should().Be(HttpStatusCode.Created);
        return table;
    }

    private static async Task<Guid> InsertAsync(HttpClient client, string table)
    {
        var response = await client.PostAsJsonAsync($"/api/data/{table}", new Dictionary<string, object?> { ["name"] = "x" }, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<DataRecordResponse>(Ct))!.Id;
    }

    private static async Task UntilAsync(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(5, Ct);
        }

        condition().Should().BeTrue(what);
    }

    [Fact]
    public async Task A_change_to_one_table_does_not_wait_behind_a_table_with_many_callers()
    {
        var master = Enforced(MasterSecret);
        var busy = await TableAsync(master);
        var quiet = await TableAsync(master);

        var busySubscribers = new List<(HubConnection Connection, ConcurrentDictionary<Guid, long> Heard)>();
        for (var i = 0; i < BusyCallers; i++)
        {
            var issued = await master.PostAsJsonAsync("/api/security/secrets", new IssueSecretApiRequest { Name = $"q-{Guid.NewGuid():N}", Role = "writer" }, Ct);
            issued.StatusCode.Should().Be(HttpStatusCode.Created);
            busySubscribers.Add(await SubscribeAsync((await issued.Content.ReadFromJsonAsync<IssuedSecretResponse>(Ct))!.Secret, busy));
        }

        var quietSubscriber = await SubscribeAsync(MasterSecret, quiet);
        try
        {
            var busyRow = await InsertAsync(master, busy);
            var quietRow = await InsertAsync(master, quiet);

            await UntilAsync(() => quietSubscriber.Heard.ContainsKey(quietRow), "the quiet table's change is delivered");
            await UntilAsync(() => busySubscribers.All(s => s.Heard.ContainsKey(busyRow)), "the busy table's change reaches every caller");

            // The busy change is read back once per caller; one queue for every table would deliver
            // the quiet change only after the last of those reads.
            quietSubscriber.Heard[quietRow].Should().BeLessThan(busySubscribers.Max(s => s.Heard[busyRow]),
                "a change to another table is not held behind the busy table's reads");
        }
        finally
        {
            foreach (var subscriber in busySubscribers)
            {
                await subscriber.Connection.DisposeAsync();
            }

            await quietSubscriber.Connection.DisposeAsync();
        }
    }
}
