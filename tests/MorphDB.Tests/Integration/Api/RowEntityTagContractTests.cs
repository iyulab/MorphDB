using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MorphDB.Service.Models.Api;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration.Api;

/// <summary>
/// A row has an entity tag — its <c>_version</c> as a strong tag (<c>"3"</c>) — and the single-row update and delete
/// take it back as <c>If-Match</c> (RFC 9110 §13.1.1). A read names the tag; a write that names a tag the row has
/// moved past is answered <c>412</c> and changes nothing; the compare and the write are one statement, so two writers
/// holding the same tag cannot both win. The other conditional headers are still refused on every door
/// (<see cref="WriteDoorUnknownInputContractTests"/>).
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class RowEntityTagContractTests
{
    private readonly HttpClient _client;

    public RowEntityTagContractTests(ApiIntegrationFixture fixture) => _client = fixture.Api.Client;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<(string Table, Guid Id)> TableWithOneRowAsync(bool versioning = true)
    {
        var table = $"etag_{Guid.NewGuid():N}"[..28];
        (await _client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = table,
            SystemColumns = new SystemColumnOptionsApiRequest { Versioning = versioning },
            Columns =
            [
                new CreateColumnApiRequest { Name = "code", Type = "text", Nullable = false, Unique = true },
                new CreateColumnApiRequest { Name = "label", Type = "text", Nullable = false },
            ],
        }, Ct)).EnsureSuccessStatusCode();
        (await _client.PostAsJsonAsync($"/api/data/{table}", new { code = "k", label = "v1" }, Ct)).EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await _client.GetStringAsync($"/api/data/{table}?filter=code:eq:k", Ct));
        return (table, doc.RootElement.GetProperty("data")[0].GetProperty("data").GetProperty("_id").GetGuid());
    }

    private async Task<string> LabelAsync(string table, Guid id)
    {
        using var doc = JsonDocument.Parse(await _client.GetStringAsync($"/api/data/{table}/{id}", Ct));
        return doc.RootElement.GetProperty("data").GetProperty("label").GetString()!;
    }

    private Task<HttpResponseMessage> PatchAsync(string table, Guid id, string label, string? ifMatch)
    {
        var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/data/{table}/{id}") { Content = JsonContent.Create(new { label }) };
        if (ifMatch is not null)
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        return _client.SendAsync(request, Ct);
    }

    private Task<HttpResponseMessage> DeleteAsync(string table, Guid id, string? ifMatch)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/data/{table}/{id}");
        if (ifMatch is not null)
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        return _client.SendAsync(request, Ct);
    }

    private static string? ETagOf(HttpResponseMessage response) =>
        response.Headers.TryGetValues("ETag", out var values) ? values.Single() : null;

    private static async Task<string> CodeOfAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        return doc.RootElement.GetProperty("code").GetString()!;
    }

    [Fact]
    public async Task A_row_read_names_its_version_as_a_strong_entity_tag_and_a_write_moves_it()
    {
        var (table, id) = await TableWithOneRowAsync();

        using var read = await _client.GetAsync($"/api/data/{table}/{id}", Ct);
        ETagOf(read).Should().Be("\"1\"");

        using var patched = await PatchAsync(table, id, "v2", ifMatch: null);
        patched.StatusCode.Should().Be(HttpStatusCode.OK);
        ETagOf(patched).Should().Be("\"2\"", "the write's answer names the row's new version, so the next write needs no read");
    }

    [Fact]
    public async Task An_update_naming_the_current_tag_goes_through()
    {
        var (table, id) = await TableWithOneRowAsync();

        using var response = await PatchAsync(table, id, "changed", "\"1\"");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await LabelAsync(table, id)).Should().Be("changed");
    }

    [Fact]
    public async Task An_update_naming_a_tag_the_row_has_moved_past_is_412_and_changes_nothing()
    {
        var (table, id) = await TableWithOneRowAsync();
        (await PatchAsync(table, id, "someone-else", ifMatch: null)).EnsureSuccessStatusCode(); // now version 2

        using var response = await PatchAsync(table, id, "mine", "\"1\"");

        response.StatusCode.Should().Be(HttpStatusCode.PreconditionFailed);
        (await CodeOfAsync(response)).Should().Be("PRECONDITION_FAILED");
        (await LabelAsync(table, id)).Should().Be("someone-else");
    }

    [Fact]
    public async Task A_delete_naming_a_stale_tag_is_412_and_keeps_the_row_and_the_current_tag_deletes_it()
    {
        var (table, id) = await TableWithOneRowAsync();
        (await PatchAsync(table, id, "moved", ifMatch: null)).EnsureSuccessStatusCode(); // version 2

        using var stale = await DeleteAsync(table, id, "\"1\"");
        stale.StatusCode.Should().Be(HttpStatusCode.PreconditionFailed);
        (await LabelAsync(table, id)).Should().Be("moved");

        using var current = await DeleteAsync(table, id, "\"2\"");
        current.StatusCode.Should().Be(HttpStatusCode.NoContent);
        using var gone = await _client.GetAsync($"/api/data/{table}/{id}", Ct);
        gone.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("W/\"1\"")]
    [InlineData("\"abc\"")]
    public async Task A_weak_or_foreign_tag_never_matches_under_the_strong_comparison_If_Match_requires(string tag)
    {
        var (table, id) = await TableWithOneRowAsync();

        using var response = await PatchAsync(table, id, "changed", tag);

        response.StatusCode.Should().Be(HttpStatusCode.PreconditionFailed);
        (await LabelAsync(table, id)).Should().Be("v1");
    }

    /// <summary>The tag is the version in plain decimal; compared strongly, "01" is not a spelling of "1" but a tag no row carries.</summary>
    [Theory]
    [InlineData("\"01\"")]
    [InlineData("\"001\"")]
    [InlineData("\"1.0\"")]
    public async Task A_tag_that_is_not_the_exact_decimal_of_the_version_does_not_match_it(string tag)
    {
        var (table, id) = await TableWithOneRowAsync(); // version 1

        using var patched = await PatchAsync(table, id, "changed", tag);
        using var deleted = await DeleteAsync(table, id, tag);

        patched.StatusCode.Should().Be(HttpStatusCode.PreconditionFailed, tag);
        deleted.StatusCode.Should().Be(HttpStatusCode.PreconditionFailed, tag);
        (await LabelAsync(table, id)).Should().Be("v1", "neither write was applied");
    }

    /// <summary>An update and a delete that name a tag the row is not at say the same thing: what they expected, what the row is at.</summary>
    [Fact]
    public async Task A_conflict_names_the_current_version_the_same_way_on_an_update_and_a_delete()
    {
        var (table, id) = await TableWithOneRowAsync();
        (await PatchAsync(table, id, "moved", ifMatch: null)).EnsureSuccessStatusCode(); // version 2

        using var patched = await PatchAsync(table, id, "mine", "\"9\"");
        using var deleted = await DeleteAsync(table, id, "\"9\"");

        foreach (var response in new[] { patched, deleted })
        {
            response.StatusCode.Should().Be(HttpStatusCode.PreconditionFailed);
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
            doc.RootElement.GetProperty("message").GetString().Should().Contain("expected 9").And.Contain("current version is 2");
        }
    }

    /// <summary>
    /// <c>expectedVersion</c> is what a caller reaches for first; the refusal is where it learns the header that does the
    /// job — on every route that refuses it, not only the writes.
    /// </summary>
    [Fact]
    public async Task The_refusal_of_expected_version_names_the_If_Match_header()
    {
        var (table, id) = await TableWithOneRowAsync();
        using var patch = new HttpRequestMessage(HttpMethod.Patch, $"/api/data/{table}/{id}?expectedVersion=1") { Content = JsonContent.Create(new { label = "x" }) };

        var responses = new[]
        {
            await _client.SendAsync(patch, Ct),
            await _client.DeleteAsync($"/api/data/{table}/{id}?expectedVersion=1", Ct),
            await _client.GetAsync($"/api/data/{table}/{id}?expectedVersion=1", Ct),
            await _client.PostAsJsonAsync($"/api/data/{table}?expectedVersion=1", new { code = "z", label = "z" }, Ct),
        };

        foreach (var response in responses)
        {
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
            doc.RootElement.GetProperty("message").GetString().Should().Contain("expectedVersion").And.Contain("If-Match");
        }

        (await LabelAsync(table, id)).Should().Be("v1");
    }

    [Fact]
    public async Task A_star_needs_only_that_the_row_exists()
    {
        var (table, id) = await TableWithOneRowAsync();
        (await PatchAsync(table, id, "moved", ifMatch: null)).EnsureSuccessStatusCode();

        using var response = await PatchAsync(table, id, "star", "*");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("\"1\", \"2\"")]
    [InlineData("1")]
    [InlineData(",")]
    public async Task An_If_Match_the_service_cannot_honor_is_400_and_changes_nothing(string value)
    {
        var (table, id) = await TableWithOneRowAsync();

        using var response = await PatchAsync(table, id, "changed", value);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await LabelAsync(table, id)).Should().Be("v1");
    }

    [Fact]
    public async Task A_version_tag_on_a_table_that_keeps_no_version_is_refused_not_ignored()
    {
        var (table, id) = await TableWithOneRowAsync(versioning: false);

        using var read = await _client.GetAsync($"/api/data/{table}/{id}", Ct);
        ETagOf(read).Should().BeNull("a table that keeps no version has no tag to name");

        using var response = await PatchAsync(table, id, "changed", "\"1\"");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await LabelAsync(table, id)).Should().Be("v1");
    }

    [Fact]
    public async Task A_missing_row_is_still_404_with_or_without_the_header()
    {
        var (table, _) = await TableWithOneRowAsync();

        using var response = await PatchAsync(table, Guid.NewGuid(), "x", "\"1\"");
        using var delete = await DeleteAsync(table, Guid.NewGuid(), "\"1\"");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        delete.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // RFC 9110 §13.2.1: a response that would not have been a 2xx or 412 without the header ignores the header.
        using var weakPatch = await PatchAsync(table, Guid.NewGuid(), "x", "W/\"1\"");
        using var weakDelete = await DeleteAsync(table, Guid.NewGuid(), "W/\"1\"");
        weakPatch.StatusCode.Should().Be(HttpStatusCode.NotFound);
        weakDelete.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Two_writers_holding_the_same_tag_cannot_both_win()
    {
        var (table, id) = await TableWithOneRowAsync();

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => PatchAsync(table, id, $"writer-{i}", "\"1\"")));

        results.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1, "the compare and the write are one statement");
        results.Count(r => r.StatusCode == HttpStatusCode.PreconditionFailed).Should().Be(7);
        using var doc = JsonDocument.Parse(await _client.GetStringAsync($"/api/data/{table}/{id}", Ct));
        doc.RootElement.GetProperty("data").GetProperty("_version").GetInt64().Should().Be(2);
    }
}
