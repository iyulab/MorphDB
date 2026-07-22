using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using MorphDB.Service.Models.Api;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration.Api;

/// <summary>
/// Pins the non-exposure of <c>project_id</c> (issue project-id-column-leaks-into-rows, adopted as
/// HANDOFF §3.10-B2). docs/API.md declares the project "an internal operating unit"; the request is
/// already scoped by <c>X-Project-Id</c>, so the GUID carries zero information for a consumer —
/// yet it leaked into every data row, the schema column list, and the "Available columns" error
/// text. The physical column and scope isolation stay server-internal.
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class ProjectIdExposureTests
{
    private readonly HttpClient _client;

    public ProjectIdExposureTests(ApiIntegrationFixture fixture)
    {
        _client = fixture.Api.Client;
    }

    private async Task<string> CreateTableWithRowAsync()
    {
        var tableName = $"pidexp_{Guid.NewGuid():N}"[..25];
        var create = await _client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = tableName,
            Columns = [new CreateColumnApiRequest { Name = "grade", Type = "text", Nullable = true }],
        });
        create.EnsureSuccessStatusCode();
        var insert = await _client.PostAsJsonAsync($"/api/data/{tableName}",
            new Dictionary<string, object?> { ["grade"] = "vip" });
        insert.EnsureSuccessStatusCode();
        return tableName;
    }

    [Fact]
    public async Task Data_rows_do_not_carry_the_project_id()
    {
        var tableName = await CreateTableWithRowAsync();

        var response = await _client.GetAsync($"/api/data/{tableName}");
        response.EnsureSuccessStatusCode();
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        var row = body.RootElement.GetProperty("data")[0].GetProperty("data");
        row.TryGetProperty("project_id", out _).Should().BeFalse(
            "the request is already project-scoped; the internal GUID says nothing to the caller");
        row.TryGetProperty("_id", out _).Should().BeTrue("documented system columns stay");
    }

    [Fact]
    public async Task Write_responses_do_not_carry_the_project_id()
    {
        var tableName = $"pidexp_{Guid.NewGuid():N}"[..25];
        var create = await _client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = tableName,
            Columns = [new CreateColumnApiRequest { Name = "grade", Type = "text", Nullable = true }],
        });
        create.EnsureSuccessStatusCode();

        var insert = await _client.PostAsJsonAsync($"/api/data/{tableName}",
            new Dictionary<string, object?> { ["grade"] = "vip" });
        insert.EnsureSuccessStatusCode();
        var body = JsonDocument.Parse(await insert.Content.ReadAsStringAsync());

        body.RootElement.GetProperty("data").TryGetProperty("project_id", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Complex_query_rows_do_not_carry_the_project_id()
    {
        var tableName = await CreateTableWithRowAsync();

        var response = await _client.PostAsync($"/api/data/{tableName}/query",
            new StringContent("""{"pageSize":5}""", Encoding.UTF8, "application/json"));
        response.EnsureSuccessStatusCode();
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        body.RootElement.GetProperty("data")[0].GetProperty("data")
            .TryGetProperty("project_id", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Schema_responses_do_not_list_project_id_as_a_column()
    {
        var tableName = await CreateTableWithRowAsync();

        var response = await _client.GetAsync($"/api/schema/tables/{tableName}");
        response.EnsureSuccessStatusCode();
        var table = await response.Content.ReadFromJsonAsync<TableApiResponse>();

        table!.Columns.Select(c => c.Name).Should().NotContain("project_id",
            "docs/SYSTEM_COLUMNS.md does not know it and consumers cannot use it");
        table.Columns.Select(c => c.Name).Should().Contain("_id");
    }

    [Fact]
    public async Task The_available_columns_error_text_does_not_name_project_id()
    {
        var tableName = await CreateTableWithRowAsync();

        var response = await _client.PostAsJsonAsync($"/api/data/{tableName}",
            new Dictionary<string, object?> { ["discout"] = 5 });

        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        error!.Message.Should().Contain("Available columns");
        error.Message.Should().NotContain("project_id");
    }

    [Fact]
    public async Task OData_rows_do_not_carry_the_project_id()
    {
        var tableName = await CreateTableWithRowAsync();
        var entitySet = string.Concat(tableName.Split('_').Select(p =>
            p.Length > 0 ? char.ToUpperInvariant(p[0]) + p[1..].ToLowerInvariant() : p));

        var response = await _client.GetAsync($"/odata/{entitySet}?$top=1");
        response.EnsureSuccessStatusCode();
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        body.RootElement.GetProperty("value")[0].TryGetProperty("project_id", out _).Should().BeFalse();
    }
}
