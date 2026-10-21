using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using MorphDB.Npgsql.Infrastructure;
using MorphDB.Service.Models.Api;
using MorphDB.Tests.Fixtures;
using Npgsql;

namespace MorphDB.Tests.Integration.Api;

/// <summary>
/// Table definitions are kept in memory, so they must not outlive a change to them. A change made
/// through the API is seen by the next request; a change written to the definition tables by anything
/// else — another instance of the service — is seen once the database's notification arrives.
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class DefinitionCacheContractTests(ApiIntegrationFixture fixture)
{
    private readonly HttpClient _client = fixture.Api.Client;

    private async Task<string> CreateTableAsync(CancellationToken cancellationToken)
    {
        var tableName = $"defcache_{Guid.NewGuid():N}"[..28];
        using var created = await _client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = tableName,
            Columns = [new CreateColumnApiRequest { Name = "title", Type = "text", Nullable = true }],
        }, cancellationToken);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return tableName;
    }

    private async Task WaitUntilLiveAsync(CancellationToken cancellationToken)
    {
        var cache = fixture.Api.Services.GetRequiredService<MetadataCache>();
        for (var i = 0; i < 100 && !cache.IsLive; i++)
        {
            await Task.Delay(100, cancellationToken);
        }

        Assert.True(cache.IsLive, "the metadata listener should be listening, so definitions are cached");
    }

    private Task<HttpResponseMessage> FilterAsync(string table, string column, CancellationToken cancellationToken) =>
        _client.GetAsync(new Uri($"/api/data/{table}?filter={column}:eq:x", UriKind.Relative), cancellationToken);

    [Fact]
    public async Task A_column_added_through_the_api_is_readable_by_the_very_next_request()
    {
        var ct = TestContext.Current.CancellationToken;
        await WaitUntilLiveAsync(ct);
        var table = await CreateTableAsync(ct);
        using (var before = await FilterAsync(table, "title", ct))
        {
            Assert.Equal(HttpStatusCode.OK, before.StatusCode); // the definition is now cached
        }

        using var added = await _client.PostAsJsonAsync($"/api/schema/tables/{table}/columns",
            new CreateColumnApiRequest { Name = "subtitle", Type = "text", Nullable = true }, ct);
        Assert.True(added.IsSuccessStatusCode, await added.Content.ReadAsStringAsync(ct));

        using var after = await FilterAsync(table, "subtitle", ct);
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
    }

    [Fact]
    public async Task A_definition_changed_by_another_writer_is_seen_once_its_notification_arrives()
    {
        var ct = TestContext.Current.CancellationToken;
        await WaitUntilLiveAsync(ct);
        var table = await CreateTableAsync(ct);
        using (var before = await FilterAsync(table, "title", ct))
        {
            Assert.Equal(HttpStatusCode.OK, before.StatusCode); // the definition is now cached
        }

        // Another instance renames the column's logical name: written to the definition table directly.
        await using (var connection = new NpgsqlConnection(fixture.Postgres.ConnectionString))
        {
            await connection.OpenAsync(ct);
            await using var rename = new NpgsqlCommand(
                """
                UPDATE morphdb._morph_columns c SET logical_name = 'heading'
                FROM morphdb._morph_tables t
                WHERE c.table_id = t.table_id AND t.logical_name = @table AND c.logical_name = 'title'
                """, connection);
            rename.Parameters.AddWithValue("table", table);
            Assert.Equal(1, await rename.ExecuteNonQueryAsync(ct));
        }

        var status = HttpStatusCode.BadRequest;
        for (var i = 0; i < 50 && status != HttpStatusCode.OK; i++)
        {
            using var after = await FilterAsync(table, "heading", ct);
            status = after.StatusCode;
            if (status != HttpStatusCode.OK)
            {
                await Task.Delay(100, ct);
            }
        }

        Assert.Equal(HttpStatusCode.OK, status);
    }
}
