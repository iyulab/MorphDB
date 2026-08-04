using System.Net;
using System.Net.Http.Json;
using MorphDB.Service.Models.Api;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration.Api;

/// <summary>
/// <c>EnforceOnWrite</c> was an option that could not be set.
/// <para>
/// It existed on <c>RelationMetadata</c> from the start, and the write pipeline's foreign-key
/// validator genuinely read it — but there were no columns behind it, and the schema manager pinned
/// it to <c>true</c> when creating a relation. So every relation came back enforcing, whatever the
/// caller asked for, and nothing said no. That is the harder half of a ghost contract: not a
/// promise the server cannot keep, but a knob wired to nothing.
/// </para>
/// <para>
/// These tests hold both halves — that the value survives a round trip, and that it changes what
/// the write path does. The second matters on its own: a value that persists but is never consulted
/// would pass the first test and still enforce everything.
/// </para>
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class RelationEnforcementContractTests
{
    private readonly HttpClient _client;

    public RelationEnforcementContractTests(ApiIntegrationFixture fixture)
    {
        _client = fixture.Api.Client;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_relation_keeps_the_enforcement_it_was_created_with(bool enforceOnWrite)
    {
        var (parent, child) = await CreateRelatedTablesAsync(enforceOnWrite);

        var relation = await CreateRelationAsync(parent, child, enforceOnWrite);

        relation.EnforceOnWrite.Should().Be(enforceOnWrite,
            "a relation created with an enforcement setting must come back with it — this is the " +
            "round trip that had no columns underneath it");
    }

    [Fact]
    public async Task A_non_enforcing_relation_does_not_reject_a_dangling_reference()
    {
        var (parent, child) = await CreateRelatedTablesAsync(enforceOnWrite: false);
        await CreateRelationAsync(parent, child, enforceOnWrite: false);

        var response = await _client.PostAsJsonAsync($"/api/data/{child}", new Dictionary<string, object?>
        {
            ["title"] = "child written before its parent",
            ["parent_ref"] = Guid.NewGuid().ToString()
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created,
            "a caller that rebuilds its tables wholesale writes children and parents in whatever " +
            "order the rebuild runs; a declared-but-unenforced link must not reject data that is " +
            "consistent at its source");
    }

    [Fact]
    public async Task An_enforcing_relation_still_rejects_a_dangling_reference()
    {
        var (parent, child) = await CreateRelatedTablesAsync(enforceOnWrite: true);
        await CreateRelationAsync(parent, child, enforceOnWrite: true);

        var response = await _client.PostAsJsonAsync($"/api/data/{child}", new Dictionary<string, object?>
        {
            ["title"] = "orphan",
            ["parent_ref"] = Guid.NewGuid().ToString()
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "making enforcement optional must not make it absent — the default is still to check, " +
            "and a relation that asked to be checked is checked");
    }

    private async Task<(string Parent, string Child)> CreateRelatedTablesAsync(bool enforceOnWrite)
    {
        var suffix = $"{(enforceOnWrite ? "on" : "off")}_{Guid.NewGuid():N}"[..16];
        var parent = $"fkparent_{suffix}";
        var child = $"fkchild_{suffix}";

        var parentResponse = await _client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = parent,
            Columns = [new CreateColumnApiRequest { Name = "label", Type = "text", Nullable = true }]
        });
        parentResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var childResponse = await _client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = child,
            Columns =
            [
                new CreateColumnApiRequest { Name = "title", Type = "text", Nullable = true },
                new CreateColumnApiRequest { Name = "parent_ref", Type = "uuid", Nullable = true }
            ]
        });
        childResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        return (parent, child);
    }

    private async Task<RelationApiResponse> CreateRelationAsync(
        string parent,
        string child,
        bool enforceOnWrite)
    {
        var response = await _client.PostAsJsonAsync("/api/schema/relations", new CreateRelationApiRequest
        {
            Name = $"fk_{child}_parent",
            SourceTable = child,
            SourceColumn = "parent_ref",
            TargetTable = parent,
            TargetColumn = "_id",
            Type = "one-to-many",
            EnforceOnWrite = enforceOnWrite
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created,
            "relation creation must succeed for the enforcement assertion to mean anything");

        return (await response.Content.ReadFromJsonAsync<RelationApiResponse>())!;
    }
}
