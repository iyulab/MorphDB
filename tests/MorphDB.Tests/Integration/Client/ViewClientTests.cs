using System.Net.Http.Json;
using MorphDB.Client;
using MorphDB.Client.Models;
using MorphDB.Service.Models.Api;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration.Client;

/// <summary>
/// The client's views over the real service: what it sends is what the service reads. The create
/// request used to carry its own shape — columns as names, a filter string, <c>isMaterialized</c> —
/// which the service refuses, so no view could be created through the client.
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class ViewClientTests(ApiIntegrationFixture fixture)
{
    private static readonly string[] TitlesBAndC = ["b", "c"];

    [Fact]
    public async Task A_view_created_through_the_client_reads_back_and_serves_its_rows()
    {
        var ct = TestContext.Current.CancellationToken;
        var table = $"view_cli_{Guid.NewGuid():N}"[..30];
        var setup = fixture.Api.Client;
        (await setup.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = table,
            Columns =
            [
                new CreateColumnApiRequest { Name = "title", Type = "text", Nullable = false },
                new CreateColumnApiRequest { Name = "score", Type = "integer", Nullable = false },
            ],
        }, ct)).EnsureSuccessStatusCode();
        foreach (var (title, score) in new[] { ("a", 1), ("b", 5), ("c", 9) })
        {
            (await setup.PostAsJsonAsync($"/api/data/{table}", new Dictionary<string, object?> { ["title"] = title, ["score"] = score }, ct)).EnsureSuccessStatusCode();
        }

        var http = new HttpClient(fixture.Api.CreateHandler()) { BaseAddress = fixture.Api.BaseAddress };
        await using var client = new MorphDBClient(http);
        client.SetProjectId(fixture.Api.ProjectId);

        var name = $"v_{Guid.NewGuid():N}"[..20];
        var created = await client.Views.CreateAsync(new CreateViewRequest
        {
            Name = name,
            BaseTable = table,
            Columns = [new ViewColumn { Source = "title", Alias = "title" }, new ViewColumn { Source = "score", Alias = "score" }],
            Filters = [new ViewFilter { Field = "score", Operator = "gte", Value = 5 }],
            OrderBy = [new ViewOrder { Column = "score", Descending = true }],
            Description = "high scores",
        }, ct);

        Assert.Equal(name, created.Name);
        Assert.Equal(["title", "score"], created.Columns.Select(c => c.Name));
        Assert.False(created.IsMaterialized);
        var filter = Assert.Single(created.Filters!);
        Assert.Equal("score", filter.Field);
        Assert.True(Assert.Single(created.OrderBy!).Descending);

        var read = await client.Views.GetAsync(name, ct);
        Assert.NotNull(read);
        Assert.Equal(created.Id, read.Id);
        Assert.Contains(await client.Views.ListAsync(ct), v => v.Name == name);

        var rows = await client.Views.QueryAsync(name, cancellationToken: ct);
        Assert.Equal(["c", "b"], rows.Data.Select(r => r["title"]?.ToString()));
        Assert.Equal(2, rows.TotalCount);

        // A read of the view takes the table read's query: a further filter and a page.
        var page = await client.Views.QueryAsync(name, new QueryRequest
        {
            Filters = [new Filter("title", FilterOperator.Equal, "b")],
            PageSize = 1,
        }, ct);
        Assert.Equal(["b"], page.Data.Select(r => r["title"]?.ToString()));

        // Several filters each apply — sent as repeated parameters, the way a table read sends them —
        // and a list goes as the list it is.
        var narrowed = await client.Views.QueryAsync(name, new QueryRequest
        {
            Filters =
            [
                new Filter("score", FilterOperator.GreaterThanOrEqual, 6),
                new Filter("title", FilterOperator.In, TitlesBAndC),
            ],
        }, ct);
        Assert.Equal(["c"], narrowed.Data.Select(r => r["title"]?.ToString()));

        await client.Views.DeleteAsync(name, ct);
        Assert.Null(await client.Views.GetAsync(name, ct));
    }

    [Fact]
    public async Task A_materialized_view_created_through_the_client_refreshes()
    {
        var ct = TestContext.Current.CancellationToken;
        var table = $"view_mat_{Guid.NewGuid():N}"[..30];
        var setup = fixture.Api.Client;
        (await setup.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = table,
            Columns = [new CreateColumnApiRequest { Name = "title", Type = "text", Nullable = false }],
        }, ct)).EnsureSuccessStatusCode();
        (await setup.PostAsJsonAsync($"/api/data/{table}", new Dictionary<string, object?> { ["title"] = "first" }, ct)).EnsureSuccessStatusCode();

        var http = new HttpClient(fixture.Api.CreateHandler()) { BaseAddress = fixture.Api.BaseAddress };
        await using var client = new MorphDBClient(http);
        client.SetProjectId(fixture.Api.ProjectId);

        var name = $"mv_{Guid.NewGuid():N}"[..20];
        var created = await client.Views.CreateAsync(new CreateViewRequest
        {
            Name = name,
            BaseTable = table,
            Columns = [new ViewColumn { Source = "title", Alias = "title" }],
            Materialized = true,
        }, ct);
        Assert.True(created.IsMaterialized);
        Assert.Equal("OnDemand", created.RefreshPolicy);

        (await setup.PostAsJsonAsync($"/api/data/{table}", new Dictionary<string, object?> { ["title"] = "second" }, ct)).EnsureSuccessStatusCode();
        Assert.Single((await client.Views.QueryAsync(name, cancellationToken: ct)).Data);

        await client.Views.RefreshAsync(name, ct);
        Assert.Equal(2, (await client.Views.QueryAsync(name, cancellationToken: ct)).Data.Count);

        await client.Views.DeleteAsync(name, ct);
    }
}
