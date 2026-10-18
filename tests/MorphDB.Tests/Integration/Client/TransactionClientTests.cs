using System.Net.Http.Json;
using MorphDB.Client;
using MorphDB.Client.Models;
using MorphDB.Service.Models.Api;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration.Client;

/// <summary>
/// The client's transactions and finalization over the real service: what it sends is what the
/// service reads. An operation used to name its kind <c>operation</c> where the service reads
/// <c>method</c>, so the service refused every transaction; and a finalization read counts the service
/// does not send, so it reported nothing finalized.
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class TransactionClientTests(ApiIntegrationFixture fixture)
{
    private async Task<string> RowStateTableAsync(CancellationToken ct)
    {
        var table = $"tx_cli_{Guid.NewGuid():N}"[..28];
        (await fixture.Api.Client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = table,
            Columns =
            [
                new CreateColumnApiRequest { Name = "name", Type = "text", Nullable = false },
                new CreateColumnApiRequest { Name = "score", Type = "integer", Nullable = true },
            ],
            SystemColumns = new SystemColumnOptionsApiRequest { RowState = true },
        }, ct)).EnsureSuccessStatusCode();
        return table;
    }

    private MorphDBClient Client()
    {
        var http = new HttpClient(fixture.Api.CreateHandler()) { BaseAddress = fixture.Api.BaseAddress };
        var client = new MorphDBClient(http);
        client.SetProjectId(fixture.Api.ProjectId);
        return client;
    }

    [Fact]
    public async Task A_transaction_sent_through_the_client_runs_and_a_later_operation_reads_an_earlier_result()
    {
        var ct = TestContext.Current.CancellationToken;
        var table = await RowStateTableAsync(ct);
        await using var client = Client();

        var result = await client.Transactions.ExecuteAsync(new TransactionRequest
        {
            Operations =
            [
                new TransactionOperation { Method = BatchMethod.Insert, Table = table, Ref = "first", Data = new Dictionary<string, object?> { ["name"] = "a", ["score"] = 1 } },
                new TransactionOperation { Method = BatchMethod.Update, Table = table, Id = "$first._id", Data = new Dictionary<string, object?> { ["score"] = 2 } },
            ],
        }, ct);

        Assert.True(result.Success);
        Assert.Equal([0, 1], result.Results.Select(r => r.Index));
        Assert.All(result.Results, r => Assert.True(r.Success));
        var id = result.Results[0].Id;
        Assert.NotNull(id);
        Assert.Equal("first", result.Results[0].Ref);

        var row = await client.Data.GetByIdAsync(table, id.Value, ct);
        Assert.Equal(2L, Convert.ToInt64(row!.Data["score"], System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task A_draft_written_in_a_transaction_is_finalized_through_the_client()
    {
        var ct = TestContext.Current.CancellationToken;
        var table = await RowStateTableAsync(ct);
        await using var client = Client();

        var written = await client.Transactions.ExecuteAsync(new TransactionRequest
        {
            Operations =
            [
                new TransactionOperation { Method = BatchMethod.Insert, Table = table, Mode = "draft", Data = new Dictionary<string, object?> { ["name"] = "one" } },
                new TransactionOperation { Method = BatchMethod.Insert, Table = table, Mode = "draft", Data = new Dictionary<string, object?> { ["name"] = "two" } },
                new TransactionOperation { Method = BatchMethod.Insert, Table = table, Mode = "draft", Data = new Dictionary<string, object?> { ["name"] = "three" } },
            ],
        }, ct);
        var ids = written.Results.Select(r => r.Id!.Value).ToList();

        var single = await client.Transactions.FinalizeRecordAsync(table, ids[0], ct);
        Assert.Equal(1, single.ValidCount);
        Assert.Equal(0, single.ErrorCount);
        var only = Assert.Single(single.Results);
        Assert.Equal(ids[0], only.RecordId);
        Assert.Equal("valid", only.NewState);

        var batch = await client.Transactions.FinalizeBatchAsync(table, new FinalizeRequest { RecordIds = ids[1..] }, ct);
        Assert.Equal(2, batch.ValidCount);
        Assert.Equal(ids[1..], batch.Results.Select(r => r.RecordId));
    }
}
