using System.Net;
using System.Net.Http.Json;
using MorphDB.Client;
using MorphDB.Service.Models.Api;
using MorphDB.Tests.Fixtures;
using ClientModels = MorphDB.Client.Models;

namespace MorphDB.Tests.Integration.Api;

/// <summary>
/// A table's creator notes: strings the creator gives when it creates the table, stored with it and
/// returned with it, never read by MorphDB. Two writers sharing a project can tell their own tables
/// apart by them — a writer that recreates its table writes its notes again, and one that finds a table
/// carrying another writer's notes knows not to replace it.
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public sealed class TableMetadataTests
{
    private readonly ApiIntegrationFixture _fixture;
    private readonly HttpClient _client;

    public TableMetadataTests(ApiIntegrationFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.Api.Client;
    }

    private static string Name() => $"tmeta_{Guid.NewGuid():N}"[..20];

    private async Task<HttpResponseMessage> CreateAsync(string name, Dictionary<string, string>? metadata) =>
        await _client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = name,
            Columns = [new CreateColumnApiRequest { Name = "label", Type = "text" }],
            Metadata = metadata,
        }, TestContext.Current.CancellationToken);

    [Fact]
    public async Task The_notes_given_at_creation_are_returned_on_create_and_on_every_read()
    {
        var ct = TestContext.Current.CancellationToken;
        var name = Name();

        var created = await (await CreateAsync(name, new() { ["owner"] = "writer-a", ["origin"] = "projection" })).Content.ReadFromJsonAsync<TableApiResponse>(ct);
        var read = await _client.GetFromJsonAsync<TableApiResponse>($"/api/schema/tables/{name}", ct);

        created!.Metadata.Should().BeEquivalentTo(new Dictionary<string, string> { ["owner"] = "writer-a", ["origin"] = "projection" });
        read!.Metadata.Should().BeEquivalentTo(created.Metadata);
    }

    [Fact]
    public async Task A_table_created_without_notes_reads_empty_notes()
    {
        var ct = TestContext.Current.CancellationToken;
        var name = Name();
        (await CreateAsync(name, metadata: null)).StatusCode.Should().Be(HttpStatusCode.Created);

        (await _client.GetFromJsonAsync<TableApiResponse>($"/api/schema/tables/{name}", ct))!.Metadata.Should().BeEmpty();
    }

    [Fact]
    public async Task A_table_recreated_carries_the_notes_of_the_creation_that_made_it()
    {
        var ct = TestContext.Current.CancellationToken;
        var name = Name();
        (await CreateAsync(name, new() { ["owner"] = "writer-a" })).StatusCode.Should().Be(HttpStatusCode.Created);
        (await _client.DeleteAsync($"/api/schema/tables/{name}", ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await CreateAsync(name, new() { ["owner"] = "writer-b" })).StatusCode.Should().Be(HttpStatusCode.Created);

        (await _client.GetFromJsonAsync<TableApiResponse>($"/api/schema/tables/{name}", ct))!.Metadata["owner"].Should().Be("writer-b");
    }

    [Fact]
    public async Task Notes_outside_the_bounds_are_refused_and_no_table_is_created()
    {
        var ct = TestContext.Current.CancellationToken;
        var tooMany = Enumerable.Range(0, 33).ToDictionary(i => $"k{i}", i => "v");
        var cases = new[]
        {
            tooMany,
            new Dictionary<string, string> { [" "] = "blank key" },
            new Dictionary<string, string> { [new string('k', 129)] = "long key" },
            new Dictionary<string, string> { ["long"] = new string('v', 1025) },
        };

        foreach (var metadata in cases)
        {
            var name = Name();
            var response = await CreateAsync(name, metadata);

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await response.Content.ReadFromJsonAsync<ErrorResponse>(ct))!.Code.Should().Be("VALIDATION_ERROR");
            (await _client.GetAsync($"/api/schema/tables/{name}", ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
    }

    [Fact]
    public async Task The_client_gives_the_notes_and_reads_them_back()
    {
        var ct = TestContext.Current.CancellationToken;
        var http = new HttpClient(_fixture.Api.CreateHandler()) { BaseAddress = _fixture.Api.BaseAddress };
        await using var client = new MorphDBClient(http);
        client.SetProjectId(_fixture.Api.ProjectId);
        var name = Name();

        var created = await client.Schema.CreateTableAsync(new ClientModels.CreateTableRequest
        {
            Name = name,
            Columns = [new ClientModels.CreateColumnRequest { Name = "label", Type = "text" }],
            Metadata = new() { ["owner"] = "writer-a" },
        }, ct);
        var read = await client.Schema.GetTableAsync(name, ct);

        created.Metadata.Should().ContainKey("owner").WhoseValue.Should().Be("writer-a");
        read!.Metadata.Should().BeEquivalentTo(created.Metadata);
    }
}
