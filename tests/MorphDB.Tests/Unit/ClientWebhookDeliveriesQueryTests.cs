using System.Net;
using System.Text;
using MorphDB.Client;

namespace MorphDB.Tests.Unit;

/// <summary>
/// The deliveries route pages by <c>limit</c> and <c>offset</c>. The client sent <c>page</c> and <c>pageSize</c>,
/// which the route does not take and used to drop without a word — so every page answered the first one, and a
/// caller walking the history read the same fifty deliveries over and over.
/// </summary>
public class ClientWebhookDeliveriesQueryTests
{
    [Theory]
    [InlineData(1, 50, "limit=50&offset=0")]
    [InlineData(3, 20, "limit=20&offset=40")]
    [InlineData(2, 1, "limit=1&offset=1")]
    public async Task A_page_is_sent_as_the_limit_and_offset_the_route_takes(int page, int pageSize, string expected)
    {
        var handler = new RecordingHandler();
        await using var client = new MorphDBClient(new HttpClient(handler) { BaseAddress = new Uri("http://morphdb.invalid") });

        await client.Webhooks.GetDeliveriesAsync(Guid.NewGuid(), page, pageSize, TestContext.Current.CancellationToken);

        handler.Query.Should().Be("?" + expected);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(1, 0)]
    [InlineData(-1, 10)]
    public async Task A_page_before_the_first_or_an_empty_page_is_refused_before_anything_is_sent(int page, int pageSize)
    {
        var handler = new RecordingHandler();
        await using var client = new MorphDBClient(new HttpClient(handler) { BaseAddress = new Uri("http://morphdb.invalid") });

        var act = () => client.Webhooks.GetDeliveriesAsync(Guid.NewGuid(), page, pageSize, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
        handler.Query.Should().BeNull("no request was made");
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public string? Query { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Query = request.RequestUri!.Query;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", Encoding.UTF8, "application/json"),
            });
        }
    }
}
