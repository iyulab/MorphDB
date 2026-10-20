using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MorphDB.Service.Models.Api;
using MorphDB.Service.OData;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration.Api;

/// <summary>
/// Integration tests for OData API endpoints.
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class ODataApiTests
{
    private readonly ApiIntegrationFixture _fixture;
    private readonly HttpClient _client;

    public ODataApiTests(ApiIntegrationFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.Api.Client;
    }

    private async Task<string> SetupTestTableAsync()
    {
        var tableName = $"odata_test_{Guid.NewGuid():N}"[..30];
        await _client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = tableName,
            Columns =
            [
                new CreateColumnApiRequest { Name = "name", Type = "text", Nullable = false },
                new CreateColumnApiRequest { Name = "email", Type = "text", Nullable = false },
                new CreateColumnApiRequest { Name = "age", Type = "integer", Nullable = true },
                new CreateColumnApiRequest { Name = "is_active", Type = "boolean", Nullable = false }
            ]
        });
        return tableName;
    }

    private async Task<Guid> InsertTestRecordAsync(string tableName, string name, string email, int? age, bool isActive)
    {
        var data = new Dictionary<string, object?>
        {
            ["name"] = name,
            ["email"] = email,
            ["age"] = age,
            ["is_active"] = isActive
        };

        var response = await _client.PostAsJsonAsync($"/api/data/{tableName}", data);
        var result = await response.Content.ReadFromJsonAsync<DataRecordResponse>();
        return result!.Id;
    }

    #region Metadata Tests

    [Fact]
    public async Task GetMetadata_ShouldReturnXml()
    {
        // Arrange
        await SetupTestTableAsync();

        // Act
        var response = await _client.GetAsync("/odata/$metadata", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/xml");
    }

    #endregion

    #region Query Tests

    [Fact]
    public async Task GetEntitySet_ShouldReturnODataResponse()
    {
        // Arrange
        var tableName = await SetupTestTableAsync();
        await InsertTestRecordAsync(tableName, "John", "john@example.com", 30, true);
        await InsertTestRecordAsync(tableName, "Jane", "jane@example.com", 25, true);

        var entitySetName = ToPascalCase(tableName);

        // Act
        var response = await _client.GetAsync($"/odata/{entitySetName}", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        content.Should().Contain("@odata.context");
        content.Should().Contain("value");
    }

    [Fact]
    public async Task GetEntitySet_WithTop_ShouldLimitResults()
    {
        // Arrange
        var tableName = await SetupTestTableAsync();
        for (int i = 0; i < 5; i++)
        {
            await InsertTestRecordAsync(tableName, $"User{i}", $"user{i}@example.com", 20 + i, true);
        }

        var entitySetName = ToPascalCase(tableName);

        // Act
        var response = await _client.GetAsync($"/odata/{entitySetName}?$top=2", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var jsonDoc = await response.Content.ReadFromJsonAsync<JsonDocument>(TestContext.Current.CancellationToken);
        var valueArray = jsonDoc!.RootElement.GetProperty("value");
        valueArray.GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task GetEntitySet_WithCount_ShouldIncludeTotalCount()
    {
        // Arrange
        var tableName = await SetupTestTableAsync();
        for (int i = 0; i < 5; i++)
        {
            await InsertTestRecordAsync(tableName, $"User{i}", $"user{i}@example.com", 20 + i, true);
        }

        var entitySetName = ToPascalCase(tableName);

        // Act
        var response = await _client.GetAsync($"/odata/{entitySetName}?$count=true", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var jsonDoc = await response.Content.ReadFromJsonAsync<JsonDocument>(TestContext.Current.CancellationToken);
        jsonDoc!.RootElement.TryGetProperty("@odata.count", out var countProp).Should().BeTrue();
        countProp.GetInt64().Should().Be(5);
    }

    [Fact]
    public async Task GetEntitySet_WithFilter_ShouldFilterResults()
    {
        // Arrange
        var tableName = await SetupTestTableAsync();
        await InsertTestRecordAsync(tableName, "John", "john@example.com", 30, true);
        await InsertTestRecordAsync(tableName, "Jane", "jane@example.com", 25, false);

        var entitySetName = ToPascalCase(tableName);

        // Act
        var response = await _client.GetAsync($"/odata/{entitySetName}?$filter=is_active eq true", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var jsonDoc = await response.Content.ReadFromJsonAsync<JsonDocument>(TestContext.Current.CancellationToken);
        var valueArray = jsonDoc!.RootElement.GetProperty("value");
        valueArray.GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task GetEntitySet_WithOrderBy_ShouldSortResults()
    {
        // Arrange
        var tableName = await SetupTestTableAsync();
        await InsertTestRecordAsync(tableName, "Charlie", "charlie@example.com", 35, true);
        await InsertTestRecordAsync(tableName, "Alice", "alice@example.com", 25, true);
        await InsertTestRecordAsync(tableName, "Bob", "bob@example.com", 30, true);

        var entitySetName = ToPascalCase(tableName);

        // Act
        var response = await _client.GetAsync($"/odata/{entitySetName}?$orderby=name", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var jsonDoc = await response.Content.ReadFromJsonAsync<JsonDocument>(TestContext.Current.CancellationToken);
        var valueArray = jsonDoc!.RootElement.GetProperty("value");
        var firstItem = valueArray[0];
        firstItem.GetProperty("name").GetString().Should().Be("Alice");
    }

    [Fact]
    public async Task GetEntitySet_WithSelect_ShouldReturnSelectedFields()
    {
        // Arrange
        var tableName = await SetupTestTableAsync();
        await InsertTestRecordAsync(tableName, "John", "john@example.com", 30, true);

        var entitySetName = ToPascalCase(tableName);

        // Act
        var response = await _client.GetAsync($"/odata/{entitySetName}?$select=name,email", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var jsonDoc = await response.Content.ReadFromJsonAsync<JsonDocument>(TestContext.Current.CancellationToken);
        var valueArray = jsonDoc!.RootElement.GetProperty("value");
        var firstItem = valueArray[0];
        firstItem.TryGetProperty("name", out _).Should().BeTrue();
        firstItem.TryGetProperty("email", out _).Should().BeTrue();
    }

    #endregion

    #region Single Entity Tests

    [Fact]
    public async Task GetEntity_ById_ShouldReturnSingleEntity()
    {
        // Arrange
        var tableName = await SetupTestTableAsync();
        var id = await InsertTestRecordAsync(tableName, "John", "john@example.com", 30, true);

        var entitySetName = ToPascalCase(tableName);

        // Act
        var response = await _client.GetAsync($"/odata/{entitySetName}({id})", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var jsonDoc = await response.Content.ReadFromJsonAsync<JsonDocument>(TestContext.Current.CancellationToken);
        jsonDoc!.RootElement.TryGetProperty("@odata.context", out _).Should().BeTrue();
        jsonDoc.RootElement.TryGetProperty("value", out _).Should().BeFalse(
            "a single entity carries its properties at the top level; `value` is a collection's");
        jsonDoc.RootElement.GetProperty("name").GetString().Should().Be("John");
    }

    [Fact]
    public async Task GetEntity_NotFound_ShouldReturn404()
    {
        // Arrange
        var tableName = await SetupTestTableAsync();
        var entitySetName = ToPascalCase(tableName);
        var nonExistentId = Guid.NewGuid();

        // Act
        var response = await _client.GetAsync($"/odata/{entitySetName}({nonExistentId})", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region CUD Operations

    [Fact]
    public async Task CreateEntity_ShouldReturnCreated()
    {
        // Arrange
        var tableName = await SetupTestTableAsync();
        var entitySetName = ToPascalCase(tableName);
        var data = new Dictionary<string, object?>
        {
            ["name"] = "New User",
            ["email"] = "new@example.com",
            ["age"] = 28,
            ["is_active"] = true
        };

        // Act
        var response = await _client.PostAsJsonAsync($"/odata/{entitySetName}", data, TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var jsonDoc = await response.Content.ReadFromJsonAsync<JsonDocument>(TestContext.Current.CancellationToken);
        jsonDoc!.RootElement.GetProperty("@odata.context").GetString().Should().EndWith("/$entity");
        jsonDoc.RootElement.GetProperty("name").GetString().Should().Be("New User");
    }

    [Fact]
    public async Task UpdateEntity_ShouldReturnOk()
    {
        // Arrange
        var tableName = await SetupTestTableAsync();
        var id = await InsertTestRecordAsync(tableName, "John", "john@example.com", 30, true);
        var entitySetName = ToPascalCase(tableName);

        var updateData = new Dictionary<string, object?>
        {
            ["name"] = "John Updated"
        };

        // Act
        var request = new HttpRequestMessage(HttpMethod.Patch, $"/odata/{entitySetName}({id})")
        {
            Content = JsonContent.Create(updateData)
        };
        var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var jsonDoc = await response.Content.ReadFromJsonAsync<JsonDocument>(TestContext.Current.CancellationToken);
        jsonDoc!.RootElement.GetProperty("name").GetString().Should().Be("John Updated");
    }

    [Fact]
    public async Task DeleteEntity_ShouldReturnNoContent()
    {
        // Arrange
        var tableName = await SetupTestTableAsync();
        var id = await InsertTestRecordAsync(tableName, "John", "john@example.com", 30, true);
        var entitySetName = ToPascalCase(tableName);

        // Act
        var response = await _client.DeleteAsync($"/odata/{entitySetName}({id})", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify deletion
        var getResponse = await _client.GetAsync($"/odata/{entitySetName}({id})", TestContext.Current.CancellationToken);
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region Batch Tests

    [Fact]
    public async Task BatchRequest_ShouldExecuteMultipleOperations()
    {
        // Arrange
        var tableName = await SetupTestTableAsync();
        var id = await InsertTestRecordAsync(tableName, "John", "john@example.com", 30, true);
        var entitySetName = ToPascalCase(tableName);

        var batchRequest = new ODataBatchRequest
        {
            Requests =
            [
                new ODataBatchRequestItem
                {
                    Id = "1",
                    Method = "POST",
                    Url = entitySetName,
                    Body = JsonSerializer.SerializeToElement(new Dictionary<string, object?>
                    {
                        ["name"] = "New User",
                        ["email"] = "new@example.com",
                        ["is_active"] = true
                    })
                },
                new ODataBatchRequestItem
                {
                    Id = "2",
                    Method = "GET",
                    Url = $"{entitySetName}({id})"
                },
                new ODataBatchRequestItem
                {
                    Id = "3",
                    Method = "DELETE",
                    Url = $"{entitySetName}({id})"
                }
            ]
        };

        // Act
        var response = await _client.PostAsJsonAsync("/odata/$batch", batchRequest, TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var batchResponse = await response.Content.ReadFromJsonAsync<ODataBatchResponse>(TestContext.Current.CancellationToken);
        batchResponse.Should().NotBeNull();
        batchResponse!.Responses.Should().HaveCount(3);

        batchResponse.Responses[0].Status.Should().Be(201); // POST created
        batchResponse.Responses[1].Status.Should().Be(200); // GET success
        batchResponse.Responses[2].Status.Should().Be(204); // DELETE no content
    }

    /// <summary>
    /// A batch request is answered exactly as the same request on its own: a collection read takes
    /// the query options in its URL, a create carries its location, an entity read is the entity
    /// object, and a delete answers nothing.
    /// </summary>
    [Fact]
    public async Task A_batch_request_answers_as_the_same_request_on_its_own()
    {
        var ct = TestContext.Current.CancellationToken;
        var tableName = await SetupTestTableAsync();
        var john = await InsertTestRecordAsync(tableName, "John", "john@example.com", 30, true);
        await InsertTestRecordAsync(tableName, "Jane", "jane@example.com", 25, false);
        var set = ToPascalCase(tableName);

        var response = await _client.PostAsJsonAsync("/odata/$batch", new
        {
            requests = new object[]
            {
                new { id = "list", method = "get", url = $"{set}?$filter=age gt 26&$select=name&$count=true" },
                new { id = "one", method = "GET", url = $"/odata/{set}({john})" },
                new { id = "new", method = "POST", url = set, body = new { name = "Ann", email = "ann@example.com", age = 41, is_active = true } },
                new { id = "gone", method = "DELETE", url = $"{set}({john})", dependsOn = DependsOn("one") },
                new { id = "put", method = "PUT", url = $"{set}({john})", body = new { name = "x" } },
            },
        }, ct);
        var payload = await response.Content.ReadAsStringAsync(ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK, payload);
        var items = JsonDocument.Parse(payload).RootElement.GetProperty("responses");

        var list = items[0].GetProperty("body");
        items[0].GetProperty("status").GetInt32().Should().Be(200);
        list.GetProperty("@odata.count").GetInt64().Should().Be(1);
        list.GetProperty("value")[0].GetProperty("name").GetString().Should().Be("John");

        items[1].GetProperty("body").GetProperty("name").GetString().Should().Be("John", "an entity is the entity object");

        items[2].GetProperty("status").GetInt32().Should().Be(201);
        items[2].GetProperty("headers").GetProperty("location").GetString().Should().StartWith($"/odata/{set}(");
        items[2].GetProperty("body").GetProperty("name").GetString().Should().Be("Ann");

        items[3].GetProperty("status").GetInt32().Should().Be(204);
        items[3].TryGetProperty("body", out _).Should().BeFalse();

        items[4].GetProperty("status").GetInt32().Should().Be(405, "an entity is replaced by PATCH here, as on its own route");
    }

    /// <summary>
    /// <c>$count</c> counts the rows <c>$filter</c> selects — it counted the whole set — and a read
    /// without <c>$top</c> that the server cuts at its page size says where the next page is, so
    /// following <c>@odata.nextLink</c> reads every row once. It used to stop at 100 rows silently.
    /// </summary>
    [Fact]
    public async Task A_collection_read_counts_what_it_filters_and_pages_with_a_next_link()
    {
        var ct = TestContext.Current.CancellationToken;
        var tableName = await SetupTestTableAsync();
        var rows = Enumerable.Range(0, 105).Select(i => new Dictionary<string, object?>
        {
            ["name"] = $"P{i:000}",
            ["email"] = $"p{i}@example.com",
            ["age"] = i,
            ["is_active"] = i % 2 == 0,
        }).ToList();
        (await _client.PostAsJsonAsync($"/api/batch/data/{tableName}/insert", rows, ct)).EnsureSuccessStatusCode();
        var set = ToPascalCase(tableName);

        var filtered = await _client.GetFromJsonAsync<JsonElement>($"/odata/{set}?$filter=age lt 10&$count=true", ct);
        filtered.GetProperty("@odata.count").GetInt64().Should().Be(10);
        filtered.TryGetProperty("@odata.nextLink", out _).Should().BeFalse();

        var seen = new List<string>();
        var url = $"/odata/{set}?$count=true";
        for (var page = 0; page < 3 && url is not null; page++)
        {
            var body = await _client.GetFromJsonAsync<JsonElement>(url, ct);
            body.GetProperty("@odata.count").GetInt64().Should().Be(105);
            seen.AddRange(body.GetProperty("value").EnumerateArray().Select(r => r.GetProperty("name").GetString()!));
            url = body.TryGetProperty("@odata.nextLink", out var next) ? new Uri(next.GetString()!).PathAndQuery : null;
        }

        url.Should().BeNull("the second page is the last");
        seen.Should().HaveCount(105).And.OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task A_request_whose_dependency_failed_is_not_run()
    {
        var ct = TestContext.Current.CancellationToken;
        var set = ToPascalCase(await SetupTestTableAsync());

        var response = await _client.PostAsJsonAsync("/odata/$batch", new
        {
            requests = new object[]
            {
                new { id = "a", method = "GET", url = $"{set}({Guid.NewGuid()})" },
                new { id = "b", method = "POST", url = set, body = new { name = "Never", email = "never@example.com", is_active = true }, dependsOn = DependsOn("a") },
            },
        }, ct);
        var items = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct)).RootElement.GetProperty("responses");

        items[0].GetProperty("status").GetInt32().Should().Be(404);
        items[1].GetProperty("status").GetInt32().Should().Be(424);
        var all = await _client.GetFromJsonAsync<JsonElement>($"/odata/{set}?$count=true", ct);
        all.GetProperty("@odata.count").GetInt64().Should().Be(0, "the dependent create did not run");
    }

    private async Task<(int Status, JsonElement[] Items)> BatchAsync(object[] requests, CancellationToken ct)
    {
        var response = await _client.PostAsJsonAsync("/odata/$batch", new { requests }, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        return ((int)response.StatusCode, response.IsSuccessStatusCode
            ? [.. JsonDocument.Parse(text).RootElement.GetProperty("responses").EnumerateArray()]
            : []);
    }

    private async Task<long> CountAsync(string set, CancellationToken ct) =>
        (await _client.GetFromJsonAsync<JsonElement>($"/odata/{set}?$count=true", ct)).GetProperty("@odata.count").GetInt64();

    private static object Create(string id, string set, string name, string? group = null, string[]? dependsOn = null) =>
        new { id, method = "POST", url = set, atomicityGroup = group, dependsOn, body = new { name, email = $"{name}@example.com", is_active = true } };

    [Fact]
    public async Task An_atomicity_group_is_applied_whole()
    {
        var ct = TestContext.Current.CancellationToken;
        var set = ToPascalCase(await SetupTestTableAsync());

        var (status, items) = await BatchAsync([Create("1", set, "a", "g"), Create("2", set, "b", "g")], ct);

        status.Should().Be(200);
        items.Select(i => i.GetProperty("status").GetInt32()).Should().Equal(201, 201);
        (await CountAsync(set, ct)).Should().Be(2);
    }

    [Fact]
    public async Task When_a_request_of_an_atomicity_group_fails_nothing_of_the_group_is_kept()
    {
        var ct = TestContext.Current.CancellationToken;
        var set = ToPascalCase(await SetupTestTableAsync());

        var (_, items) = await BatchAsync(
        [
            Create("1", set, "kept-not", "g"),
            new { id = "2", method = "POST", url = set, atomicityGroup = "g", body = new { name = "no email" } },
            Create("3", set, "never-run", "g"),
            Create("4", set, "outside"),
        ], ct);

        items.Select(i => i.GetProperty("status").GetInt32()).Should().Equal(424, 400, 424, 201);
        items[0].GetProperty("body").GetRawText().Should().Contain("atomicity group 'g'");
        (await _client.GetFromJsonAsync<JsonElement>($"/odata/{set}?$select=name", ct)).GetProperty("value").EnumerateArray()
            .Select(v => v.GetProperty("name").GetString()).Should().Equal(["outside"], "the group's first create was rolled back with it");
    }

    [Fact]
    public async Task A_request_in_an_atomicity_group_sees_what_the_group_wrote_before_it()
    {
        var ct = TestContext.Current.CancellationToken;
        var set = ToPascalCase(await SetupTestTableAsync());

        var (_, items) = await BatchAsync(
        [
            Create("1", set, "a", "g"),
            new { id = "2", method = "GET", url = $"{set}?$count=true", atomicityGroup = "g" },
        ], ct);

        items[1].GetProperty("status").GetInt32().Should().Be(200);
        items[1].GetProperty("body").GetProperty("@odata.count").GetInt64().Should().Be(1, "the read runs in the group's transaction");
    }

    [Fact]
    public async Task A_request_may_depend_on_a_group_and_is_not_run_when_the_group_failed()
    {
        var ct = TestContext.Current.CancellationToken;
        var set = ToPascalCase(await SetupTestTableAsync());

        var (_, items) = await BatchAsync(
        [
            new { id = "1", method = "POST", url = set, atomicityGroup = "g", body = new { name = "no email" } },
            Create("2", set, "after", dependsOn: DependsOn("g")),
        ], ct);

        items.Select(i => i.GetProperty("status").GetInt32()).Should().Equal(400, 424);
        (await CountAsync(set, ct)).Should().Be(0);
    }

    [Fact]
    public async Task Depending_on_a_request_inside_another_group_is_refused()
    {
        var ct = TestContext.Current.CancellationToken;
        var set = ToPascalCase(await SetupTestTableAsync());

        var (_, items) = await BatchAsync([Create("1", set, "a", "g"), Create("2", set, "b", dependsOn: DependsOn("1"))], ct);

        items[1].GetProperty("status").GetInt32().Should().Be(400);
        items[1].GetProperty("body").GetRawText().Should().Contain("depend on the group instead");
    }

    [Theory]
    [InlineData("apart")]
    [InlineData("named like a request")]
    [InlineData("duplicate id")]
    public async Task A_batch_that_breaks_the_format_is_refused_whole(string shape)
    {
        var ct = TestContext.Current.CancellationToken;
        var set = ToPascalCase(await SetupTestTableAsync());
        object[] requests = shape switch
        {
            "apart" => [Create("1", set, "a", "g"), Create("2", set, "b"), Create("3", set, "c", "g")],
            "named like a request" => [Create("1", set, "a", "1")],
            _ => [Create("1", set, "a"), Create("1", set, "b")],
        };

        var (status, _) = await BatchAsync(requests, ct);

        status.Should().Be(400);
        (await CountAsync(set, ct)).Should().Be(0, "nothing of a malformed batch runs");
    }

    #endregion

    private static string[] DependsOn(params string[] ids) => ids;

    private static string ToPascalCase(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;

        var parts = name.Split('_', '-', ' ');
        return string.Concat(parts.Select(p =>
            p.Length > 0
                ? char.ToUpperInvariant(p[0]) + p[1..].ToLowerInvariant()
                : p));
    }
}
