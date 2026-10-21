using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using MorphDB.Client;
using MorphDB.Client.Models;
using MorphDB.Service.Models.Api;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration.Client;

/// <summary>
/// A list counts its rows only when asked (<c>count=true</c>): counting is a second statement over every
/// matching row, which most reads never look at. Whether more rows follow is known either way — the page
/// is read one row past its size.
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class ListCountTests(ApiIntegrationFixture fixture)
{
    private async Task<string> TableOfFiveAsync(CancellationToken ct)
    {
        var table = $"q_cnt_{Guid.NewGuid():N}"[..30];
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

    private async Task<JsonElement> PaginationAsync(HttpResponseMessage response, CancellationToken ct)
    {
        response.EnsureSuccessStatusCode();
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return body.RootElement.GetProperty("pagination").Clone();
    }

    [Theory]
    [InlineData("pageSize=2", true)]
    [InlineData("pageSize=2&page=3", false)]
    [InlineData("pageSize=2&offset=3", false)]
    [InlineData("pageSize=5", false)]
    public async Task A_list_not_asked_to_count_leaves_the_count_out_and_still_says_whether_more_rows_follow(string parameters, bool more)
    {
        var ct = TestContext.Current.CancellationToken;
        var table = await TableOfFiveAsync(ct);

        var pagination = await PaginationAsync(await fixture.Api.Client.GetAsync($"/api/data/{table}?orderBy=v:asc&{parameters}", ct), ct);

        Assert.False(pagination.TryGetProperty("totalCount", out _));
        Assert.False(pagination.TryGetProperty("totalPages", out _));
        Assert.Equal(more, pagination.GetProperty("hasNext").GetBoolean());
    }

    [Fact]
    public async Task A_list_asked_to_count_returns_the_count_and_its_pages()
    {
        var ct = TestContext.Current.CancellationToken;
        var table = await TableOfFiveAsync(ct);

        var pagination = await PaginationAsync(await fixture.Api.Client.GetAsync($"/api/data/{table}?pageSize=2&count=true", ct), ct);

        Assert.Equal(5, pagination.GetProperty("totalCount").GetInt64());
        Assert.Equal(3, pagination.GetProperty("totalPages").GetInt32());
        Assert.True(pagination.GetProperty("hasNext").GetBoolean());
    }

    [Fact]
    public async Task The_structured_query_counts_only_when_asked_too()
    {
        var ct = TestContext.Current.CancellationToken;
        var table = await TableOfFiveAsync(ct);

        var plain = await PaginationAsync(await fixture.Api.Client.PostAsJsonAsync($"/api/data/{table}/query", new ComplexQueryApiRequest { PageSize = 4 }, ct), ct);
        var counted = await PaginationAsync(await fixture.Api.Client.PostAsJsonAsync($"/api/data/{table}/query", new ComplexQueryApiRequest { PageSize = 4, Count = true }, ct), ct);

        Assert.False(plain.TryGetProperty("totalCount", out _));
        Assert.True(plain.GetProperty("hasNext").GetBoolean());
        Assert.Equal(5, counted.GetProperty("totalCount").GetInt64());
    }

    [Fact]
    public async Task The_project_list_counts_only_when_asked_too()
    {
        var ct = TestContext.Current.CancellationToken;

        var plain = await PaginationAsync(await fixture.Api.Client.GetAsync("/api/projects", ct), ct);
        var counted = await PaginationAsync(await fixture.Api.Client.GetAsync("/api/projects?count=true", ct), ct);

        Assert.False(plain.TryGetProperty("totalCount", out _));
        Assert.True(counted.GetProperty("totalCount").GetInt64() >= 1);
    }

    // The client read the next-page flags under names the server never wrote, so they were always false.
    [Fact]
    public async Task The_client_reads_whether_more_rows_follow_with_and_without_a_count()
    {
        var ct = TestContext.Current.CancellationToken;
        var table = await TableOfFiveAsync(ct);
        var http = new HttpClient(fixture.Api.CreateHandler()) { BaseAddress = fixture.Api.BaseAddress };
        await using var client = new MorphDBClient(http);
        client.SetProjectId(fixture.Api.ProjectId);

        var first = await client.Data.QueryAsync(table, new QueryRequest { OrderBy = [new OrderBy { Column = "v" }], PageSize = 2 }, ct);
        var last = await client.Data.QueryAsync(table, new QueryRequest { OrderBy = [new OrderBy { Column = "v" }], PageSize = 2, Page = 3, Count = true }, ct);

        Assert.Equal([1, 2], first.Data.Select(r => Convert.ToInt32(r.Data["v"], CultureInfo.InvariantCulture)));
        Assert.Null(first.Pagination.TotalCount);
        Assert.Null(first.Pagination.TotalPages);
        Assert.True(first.Pagination.HasNext);
        Assert.False(first.Pagination.HasPrevious);
        Assert.Equal([5], last.Data.Select(r => Convert.ToInt32(r.Data["v"], CultureInfo.InvariantCulture)));
        Assert.Equal(5, last.Pagination.TotalCount);
        Assert.Equal(3, last.Pagination.TotalPages);
        Assert.False(last.Pagination.HasNext);
        Assert.True(last.Pagination.HasPrevious);
    }
}
