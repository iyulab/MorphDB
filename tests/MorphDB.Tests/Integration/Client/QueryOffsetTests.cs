using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using MorphDB.Client;
using MorphDB.Client.Models;
using MorphDB.Service.Models.Api;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration.Client;

/// <summary>
/// A query can start anywhere, not only on a page boundary: the engine always skipped rows by count, and
/// the data routes now take that count as <c>offset</c> instead of deriving it from a page.
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class QueryOffsetTests(ApiIntegrationFixture fixture)
{
    private async Task<string> TableOfFiveAsync(CancellationToken ct)
    {
        var table = $"q_off_{Guid.NewGuid():N}"[..30];
        var http = fixture.Api.Client;
        (await http.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = table,
            Columns = [new CreateColumnApiRequest { Name = "v", Type = "integer", Nullable = false }],
        }, ct)).EnsureSuccessStatusCode();
        for (var v = 1; v <= 5; v++)
        {
            (await http.PostAsJsonAsync($"/api/data/{table}", new Dictionary<string, object?> { ["v"] = v }, ct)).EnsureSuccessStatusCode();
        }

        return table;
    }

    [Fact]
    public async Task A_query_by_offset_returns_the_slice_that_starts_there()
    {
        var ct = TestContext.Current.CancellationToken;
        var table = await TableOfFiveAsync(ct);
        var http = new HttpClient(fixture.Api.CreateHandler()) { BaseAddress = fixture.Api.BaseAddress };
        await using var client = new MorphDBClient(http);
        client.SetProjectId(fixture.Api.ProjectId);

        var page = await client.Data.QueryAsync(table, new QueryRequest
        {
            OrderBy = [new OrderBy { Column = "v" }],
            PageSize = 2,
            Offset = 1,
        }, ct);

        Assert.Equal([2, 3], page.Data.Select(r => Convert.ToInt32(r.Data["v"], CultureInfo.InvariantCulture)));
        Assert.Equal(1, page.Pagination.Offset);
        Assert.Equal(5, page.Pagination.TotalCount);
    }

    [Fact]
    public async Task The_structured_query_takes_an_offset_too()
    {
        var ct = TestContext.Current.CancellationToken;
        var table = await TableOfFiveAsync(ct);

        var response = await fixture.Api.Client.PostAsJsonAsync($"/api/data/{table}/query", new ComplexQueryApiRequest
        {
            OrderBy = ["v:asc"],
            PageSize = 2,
            Offset = 3,
        }, ct);

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<MorphDB.Service.Models.Api.PagedResponse<DataRecordResponse>>(ct);
        Assert.Equal([4, 5], body!.Data.Select(r => Convert.ToInt32(r.Data["v"]?.ToString(), CultureInfo.InvariantCulture)));
        Assert.True(body.Pagination.HasPrevious);
        Assert.False(body.Pagination.HasNext);
    }

    [Theory]
    [InlineData("offset=1&page=2")]
    [InlineData("offset=-1")]
    public async Task An_offset_beside_a_page_or_below_zero_is_refused(string parameters)
    {
        var ct = TestContext.Current.CancellationToken;
        var table = await TableOfFiveAsync(ct);

        var response = await fixture.Api.Client.GetAsync($"/api/data/{table}?{parameters}", ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
