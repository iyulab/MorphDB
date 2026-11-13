using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MorphDB.Service.Models.Api;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration.Api;

/// <summary>
/// A <c>check</c> is a virtual constraint enforced by the app-layer evaluator, and the docs promise a
/// declaration is accepted only when that evaluator can enforce it -- "a stored-but-unenforceable CHECK
/// would constrain nothing, silently". The evaluator skips a comparison whose field has no value, so a
/// misspelt field name, or an expression outside the grammar, is exactly such a check. Every way to declare
/// one -- create a table, add a column, change a column -- is held to the same gate: the expression is in the
/// grammar and reads only fields the table has. A check that passes still constrains.
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class CheckFieldDeclarationContractTests
{
    private readonly HttpClient _client;

    public CheckFieldDeclarationContractTests(ApiIntegrationFixture fixture) => _client = fixture.Api.Client;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_table_whose_check_reads_a_field_it_does_not_have_is_refused()
    {
        var response = await _client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = Name("chk_unknown"),
            Columns = [new CreateColumnApiRequest { Name = "qty", Type = "integer", Check = "no_such_field > 0" }],
        }, Ct);

        await AssertRefusedAsync(response, "no_such_field");
    }

    [Fact]
    public async Task A_check_may_read_a_sibling_column_declared_in_the_same_request_and_still_constrains()
    {
        var table = Name("chk_sibling");
        (await _client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = table,
            Columns =
            [
                new CreateColumnApiRequest { Name = "low", Type = "integer" },
                new CreateColumnApiRequest { Name = "high", Type = "integer", Check = "high >= low" },
            ],
        }, Ct)).StatusCode.Should().Be(HttpStatusCode.Created);

        (await _client.PostAsJsonAsync($"/api/data/{table}", new Dictionary<string, object?> { ["low"] = 1, ["high"] = 5 }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        (await _client.PostAsJsonAsync($"/api/data/{table}", new Dictionary<string, object?> { ["low"] = 9, ["high"] = 5 }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest, "a declared check that passes the gate is enforced");
    }

    [Fact]
    public async Task A_column_added_with_a_check_on_a_field_the_table_does_not_have_is_refused()
    {
        var table = await SeedTableAsync("chk_add");

        var response = await _client.PostAsJsonAsync($"/api/schema/tables/{table}/columns",
            new CreateColumnApiRequest { Name = "extra", Type = "integer", Check = "no_such_field > 0" }, Ct);

        await AssertRefusedAsync(response, "no_such_field");
    }

    [Theory]
    [InlineData("no_such_field > 0", "a field the table does not have")]
    [InlineData("qty >>> 0", "an expression outside the grammar")]
    public async Task A_column_changed_to_a_check_the_evaluator_cannot_enforce_is_refused(string check, string because)
    {
        var table = await SeedTableAsync("chk_patch");

        var response = await PatchCheckAsync(table, check);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, because);
    }

    [Fact]
    public async Task A_column_can_be_changed_to_a_check_that_is_enforceable_and_a_blank_one_clears_it()
    {
        var table = await SeedTableAsync("chk_ok");

        (await PatchCheckAsync(table, "qty > 0")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await _client.PostAsJsonAsync($"/api/data/{table}", new Dictionary<string, object?> { ["qty"] = -1 }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        (await PatchCheckAsync(table, " ")).StatusCode.Should().Be(HttpStatusCode.OK, "a blank expression clears the check");
        (await _client.PostAsJsonAsync($"/api/data/{table}", new Dictionary<string, object?> { ["qty"] = -1 }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.Created);
    }

    private static string Name(string prefix) => $"{prefix}_{Guid.NewGuid():N}"[..24];

    private static async Task AssertRefusedAsync(HttpResponseMessage response, string field)
    {
        var text = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, text);
        JsonElement.Parse(text).GetProperty("message").GetString().Should().Contain(field);
        PhysicalNameGuard.FindPhysicalNames(text).Should().BeEmpty("the refusal must not carry a physical name");
    }

    private async Task<string> SeedTableAsync(string prefix)
    {
        var table = Name(prefix);
        (await _client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = table,
            Columns = [new CreateColumnApiRequest { Name = "qty", Type = "integer" }],
        }, Ct)).EnsureSuccessStatusCode();
        return table;
    }

    private async Task<HttpResponseMessage> PatchCheckAsync(string table, string check)
    {
        var current = await _client.GetFromJsonAsync<JsonElement>($"/api/schema/tables/{table}", Ct);
        var version = current.GetProperty("version").GetInt32();
        return await _client.PatchAsJsonAsync($"/api/schema/tables/{table}/columns/qty",
            new UpdateColumnApiRequest { Check = check, Version = version }, Ct);
    }
}
