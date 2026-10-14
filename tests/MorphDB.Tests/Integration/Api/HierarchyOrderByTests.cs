using System.Net;
using System.Net.Http.Json;
using MorphDB.Service.Models.Api;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration.Api;

/// <summary>
/// The descendants and subtree routes read <c>orderBy</c> as <c>column [asc|desc]</c> terms over the
/// table's columns; anything else is refused before it reaches the query.
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class HierarchyOrderByTests
{
    private readonly HttpClient _client;

    public HierarchyOrderByTests(ApiIntegrationFixture fixture)
    {
        _client = fixture.Api.Client;
    }

    private async Task<(string Table, Guid RecordId)> CreateTableWithRowAsync()
    {
        var tableName = $"hier_order_{Guid.NewGuid():N}"[..30];
        var created = await _client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = tableName,
            Columns = [new CreateColumnApiRequest { Name = "name", Type = "text", Nullable = false }],
            SystemColumns = new SystemColumnOptionsApiRequest { Hierarchy = true }
        }, TestContext.Current.CancellationToken);
        created.EnsureSuccessStatusCode();

        var row = await _client.PostAsJsonAsync($"/api/data/{tableName}",
            new Dictionary<string, object?> { ["name"] = "root" }, TestContext.Current.CancellationToken);
        row.EnsureSuccessStatusCode();
        var record = await row.Content.ReadFromJsonAsync<DataRecordResponse>(TestContext.Current.CancellationToken);
        return (tableName, record!.Id);
    }

    [Theory]
    [InlineData("descendants", "name; DROP TABLE x")]
    [InlineData("descendants", "(SELECT 1)")]
    [InlineData("descendants", "name asc, pg_sleep(1)")]
    [InlineData("subtree", "name desc nulls first")]
    [InlineData("descendants", "no_such_column")]
    public async Task OrderBy_ThatIsNotAColumnList_IsRefused(string route, string orderBy)
    {
        var (table, id) = await CreateTableWithRowAsync();

        var response = await _client.GetAsync(
            $"/api/hierarchy/{table}/{id}/{route}?orderBy={Uri.EscapeDataString(orderBy)}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("descendants", "name desc")]
    [InlineData("subtree", "name, _created_at desc")]
    public async Task OrderBy_OverDeclaredAndSystemColumns_IsAccepted(string route, string orderBy)
    {
        var (table, id) = await CreateTableWithRowAsync();

        var response = await _client.GetAsync(
            $"/api/hierarchy/{table}/{id}/{route}?orderBy={Uri.EscapeDataString(orderBy)}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }
}
