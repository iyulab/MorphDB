using System.Net;
using System.Net.Http.Json;
using MorphDB.Service.Models.Api;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration.Api;

/// <summary>
/// Integration tests for View API endpoints.
/// Tests view CRUD operations, materialized view refresh, and data querying.
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class ViewApiTests
{
    private readonly ApiIntegrationFixture _fixture;
    private readonly HttpClient _client;
    private readonly Guid _projectId;

    public ViewApiTests(ApiIntegrationFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.Api.Client;
        _projectId = fixture.Api.ProjectId;
    }

    #region Helper Methods

    /// <summary>
    /// Creates a base table for view tests.
    /// </summary>
    private async Task<string> CreateBaseTableAsync(string? suffix = null)
    {
        var tableName = $"view_base_{suffix ?? Guid.NewGuid().ToString("N")[..8]}";
        var request = new CreateTableApiRequest
        {
            Name = tableName,
            Columns =
            [
                new CreateColumnApiRequest { Name = "name", Type = "text", Nullable = false },
                new CreateColumnApiRequest { Name = "email", Type = "text", Nullable = false },
                new CreateColumnApiRequest { Name = "age", Type = "integer", Nullable = true },
                new CreateColumnApiRequest { Name = "score", Type = "decimal", Nullable = true }
            ]
        };

        var response = await _client.PostAsJsonAsync("/api/schema/tables", request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return tableName;
    }

    /// <summary>
    /// Inserts test data into the base table.
    /// </summary>
    private async Task InsertTestDataAsync(string tableName)
    {
        var data = new[]
        {
            new { name = "Alice", email = "alice@test.com", age = 30, score = 85.5m },
            new { name = "Bob", email = "bob@test.com", age = 25, score = 92.0m },
            new { name = "Carol", email = "carol@test.com", age = 35, score = 78.5m }
        };

        foreach (var row in data)
        {
            var response = await _client.PostAsJsonAsync($"/api/data/{tableName}", row);
            response.StatusCode.Should().Be(HttpStatusCode.Created);
        }
    }

    #endregion

    #region Create View Tests

    [Fact]
    public async Task CreateView_WithValidRequest_ShouldReturnCreated()
    {
        // Arrange
        var baseTable = await CreateBaseTableAsync();
        var viewName = $"vw_test_{Guid.NewGuid():N}"[..30];

        var request = new CreateViewApiRequest
        {
            Name = viewName,
            BaseTable = baseTable,
            Columns =
            [
                new ViewColumnApiSpec { Source = "name", Alias = "user_name" },
                new ViewColumnApiSpec { Source = "email", Alias = "user_email" }
            ],
            Materialized = false
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/views", request, TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var view = await response.Content.ReadFromJsonAsync<ViewApiResponse>(TestContext.Current.CancellationToken);
        view.Should().NotBeNull();
        view!.Name.Should().Be(viewName);
        view.BaseTable.Should().Be(baseTable);
        view.IsMaterialized.Should().BeFalse();
    }

    [Fact]
    public async Task CreateView_WithFilters_ShouldReturnCreated()
    {
        // Arrange
        var baseTable = await CreateBaseTableAsync();
        var viewName = $"vw_filtered_{Guid.NewGuid():N}"[..30];

        var request = new CreateViewApiRequest
        {
            Name = viewName,
            BaseTable = baseTable,
            Columns =
            [
                new ViewColumnApiSpec { Source = "name", Alias = "name" },
                new ViewColumnApiSpec { Source = "age", Alias = "age" }
            ],
            Filters =
            [
                new ViewFilterApiSpec { Field = "age", Operator = "gte", Value = 18 }
            ]
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/views", request, TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var view = await response.Content.ReadFromJsonAsync<ViewApiResponse>(TestContext.Current.CancellationToken);
        view.Should().NotBeNull();
        view!.Name.Should().Be(viewName);
    }

    [Fact]
    public async Task CreateView_WithStringFilter_ShouldReturnCreated()
    {
        // Regression (issue rest-jsonelement-defects, audit finding): a string filter value
        // arrives as a JsonElement over REST. The view SQL builder's `value is string` check did
        // not match a JsonElement, so the value was emitted unquoted (field = Alice) -> invalid
        // SQL -> view creation failure. It must now be quoted correctly.
        // Arrange
        var baseTable = await CreateBaseTableAsync();
        var viewName = $"vw_strfilter_{Guid.NewGuid():N}"[..30];

        var request = new CreateViewApiRequest
        {
            Name = viewName,
            BaseTable = baseTable,
            Columns =
            [
                new ViewColumnApiSpec { Source = "name", Alias = "name" },
                new ViewColumnApiSpec { Source = "age", Alias = "age" }
            ],
            Filters =
            [
                new ViewFilterApiSpec { Field = "name", Operator = "eq", Value = "Alice" }
            ]
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/views", request, TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var view = await response.Content.ReadFromJsonAsync<ViewApiResponse>(TestContext.Current.CancellationToken);
        view!.Name.Should().Be(viewName);
    }

    [Fact]
    public async Task CreateView_WithAggregation_ShouldReturnCreated()
    {
        // Arrange
        var baseTable = await CreateBaseTableAsync();
        var viewName = $"vw_agg_{Guid.NewGuid():N}"[..30];

        var request = new CreateViewApiRequest
        {
            Name = viewName,
            BaseTable = baseTable,
            Columns =
            [
                new ViewColumnApiSpec { Source = "name", Alias = "name" },
                new ViewColumnApiSpec { Source = "score", Alias = "avg_score", Aggregation = "avg" }
            ],
            GroupBy = ["name"]
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/views", request, TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task CreateMaterializedView_ShouldReturnCreated()
    {
        // Arrange
        var baseTable = await CreateBaseTableAsync();
        var viewName = $"mv_test_{Guid.NewGuid():N}"[..30];

        var request = new CreateViewApiRequest
        {
            Name = viewName,
            BaseTable = baseTable,
            Columns =
            [
                new ViewColumnApiSpec { Source = "name", Alias = "name" },
                new ViewColumnApiSpec { Source = "email", Alias = "email" }
            ],
            Materialized = true,
            RefreshPolicy = "OnDemand"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/views", request, TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var view = await response.Content.ReadFromJsonAsync<ViewApiResponse>(TestContext.Current.CancellationToken);
        view.Should().NotBeNull();
        view!.IsMaterialized.Should().BeTrue();
    }

    [Fact]
    public async Task CreateView_WithDuplicateName_ShouldReturnConflict()
    {
        // Arrange
        var baseTable = await CreateBaseTableAsync();
        var viewName = $"vw_dup_{Guid.NewGuid():N}"[..30];

        var request = new CreateViewApiRequest
        {
            Name = viewName,
            BaseTable = baseTable,
            Columns = [new ViewColumnApiSpec { Source = "name", Alias = "name" }]
        };

        await _client.PostAsJsonAsync("/api/views", request, TestContext.Current.CancellationToken);

        // Act - try to create again with same name
        var response = await _client.PostAsJsonAsync("/api/views", request, TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task CreateView_WithNonExistentBaseTable_ShouldReturnNotFound()
    {
        // Arrange
        var request = new CreateViewApiRequest
        {
            Name = $"vw_nobase_{Guid.NewGuid():N}"[..30],
            BaseTable = "nonexistent_table",
            Columns = [new ViewColumnApiSpec { Source = "name", Alias = "name" }]
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/views", request, TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.BadRequest);
    }

    #endregion

    #region Get View Tests

    [Fact]
    public async Task GetView_WithExistingView_ShouldReturnOk()
    {
        // Arrange
        var baseTable = await CreateBaseTableAsync();
        var viewName = $"vw_get_{Guid.NewGuid():N}"[..30];

        var createRequest = new CreateViewApiRequest
        {
            Name = viewName,
            BaseTable = baseTable,
            Columns = [new ViewColumnApiSpec { Source = "name", Alias = "name" }]
        };
        await _client.PostAsJsonAsync("/api/views", createRequest, TestContext.Current.CancellationToken);

        // Act
        var response = await _client.GetAsync($"/api/views/{viewName}", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var view = await response.Content.ReadFromJsonAsync<ViewApiResponse>(TestContext.Current.CancellationToken);
        view.Should().NotBeNull();
        view!.Name.Should().Be(viewName);
    }

    [Fact]
    public async Task GetView_WithNonExistentView_ShouldReturnNotFound()
    {
        // Act
        var response = await _client.GetAsync("/api/views/nonexistent_view", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ListViews_ShouldReturnAllViews()
    {
        // Arrange
        var baseTable = await CreateBaseTableAsync();
        var viewName = $"vw_list_{Guid.NewGuid():N}"[..30];

        await _client.PostAsJsonAsync("/api/views", new CreateViewApiRequest
        {
            Name = viewName,
            BaseTable = baseTable,
            Columns = [new ViewColumnApiSpec { Source = "name", Alias = "name" }]
        }, TestContext.Current.CancellationToken);

        // Act
        var response = await _client.GetAsync("/api/views", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var views = await response.Content.ReadFromJsonAsync<IReadOnlyList<ViewApiResponse>>(TestContext.Current.CancellationToken);
        views.Should().NotBeNull();
        views!.Should().NotBeEmpty();
        views.Should().Contain(v => v.Name == viewName);
    }

    #endregion

    #region Update View Tests

    [Fact]
    public async Task UpdateView_WithValidRequest_ShouldReturnOk()
    {
        // Arrange
        var baseTable = await CreateBaseTableAsync();
        var viewName = $"vw_upd_{Guid.NewGuid():N}"[..30];
        var newName = $"vw_updated_{Guid.NewGuid():N}"[..30];

        await _client.PostAsJsonAsync("/api/views", new CreateViewApiRequest
        {
            Name = viewName,
            BaseTable = baseTable,
            Columns = [new ViewColumnApiSpec { Source = "name", Alias = "name" }]
        }, TestContext.Current.CancellationToken);

        var updateRequest = new UpdateViewApiRequest
        {
            Name = newName,
            Description = "Updated view description"
        };

        // Act
        var response = await _client.PatchAsJsonAsync($"/api/views/{viewName}", updateRequest, TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var updatedView = await response.Content.ReadFromJsonAsync<ViewApiResponse>(TestContext.Current.CancellationToken);
        updatedView.Should().NotBeNull();
        updatedView!.Name.Should().Be(newName);
    }

    #endregion

    #region Delete View Tests

    [Fact]
    public async Task DeleteView_WithExistingView_ShouldReturnNoContent()
    {
        // Arrange
        var baseTable = await CreateBaseTableAsync();
        var viewName = $"vw_del_{Guid.NewGuid():N}"[..30];

        await _client.PostAsJsonAsync("/api/views", new CreateViewApiRequest
        {
            Name = viewName,
            BaseTable = baseTable,
            Columns = [new ViewColumnApiSpec { Source = "name", Alias = "name" }]
        }, TestContext.Current.CancellationToken);

        // Act
        var response = await _client.DeleteAsync($"/api/views/{viewName}", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify deletion
        var getResponse = await _client.GetAsync($"/api/views/{viewName}", TestContext.Current.CancellationToken);
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteView_WithNonExistentView_ShouldReturnNotFound()
    {
        // Act
        var response = await _client.DeleteAsync("/api/views/nonexistent_view", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region Materialized View Refresh Tests

    [Fact]
    public async Task RefreshMaterializedView_ShouldReturnNoContent()
    {
        // Arrange
        var baseTable = await CreateBaseTableAsync();
        var viewName = $"mv_refresh_{Guid.NewGuid():N}"[..30];

        await _client.PostAsJsonAsync("/api/views", new CreateViewApiRequest
        {
            Name = viewName,
            BaseTable = baseTable,
            Columns = [new ViewColumnApiSpec { Source = "name", Alias = "name" }],
            Materialized = true
        }, TestContext.Current.CancellationToken);

        // Act
        var response = await _client.PostAsync($"/api/views/{viewName}/refresh", null, TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task RefreshMaterializedView_OnRegularView_ShouldReturnBadRequest()
    {
        // Arrange
        var baseTable = await CreateBaseTableAsync();
        var viewName = $"vw_norefresh_{Guid.NewGuid():N}"[..30];

        await _client.PostAsJsonAsync("/api/views", new CreateViewApiRequest
        {
            Name = viewName,
            BaseTable = baseTable,
            Columns = [new ViewColumnApiSpec { Source = "name", Alias = "name" }],
            Materialized = false
        }, TestContext.Current.CancellationToken);

        // Act
        var response = await _client.PostAsync($"/api/views/{viewName}/refresh", null, TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CheckMaterializedViewStale_ShouldReturnOk()
    {
        // Arrange
        var baseTable = await CreateBaseTableAsync();
        var viewName = $"mv_stale_{Guid.NewGuid():N}"[..30];

        await _client.PostAsJsonAsync("/api/views", new CreateViewApiRequest
        {
            Name = viewName,
            BaseTable = baseTable,
            Columns = [new ViewColumnApiSpec { Source = "name", Alias = "name" }],
            Materialized = true
        }, TestContext.Current.CancellationToken);

        // Act
        var response = await _client.GetAsync($"/api/views/{viewName}/stale", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    #endregion

    #region Query View Data Tests

    [Fact]
    public async Task QueryViewData_ShouldReturnResults()
    {
        // Arrange
        var baseTable = await CreateBaseTableAsync();
        await InsertTestDataAsync(baseTable);

        var viewName = $"vw_query_{Guid.NewGuid():N}"[..30];

        await _client.PostAsJsonAsync("/api/views", new CreateViewApiRequest
        {
            Name = viewName,
            BaseTable = baseTable,
            Columns =
            [
                new ViewColumnApiSpec { Source = "name", Alias = "name" },
                new ViewColumnApiSpec { Source = "email", Alias = "email" }
            ]
        }, TestContext.Current.CancellationToken);

        // Act
        var response = await _client.GetAsync($"/api/views/{viewName}/data", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<ViewQueryApiResponse>(TestContext.Current.CancellationToken);
        result.Should().NotBeNull();
        result!.TotalCount.Should().BeGreaterThanOrEqualTo(3);
        result.Data.Should().NotBeEmpty();
    }

    [Fact]
    public async Task QueryViewData_WithPagination_ShouldReturnPagedResults()
    {
        // Arrange
        var baseTable = await CreateBaseTableAsync();
        await InsertTestDataAsync(baseTable);

        var viewName = $"vw_paged_{Guid.NewGuid():N}"[..30];

        await _client.PostAsJsonAsync("/api/views", new CreateViewApiRequest
        {
            Name = viewName,
            BaseTable = baseTable,
            Columns = [new ViewColumnApiSpec { Source = "name", Alias = "name" }]
        }, TestContext.Current.CancellationToken);

        // Act
        var response = await _client.GetAsync($"/api/views/{viewName}/data?skip=0&take=2", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<ViewQueryApiResponse>(TestContext.Current.CancellationToken);
        result.Should().NotBeNull();
        result!.Data.Count.Should().BeLessThanOrEqualTo(2);
    }

    /// <summary>
    /// A view read takes the table read's filter language: one condition per repeated <c>filter</c>
    /// parameter, a malformed one refused. The view endpoint used to split one parameter on <c>" AND "</c>
    /// and drop a condition it could not read — an answer wider than the question.
    /// </summary>
    [Fact]
    public async Task QueryViewData_Filters_ReadAsOnATableRead()
    {
        var ct = TestContext.Current.CancellationToken;
        var baseTable = await CreateBaseTableAsync();
        await InsertTestDataAsync(baseTable);
        var viewName = $"vw_filt_{Guid.NewGuid():N}"[..30];
        (await _client.PostAsJsonAsync("/api/views", new CreateViewApiRequest
        {
            Name = viewName,
            BaseTable = baseTable,
            Columns =
            [
                new ViewColumnApiSpec { Source = "name", Alias = "name" },
                new ViewColumnApiSpec { Source = "email", Alias = "email" },
                new ViewColumnApiSpec { Source = "age", Alias = "age" },
                new ViewColumnApiSpec { Source = "score", Alias = "score" },
            ],
        }, ct)).StatusCode.Should().Be(HttpStatusCode.Created);

        async Task<string[]> Names(string query)
        {
            var response = await _client.GetAsync($"/api/views/{viewName}/data?{query}&orderBy=name:asc", ct);
            response.StatusCode.Should().Be(HttpStatusCode.OK, query);
            var result = await response.Content.ReadFromJsonAsync<ViewQueryApiResponse>(ct);
            var names = result!.Data.Select(r => r["name"]?.ToString()!).ToArray();
            result.TotalCount.Should().Be(names.Length, "the count is taken under the same conditions");
            return names;
        }

        static string F(string condition) => "filter=" + Uri.EscapeDataString(condition);

        (await Names($"{F("age:gte:30")}&{F("score:lt:80")}")).Should().Equal("Carol");
        (await Names(F("score:gt:80.0"))).Should().Equal("Alice", "Bob");
        (await Names(F("name:in:[\"Alice\",\"Bob\"]"))).Should().Equal("Alice", "Bob");
        (await Names(F("name:notin:[\"Alice\"]"))).Should().Equal("Bob", "Carol");
        (await Names(F("name:in:[]"))).Should().BeEmpty();
        (await Names(F("email:contains:ALICE"))).Should().Equal("Alice");
        (await Names(F("email:contains:%"))).Should().BeEmpty("contains matches its value literally");
        (await Names(F("name:eq:Alice AND age:gt:0"))).Should().BeEmpty("a value may hold the word AND");

        foreach (var refused in new[] { F("age:gte"), F("nope:eq:1"), F("name:in:Alice,Bob"), "filter=" + Uri.EscapeDataString("age:between:1") })
        {
            var response = await _client.GetAsync($"/api/views/{viewName}/data?{refused}", ct);
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest, refused);
            (await response.Content.ReadFromJsonAsync<ErrorResponse>(ct))!.Code.Should().Be("INVALID_FILTER");
        }
    }

    #endregion

    #region Project Isolation Tests

    [Fact]
    public async Task CreateView_DifferentProjects_ShouldBeIsolated()
    {
        // Arrange
        var viewName = "shared_view_name";
        var project1Client = await _fixture.Api.CreateClientWithNewProjectAsync(TestContext.Current.CancellationToken);
        var project2Client = await _fixture.Api.CreateClientWithNewProjectAsync(TestContext.Current.CancellationToken);

        // Create base tables for each project
        var table1Request = new CreateTableApiRequest
        {
            Name = "base_table",
            Columns = [new CreateColumnApiRequest { Name = "data", Type = "text" }]
        };
        await project1Client.PostAsJsonAsync("/api/schema/tables", table1Request, TestContext.Current.CancellationToken);
        await project2Client.PostAsJsonAsync("/api/schema/tables", table1Request, TestContext.Current.CancellationToken);

        var viewRequest = new CreateViewApiRequest
        {
            Name = viewName,
            BaseTable = "base_table",
            Columns = [new ViewColumnApiSpec { Source = "data", Alias = "data" }]
        };

        // Act
        var response1 = await project1Client.PostAsJsonAsync("/api/views", viewRequest, TestContext.Current.CancellationToken);
        var response2 = await project2Client.PostAsJsonAsync("/api/views", viewRequest, TestContext.Current.CancellationToken);

        // Assert - Both should succeed as they're in different projects
        response1.StatusCode.Should().Be(HttpStatusCode.Created);
        response2.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task GetView_DifferentProject_ShouldReturnNotFound()
    {
        // Arrange
        var baseTable = await CreateBaseTableAsync();
        var viewName = $"vw_isolated_{Guid.NewGuid():N}"[..30];

        await _client.PostAsJsonAsync("/api/views", new CreateViewApiRequest
        {
            Name = viewName,
            BaseTable = baseTable,
            Columns = [new ViewColumnApiSpec { Source = "name", Alias = "name" }]
        }, TestContext.Current.CancellationToken);

        // Create a client for a different project
        var otherProjectClient = await _fixture.Api.CreateClientWithNewProjectAsync(TestContext.Current.CancellationToken);

        // Act
        var response = await otherProjectClient.GetAsync($"/api/views/{viewName}", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion
}
