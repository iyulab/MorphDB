using System.Net;
using System.Net.Http.Json;
using MorphDB.Service.Models.Api;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration.Api;

/// <summary>
/// A value that cannot be read as its column's type is the caller's mistake, answered as one: a
/// validation error naming every such column, on insert and on update. It used to fail where the
/// value was bound to the statement, as a <c>500</c> that named nothing.
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class ValueTypeContractTests
{
    private readonly HttpClient _client;

    public ValueTypeContractTests(ApiIntegrationFixture fixture)
    {
        _client = fixture.Api.Client;
    }

    private async Task<string> CreateTableAsync(CancellationToken ct)
    {
        var table = $"typed_{Guid.NewGuid():N}"[..30];
        var created = await _client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = table,
            Columns =
            [
                new CreateColumnApiRequest { Name = "label", Type = "text", Nullable = false },
                new CreateColumnApiRequest { Name = "quantity", Type = "integer", Nullable = true },
                new CreateColumnApiRequest { Name = "ref", Type = "uuid", Nullable = true },
            ],
        }, ct);
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(ct));
        return table;
    }

    [Fact]
    public async Task An_insert_with_values_of_the_wrong_type_is_refused_naming_each_column()
    {
        var ct = TestContext.Current.CancellationToken;
        var table = await CreateTableAsync(ct);

        var response = await _client.PostAsJsonAsync($"/api/data/{table}",
            new Dictionary<string, object?> { ["label"] = "a", ["quantity"] = "many", ["ref"] = "not-an-id" }, ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync(ct);
        body.Should().Contain("VALIDATION_ERROR").And.Contain("quantity").And.Contain("ref");
    }

    [Fact]
    public async Task An_update_with_a_value_of_the_wrong_type_is_refused_and_leaves_the_row()
    {
        var ct = TestContext.Current.CancellationToken;
        var table = await CreateTableAsync(ct);
        var inserted = await _client.PostAsJsonAsync($"/api/data/{table}",
            new Dictionary<string, object?> { ["label"] = "a", ["quantity"] = 3 }, ct);
        inserted.StatusCode.Should().Be(HttpStatusCode.Created, await inserted.Content.ReadAsStringAsync(ct));
        var id = (await inserted.Content.ReadFromJsonAsync<DataRecordResponse>(ct))!.Id;

        var response = await _client.PatchAsJsonAsync($"/api/data/{table}/{id}",
            new Dictionary<string, object?> { ["quantity"] = "many" }, ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(ct)).Should().Contain("VALIDATION_ERROR").And.Contain("quantity");
        var row = await _client.GetFromJsonAsync<DataRecordResponse>($"/api/data/{table}/{id}", ct);
        row!.Data["quantity"]!.ToString().Should().Be("3");
    }

    [Fact]
    public async Task Values_of_the_right_type_in_their_json_forms_are_accepted()
    {
        var ct = TestContext.Current.CancellationToken;
        var table = await CreateTableAsync(ct);

        var response = await _client.PostAsJsonAsync($"/api/data/{table}",
            new Dictionary<string, object?> { ["label"] = "a", ["quantity"] = 7, ["ref"] = Guid.NewGuid() }, ct);

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(ct));
    }
}
