using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MorphDB.Service.Models.Api;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration.Api;

/// <summary>
/// The OData door gives an entity the tag the REST door gives its row (OData 4.01 §11.4.1.1): the <c>ETag</c> header
/// and the <c>@odata.etag</c> annotation carry the strong tag <c>"3"</c> of the row's version, and a PATCH or DELETE
/// that names a tag the row has moved past is answered <c>412</c> in OData's error shape and changes nothing.
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class ODataRowEntityTagContractTests
{
    private readonly HttpClient _client;

    public ODataRowEntityTagContractTests(ApiIntegrationFixture fixture) => _client = fixture.Api.Client;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<(string Set, string Table, Guid Id)> TableWithOneRowAsync()
    {
        var table = $"odataetag_{Guid.NewGuid():N}"[..30];
        (await _client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = table,
            Columns =
            [
                new CreateColumnApiRequest { Name = "code", Type = "text", Nullable = false, Unique = true },
                new CreateColumnApiRequest { Name = "label", Type = "text", Nullable = false },
            ],
        }, Ct)).EnsureSuccessStatusCode();
        (await _client.PostAsJsonAsync($"/api/data/{table}", new { code = "k", label = "v1" }, Ct)).EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await _client.GetStringAsync($"/api/data/{table}?filter=code:eq:k", Ct));
        var id = doc.RootElement.GetProperty("data")[0].GetProperty("data").GetProperty("_id").GetGuid();
        var set = string.Concat(table.Split('_').Select(p => p.Length > 0 ? char.ToUpperInvariant(p[0]) + p[1..].ToLowerInvariant() : p));
        return (set, table, id);
    }

    private async Task<string> LabelAsync(string table, Guid id)
    {
        using var doc = JsonDocument.Parse(await _client.GetStringAsync($"/api/data/{table}/{id}", Ct));
        return doc.RootElement.GetProperty("data").GetProperty("label").GetString()!;
    }

    private Task<HttpResponseMessage> PatchAsync(string set, Guid id, string label, string? ifMatch)
    {
        var request = new HttpRequestMessage(HttpMethod.Patch, $"/odata/{set}({id})") { Content = JsonContent.Create(new { label }) };
        if (ifMatch is not null)
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        return _client.SendAsync(request, Ct);
    }

    private Task<HttpResponseMessage> DeleteAsync(string set, Guid id, string? ifMatch)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, $"/odata/{set}({id})");
        if (ifMatch is not null)
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        return _client.SendAsync(request, Ct);
    }

    private static string? ETagOf(HttpResponseMessage response) =>
        response.Headers.TryGetValues("ETag", out var values) ? values.Single() : null;

    [Fact]
    public async Task An_entity_read_names_its_tag_in_the_header_and_the_annotation_and_a_write_moves_both()
    {
        var (set, _, id) = await TableWithOneRowAsync();

        using var read = await _client.GetAsync($"/odata/{set}({id})", Ct);
        ETagOf(read).Should().Be("\"1\"");
        using (var doc = JsonDocument.Parse(await read.Content.ReadAsStringAsync(Ct)))
            doc.RootElement.GetProperty("@odata.etag").GetString().Should().Be("\"1\"");

        using var patched = await PatchAsync(set, id, "v2", ifMatch: "\"1\"");
        patched.StatusCode.Should().Be(HttpStatusCode.OK);
        ETagOf(patched).Should().Be("\"2\"");
        using var patchedDoc = JsonDocument.Parse(await patched.Content.ReadAsStringAsync(Ct));
        patchedDoc.RootElement.GetProperty("@odata.etag").GetString().Should().Be("\"2\"");
    }

    [Fact]
    public async Task A_patch_naming_a_stale_tag_is_412_in_odatas_error_shape_and_changes_nothing()
    {
        var (set, table, id) = await TableWithOneRowAsync();
        (await PatchAsync(set, id, "someone-else", ifMatch: null)).EnsureSuccessStatusCode(); // version 2

        using var response = await PatchAsync(set, id, "mine", "\"1\"");

        response.StatusCode.Should().Be(HttpStatusCode.PreconditionFailed);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        doc.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("PRECONDITION_FAILED");
        (await LabelAsync(table, id)).Should().Be("someone-else");
    }

    [Fact]
    public async Task A_delete_naming_a_stale_tag_is_412_and_the_current_tag_deletes_it()
    {
        var (set, table, id) = await TableWithOneRowAsync();
        (await PatchAsync(set, id, "moved", ifMatch: null)).EnsureSuccessStatusCode(); // version 2

        using var stale = await DeleteAsync(set, id, "\"1\"");
        stale.StatusCode.Should().Be(HttpStatusCode.PreconditionFailed);
        (await LabelAsync(table, id)).Should().Be("moved");

        using var current = await DeleteAsync(set, id, "\"2\"");
        current.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Theory]
    [InlineData("W/\"1\"")]
    [InlineData("\"abc\"")]
    public async Task A_weak_or_foreign_tag_never_matches(string tag)
    {
        var (set, table, id) = await TableWithOneRowAsync();

        using var response = await PatchAsync(set, id, "changed", tag);

        response.StatusCode.Should().Be(HttpStatusCode.PreconditionFailed);
        (await LabelAsync(table, id)).Should().Be("v1");
    }

    [Fact]
    public async Task A_missing_entity_is_404_whatever_the_header_says()
    {
        var (set, _, _) = await TableWithOneRowAsync();

        foreach (var tag in new[] { "\"1\"", "W/\"1\"" })
        {
            using var patch = await PatchAsync(set, Guid.NewGuid(), "x", tag);
            using var delete = await DeleteAsync(set, Guid.NewGuid(), tag);
            patch.StatusCode.Should().Be(HttpStatusCode.NotFound, tag);
            delete.StatusCode.Should().Be(HttpStatusCode.NotFound, tag);
        }
    }

    [Fact]
    public async Task A_batch_item_with_If_Match_is_still_refused_because_a_batch_item_evaluates_no_precondition()
    {
        var (set, table, id) = await TableWithOneRowAsync();

        using var response = await _client.PostAsJsonAsync("/odata/$batch", new
        {
            requests = new[]
            {
                new
                {
                    id = "1",
                    method = "PATCH",
                    url = $"{set}({id})",
                    headers = new Dictionary<string, string> { ["If-Match"] = "\"1\"", ["Content-Type"] = "application/json" },
                    body = new { label = "changed" },
                },
            },
        }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(Ct)).Should().Contain("If-Match");
        (await LabelAsync(table, id)).Should().Be("v1");
    }
}
