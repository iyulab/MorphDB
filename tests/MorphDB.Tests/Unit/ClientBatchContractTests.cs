using System.Net;
using System.Text;
using System.Text.Json;
using MorphDB.Client;
using ClientModels = MorphDB.Client.Models;
using ServerModels = MorphDB.Service.Models.Api;

namespace MorphDB.Tests.Unit;

/// <summary>
/// Guards the client's batch surface against the server it talks to. <see cref="ClientWireContractTests"/>
/// covers the schema models the same way; the batch and data-value paths had no such cover, and two
/// defects survived there — the client called <c>/api/data/{table}/batch</c>, which no controller serves,
/// with request and response shapes no endpoint uses; and record values arrived as
/// <see cref="JsonElement"/> rather than as .NET values.
/// </summary>
public class ClientBatchContractTests
{
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Captures the request a client method issues, without a server.</summary>
    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly string _responseJson;

        public CapturingHandler(string responseJson) => _responseJson = responseJson;

        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responseJson, Encoding.UTF8, "application/json"),
            });
        }
    }

    private static MorphDBClient ClientOver(CapturingHandler handler)
        => new("http://morphdb.test", new MorphDBClientOptions { HttpMessageHandler = handler });

    [Fact]
    public async Task InsertMany_targets_the_route_the_server_actually_serves()
    {
        var handler = new CapturingHandler("""{"results":[],"successCount":0,"failureCount":0}""");
        var client = ClientOver(handler);

        await client.Batch.InsertManyAsync("products", [new Dictionary<string, object?> { ["k"] = "a" }]);

        // BatchController: [Route("api/batch")] + [HttpPost("data/{table}/insert")].
        handler.Request!.Method.Should().Be(HttpMethod.Post);
        handler.Request.RequestUri!.AbsolutePath.Should().Be("/api/batch/data/products/insert");
    }

    [Fact]
    public async Task Execute_targets_the_route_the_server_actually_serves()
    {
        var handler = new CapturingHandler("""{"results":[],"successCount":0,"failureCount":0}""");
        var client = ClientOver(handler);

        await client.Batch.ExecuteAsync(new ClientModels.BatchRequest
        {
            Operations = [new ClientModels.BatchOperation { Method = ClientModels.BatchMethod.Insert, Table = "products" }],
        });

        // BatchController: [Route("api/batch")] + [HttpPost("data")].
        handler.Request!.Method.Should().Be(HttpMethod.Post);
        handler.Request.RequestUri!.AbsolutePath.Should().Be("/api/batch/data");
    }

    [Fact]
    public void ClientBatchRequest_DeserializesInto_ServerRequest()
    {
        var client = new ClientModels.BatchRequest
        {
            Operations =
            [
                new ClientModels.BatchOperation
                {
                    Method = ClientModels.BatchMethod.Insert,
                    Table = "products",
                    Data = new Dictionary<string, object?> { ["name"] = "widget" },
                },
                new ClientModels.BatchOperation
                {
                    Method = ClientModels.BatchMethod.Delete,
                    Table = "products",
                    Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                },
            ],
        };

        var wire = JsonSerializer.Serialize(client, WebOptions);
        var server = JsonSerializer.Deserialize<ServerModels.BatchRequest>(wire, WebOptions);

        server.Should().NotBeNull();
        server!.Operations.Should().HaveCount(2);
        server.Operations[0].Method.Should().Be("INSERT");
        server.Operations[0].Table.Should().Be("products");
        server.Operations[1].Id.Should().Be(Guid.Parse("11111111-1111-1111-1111-111111111111"));
    }

    [Fact]
    public async Task ServerBatchResponse_DeserializesInto_ClientResponse()
    {
        var server = new ServerModels.BatchResponse
        {
            Results = [new ServerModels.BatchOperationResult { Index = 0, Success = true, AffectedRows = 1 }],
            SuccessCount = 1,
            FailureCount = 0,
        };
        var wire = JsonSerializer.Serialize(server, WebOptions);
        var handler = new CapturingHandler(wire);
        var client = ClientOver(handler);

        var response = await client.Batch.InsertManyAsync("products", [new Dictionary<string, object?>()]);

        response.SuccessCount.Should().Be(1);
        response.FailureCount.Should().Be(0);
        response.Results.Should().ContainSingle();
        response.Results[0].AffectedRows.Should().Be(1);
    }

    [Fact]
    public async Task Record_values_arrive_as_dotnet_values_not_as_JsonElement()
    {
        // A page exactly as DataController serializes it.
        const string wire = """
            {"data":[{"id":"22222222-2222-2222-2222-222222222222",
                      "data":{"lot":"L-3","qty":7,"ratio":1.5,"ok":true,"missing":null,
                              "nested":{"a":1},"list":[1,2]}}],
             "pagination":{"page":1,"pageSize":50,"totalCount":1,"totalPages":1,"hasNext":false,"hasPrevious":false}}
            """;
        var client = ClientOver(new CapturingHandler(wire));

        var page = await client.Data.QueryAsync("products");
        var values = page.Data[0].Data;

        // Each assertion fails against a JsonElement, which is what the client used to hand back.
        values["lot"].Should().Be("L-3");
        values["qty"].Should().Be(7L);
        values["ratio"].Should().Be(1.5m);
        values["ok"].Should().Be(true);
        values["missing"].Should().BeNull();
        values["nested"].Should().BeOfType<Dictionary<string, object?>>()
            .Which["a"].Should().Be(1L);
        values["list"].Should().BeOfType<List<object?>>()
            .Which.Should().Equal(1L, 2L);
    }
}
