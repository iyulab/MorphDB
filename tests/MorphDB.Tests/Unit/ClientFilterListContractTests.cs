using System.Net;
using System.Text;
using MorphDB.Client;
using MorphDB.Client.Models;

namespace MorphDB.Tests.Unit;

/// <summary>
/// The values of <c>in</c> and <c>notin</c> go on the wire as a JSON array — the form the server reads a
/// list in. They used to be written with <see cref="object.ToString"/>, which names the list's type.
/// </summary>
public class ClientFilterListContractTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"data":[],"pagination":{"page":1,"pageSize":20,"hasNext":false}}""", Encoding.UTF8, "application/json"),
            });
        }
    }

    [Fact]
    public async Task A_list_filter_is_sent_as_a_json_array()
    {
        var handler = new CapturingHandler();
        var client = new MorphDBClient("http://morphdb.test", new MorphDBClientOptions { HttpMessageHandler = handler });
        var at = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.FromHours(9));

        await client.Data.QueryAsync("t", new QueryRequest
        {
            Filters =
            [
                new Filter("name", FilterOperator.In, new List<string> { "a", "b,c", "d\"e" }),
                new Filter("n", FilterOperator.NotIn, new object[] { 1, 2L, 1.5m, true }),
                new Filter("at", FilterOperator.In, new[] { at }),
            ],
        }, TestContext.Current.CancellationToken);

        var filters = handler.RequestUri!.Query.TrimStart('?').Split('&')
            .Where(p => p.StartsWith("filter=", StringComparison.Ordinal))
            .Select(p => Uri.UnescapeDataString(p["filter=".Length..]))
            .ToList();
        filters.Should().Equal(
            """name:in:["a","b,c","d\"e"]""",
            "n:notin:[1,2,1.5,true]",
            """at:in:["2026-01-02T03:04:05.0000000+09:00"]""");
    }
}
