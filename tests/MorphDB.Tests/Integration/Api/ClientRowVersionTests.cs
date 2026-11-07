using System.Net.Http.Json;
using MorphDB.Client;
using MorphDB.Service.Models.Api;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration.Api;

/// <summary>
/// The .NET client carries the row's version the way the wire does: a record names its <c>Version</c>, an update or
/// delete may name the <c>expectedVersion</c> it read, and a row that has moved past it surfaces as a typed
/// <see cref="MorphDBPreconditionFailedException"/> with nothing changed.
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class ClientRowVersionTests
{
    private readonly ApiIntegrationFixture _fixture;

    public ClientRowVersionTests(ApiIntegrationFixture fixture) => _fixture = fixture;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<(MorphDBClient Client, string Table, Guid Id)> RowAsync()
    {
        var http = _fixture.Api.Client;
        var table = $"clientver_{Guid.NewGuid():N}"[..28];
        (await http.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = table,
            Columns = [new CreateColumnApiRequest { Name = "label", Type = "text", Nullable = false }],
        }, Ct)).EnsureSuccessStatusCode();

        var client = new MorphDBClient(new HttpClient(_fixture.Api.CreateHandler()) { BaseAddress = _fixture.Api.BaseAddress });
        client.SetProjectId(_fixture.Api.ProjectId);
        var created = await client.Data.InsertAsync(table, new Dictionary<string, object?> { ["label"] = "v1" }, Ct);
        return (client, table, created.Id);
    }

    [Fact]
    public async Task A_record_names_its_version_and_an_update_naming_it_goes_through()
    {
        var (client, table, id) = await RowAsync();
        await using var _ = client;

        var read = await client.Data.GetByIdAsync(table, id, Ct);
        read!.Version.Should().Be(1);

        var updated = await client.Data.UpdateAsync(table, id, new Dictionary<string, object?> { ["label"] = "v2" }, read.Version!.Value, Ct);

        updated.Version.Should().Be(2, "the update's answer names the new version, so the next write needs no read");
    }

    [Fact]
    public async Task An_update_naming_a_version_the_row_has_moved_past_throws_and_changes_nothing()
    {
        var (client, table, id) = await RowAsync();
        await using var _ = client;
        await client.Data.UpdateAsync(table, id, new Dictionary<string, object?> { ["label"] = "someone-else" }, Ct); // version 2

        var act = () => client.Data.UpdateAsync(table, id, new Dictionary<string, object?> { ["label"] = "mine" }, expectedVersion: 1, Ct);

        var ex = (await act.Should().ThrowAsync<MorphDBPreconditionFailedException>()).Which;
        ex.ErrorCode.Should().Be("PRECONDITION_FAILED");
        ex.StatusCode.Should().Be(System.Net.HttpStatusCode.PreconditionFailed);
        (await client.Data.GetByIdAsync(table, id, Ct))!.Data["label"]!.ToString().Should().Be("someone-else");
    }

    [Fact]
    public async Task A_delete_naming_a_stale_version_throws_and_keeps_the_row_and_the_current_version_deletes_it()
    {
        var (client, table, id) = await RowAsync();
        await using var _ = client;
        await client.Data.UpdateAsync(table, id, new Dictionary<string, object?> { ["label"] = "moved" }, Ct); // version 2

        var stale = () => client.Data.DeleteAsync(table, id, expectedVersion: 1, Ct);
        await stale.Should().ThrowAsync<MorphDBPreconditionFailedException>();
        (await client.Data.GetByIdAsync(table, id, Ct)).Should().NotBeNull();

        await client.Data.DeleteAsync(table, id, expectedVersion: 2, Ct);
        (await client.Data.GetByIdAsync(table, id, Ct)).Should().BeNull();
    }

    [Fact]
    public async Task Without_an_expected_version_the_calls_are_unconditional_as_before()
    {
        var (client, table, id) = await RowAsync();
        await using var _ = client;

        await client.Data.UpdateAsync(table, id, new Dictionary<string, object?> { ["label"] = "a" }, Ct);
        await client.Data.UpdateAsync(table, id, new Dictionary<string, object?> { ["label"] = "b" }, Ct);
        await client.Data.DeleteAsync(table, id, Ct);

        (await client.Data.GetByIdAsync(table, id, Ct)).Should().BeNull();
    }
}
