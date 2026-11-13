using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using MorphDB.Service.Models.Api;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration.Api;

/// <summary>
/// An index predicate PostgreSQL cannot accept is the caller's mistake and is answered as one: the
/// <c>where</c> names a column the table does not have, calls a function that does not exist, or uses one
/// an index predicate may not (a volatile one). The request is refused with <c>400 INVALID_EXPRESSION</c>
/// -- not the opaque <c>500</c> the database error used to become -- carrying the kind of refusal and no
/// physical name. A predicate that is fine still creates the index.
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class IndexDefinitionRefusalContractTests
{
    private readonly HttpClient _client;

    public IndexDefinitionRefusalContractTests(ApiIntegrationFixture fixture) => _client = fixture.Api.Client;

    private static readonly string[] IndexedColumns = ["qty"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("no_such_column > 0", "a column the table does not have")]
    [InlineData("no_such_function(qty) > 0", "a function that does not exist")]
    [InlineData("qty > random()", "a volatile function, which an index predicate may not use")]
    [InlineData("qty > 'abc'", "a value that is not of the column's type")]
    public async Task An_index_predicate_PostgreSQL_cannot_accept_is_a_400_not_a_500(string predicate, string because)
    {
        var table = await SeedTableAsync();

        var response = await CreateIndexAsync(table, "ix_bad", predicate);
        var text = await response.Content.ReadAsStringAsync(Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, $"{because}: {text}");
        JsonElement.Parse(text).GetProperty("code").GetString().Should().Be("INVALID_EXPRESSION");
        PhysicalNameGuard.FindPhysicalNames(text).Should().BeEmpty("the refusal must not carry a physical name");
    }

    [Fact]
    public async Task An_index_predicate_that_is_fine_still_creates_the_index()
    {
        var table = await SeedTableAsync();

        var response = await CreateIndexAsync(table, "ix_fine", "qty > 0");

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
    }

    /// <summary>
    /// The predicate is written in the table's own column names and has to reach the database as the
    /// names it stores them under: a partial unique index on <c>qty &gt; 0</c> lets rows that do not satisfy
    /// it repeat and refuses a repeat among rows that do.
    /// </summary>
    [Fact]
    public async Task A_partial_unique_index_applies_its_predicate_to_the_rows()
    {
        var table = await SeedTableAsync();
        var created = await _client.PostAsync($"/api/schema/tables/{table}/indexes", new StringContent(
            JsonSerializer.Serialize(new { name = "ix_partial", columns = IndexedColumns, unique = true, where = "qty > 0" }),
            Encoding.UTF8, "application/json"), Ct);
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(Ct));

        async Task<HttpStatusCode> InsertAsync(int qty) =>
            (await _client.PostAsJsonAsync($"/api/data/{table}", new Dictionary<string, object?> { ["qty"] = qty }, Ct)).StatusCode;

        (await InsertAsync(0)).Should().Be(HttpStatusCode.Created);
        (await InsertAsync(0)).Should().Be(HttpStatusCode.Created, "rows outside the predicate are not in the index");
        (await InsertAsync(5)).Should().Be(HttpStatusCode.Created);
        (await InsertAsync(5)).Should().NotBe(HttpStatusCode.Created, "a repeat among the rows the predicate keeps is refused");
    }

    private Task<HttpResponseMessage> CreateIndexAsync(string table, string name, string predicate) =>
        _client.PostAsync($"/api/schema/tables/{table}/indexes", new StringContent(
            JsonSerializer.Serialize(new { name, columns = IndexedColumns, where = predicate }), Encoding.UTF8, "application/json"), Ct);

    private async Task<string> SeedTableAsync()
    {
        var table = $"ixr_{Guid.NewGuid():N}"[..20];
        (await _client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = table,
            Columns = [new CreateColumnApiRequest { Name = "qty", Type = "integer", Nullable = true }],
        }, Ct)).EnsureSuccessStatusCode();
        return table;
    }
}
