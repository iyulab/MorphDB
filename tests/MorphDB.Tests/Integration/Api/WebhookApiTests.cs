using System.Net;
using System.Net.Http.Json;
using MorphDB.Service.Models.Api;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration.Api;

/// <summary>
/// Integration tests for Webhook API endpoints.
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class WebhookApiTests
{
    private readonly ApiIntegrationFixture _fixture;
    private readonly HttpClient _client;
    private readonly Guid _projectId;

    public WebhookApiTests(ApiIntegrationFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.Api.Client;
        _projectId = fixture.Api.ProjectId;
    }

    #region Helper Methods

    private async Task<string> CreateTestTableAsync()
    {
        var tableName = $"webhook_test_{Guid.NewGuid():N}"[..30];
        var request = new CreateTableApiRequest
        {
            Name = tableName,
            Columns =
            [
                new CreateColumnApiRequest { Name = "name", Type = "text", Nullable = false },
                new CreateColumnApiRequest { Name = "email", Type = "text", Nullable = true }
            ]
        };

        var response = await _client.PostAsJsonAsync("/api/schema/tables", request);
        response.EnsureSuccessStatusCode();
        return tableName;
    }

    #endregion

    #region Create Webhook

    [Fact]
    public async Task CreateWebhook_WithValidRequest_ShouldReturnCreated()
    {
        // Arrange
        var tableName = await CreateTestTableAsync();
        var request = new CreateWebhookApiRequest
        {
            Name = $"test_webhook_{Guid.NewGuid():N}"[..30],
            Table = tableName,
            Url = "https://example.com/webhook",
            Events = ["insert", "update", "delete"]
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/webhooks", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var webhook = await response.Content.ReadFromJsonAsync<WebhookApiResponse>();
        webhook.Should().NotBeNull();
        webhook!.Name.Should().Be(request.Name);
        webhook.Table.Should().Be(tableName);
        webhook.Url.Should().Be(request.Url);
        webhook.Events.Should().HaveCount(3);
        webhook.Secret.Should().NotBeNullOrEmpty(); // Secret is shown on creation
        webhook.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task CreateWebhook_WithDefaultEvents_ShouldIncludeAllEvents()
    {
        // Arrange
        var tableName = await CreateTestTableAsync();
        var request = new CreateWebhookApiRequest
        {
            Name = $"test_webhook_{Guid.NewGuid():N}"[..30],
            Table = tableName,
            Url = "https://example.com/webhook"
            // No events specified - should default to all
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/webhooks", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var webhook = await response.Content.ReadFromJsonAsync<WebhookApiResponse>();
        webhook!.Events.Should().Contain("insert");
        webhook.Events.Should().Contain("update");
        webhook.Events.Should().Contain("delete");
    }

    [Fact]
    public async Task CreateWebhook_WithNonExistentTable_ShouldReturnNotFound()
    {
        // Arrange
        var request = new CreateWebhookApiRequest
        {
            Name = "test_webhook",
            Table = "nonexistent_table",
            Url = "https://example.com/webhook"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/webhooks", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CreateWebhook_WithoutProjectHeader_ShouldReturnBadRequest()
    {
        // Arrange
        var client = _fixture.Api.CreateClientWithProject(Guid.Empty);
        var request = new CreateWebhookApiRequest
        {
            Name = "test_webhook",
            Table = "some_table",
            Url = "https://example.com/webhook"
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/webhooks", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    #endregion

    #region Get Webhook

    [Fact]
    public async Task GetWebhook_WithExistingWebhook_ShouldReturnWebhook()
    {
        // Arrange
        var tableName = await CreateTestTableAsync();
        var createRequest = new CreateWebhookApiRequest
        {
            Name = $"test_webhook_{Guid.NewGuid():N}"[..30],
            Table = tableName,
            Url = "https://example.com/webhook"
        };
        var createResponse = await _client.PostAsJsonAsync("/api/webhooks", createRequest);
        var createdWebhook = await createResponse.Content.ReadFromJsonAsync<WebhookApiResponse>();

        // Act
        var response = await _client.GetAsync($"/api/webhooks/{createdWebhook!.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var webhook = await response.Content.ReadFromJsonAsync<WebhookApiResponse>();
        webhook.Should().NotBeNull();
        webhook!.Name.Should().Be(createRequest.Name);
        webhook.Secret.Should().BeNull(); // Secret should be hidden on GET
    }

    [Fact]
    public async Task GetWebhook_WithNonExistentWebhook_ShouldReturnNotFound()
    {
        // Act
        var response = await _client.GetAsync($"/api/webhooks/{Guid.NewGuid()}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region List Webhooks

    [Fact]
    public async Task ListWebhooks_ShouldReturnWebhooksForProject()
    {
        // Arrange
        var tableName = await CreateTestTableAsync();
        var webhookName = $"list_test_{Guid.NewGuid():N}"[..30];
        await _client.PostAsJsonAsync("/api/webhooks", new CreateWebhookApiRequest
        {
            Name = webhookName,
            Table = tableName,
            Url = "https://example.com/webhook"
        });

        // Act
        var response = await _client.GetAsync("/api/webhooks");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var webhooks = await response.Content.ReadFromJsonAsync<IReadOnlyList<WebhookApiResponse>>();
        webhooks.Should().NotBeNull();
        webhooks!.Should().Contain(w => w.Name == webhookName);
    }

    [Fact]
    public async Task ListWebhooks_WithTableFilter_ShouldReturnFilteredResults()
    {
        // Arrange
        var tableName1 = await CreateTestTableAsync();
        var tableName2 = await CreateTestTableAsync();

        await _client.PostAsJsonAsync("/api/webhooks", new CreateWebhookApiRequest
        {
            Name = $"webhook1_{Guid.NewGuid():N}"[..30],
            Table = tableName1,
            Url = "https://example.com/webhook1"
        });

        await _client.PostAsJsonAsync("/api/webhooks", new CreateWebhookApiRequest
        {
            Name = $"webhook2_{Guid.NewGuid():N}"[..30],
            Table = tableName2,
            Url = "https://example.com/webhook2"
        });

        // Act
        var response = await _client.GetAsync($"/api/webhooks?table={tableName1}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var webhooks = await response.Content.ReadFromJsonAsync<IReadOnlyList<WebhookApiResponse>>();
        webhooks.Should().NotBeNull();
        webhooks!.Should().OnlyContain(w => w.Table == tableName1);
    }

    #endregion

    #region Update Webhook

    [Fact]
    public async Task UpdateWebhook_WithValidRequest_ShouldReturnUpdatedWebhook()
    {
        // Arrange
        var tableName = await CreateTestTableAsync();
        var createResponse = await _client.PostAsJsonAsync("/api/webhooks", new CreateWebhookApiRequest
        {
            Name = $"update_test_{Guid.NewGuid():N}"[..30],
            Table = tableName,
            Url = "https://example.com/original"
        });
        var created = await createResponse.Content.ReadFromJsonAsync<WebhookApiResponse>();

        var updateRequest = new UpdateWebhookApiRequest
        {
            Url = "https://example.com/updated",
            Events = ["insert"],
            IsActive = false
        };

        // Act
        var response = await _client.PatchAsJsonAsync($"/api/webhooks/{created!.Id}", updateRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = await response.Content.ReadFromJsonAsync<WebhookApiResponse>();
        updated!.Url.Should().Be("https://example.com/updated");
        updated.Events.Should().HaveCount(1);
        updated.Events.Should().Contain("insert");
        updated.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateWebhook_WithNonExistentWebhook_ShouldReturnNotFound()
    {
        // Arrange
        var updateRequest = new UpdateWebhookApiRequest
        {
            Url = "https://example.com/updated"
        };

        // Act
        var response = await _client.PatchAsJsonAsync($"/api/webhooks/{Guid.NewGuid()}", updateRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region Delete Webhook

    [Fact]
    public async Task DeleteWebhook_WithExistingWebhook_ShouldReturnNoContent()
    {
        // Arrange
        var tableName = await CreateTestTableAsync();
        var createResponse = await _client.PostAsJsonAsync("/api/webhooks", new CreateWebhookApiRequest
        {
            Name = $"delete_test_{Guid.NewGuid():N}"[..30],
            Table = tableName,
            Url = "https://example.com/webhook"
        });
        var created = await createResponse.Content.ReadFromJsonAsync<WebhookApiResponse>();

        // Act
        var response = await _client.DeleteAsync($"/api/webhooks/{created!.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify deletion
        var getResponse = await _client.GetAsync($"/api/webhooks/{created.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteWebhook_WithNonExistentWebhook_ShouldReturnNotFound()
    {
        // Act
        var response = await _client.DeleteAsync($"/api/webhooks/{Guid.NewGuid()}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region Regenerate Secret

    [Fact]
    public async Task RegenerateSecret_ShouldReturnNewSecret()
    {
        // Arrange
        var tableName = await CreateTestTableAsync();
        var createResponse = await _client.PostAsJsonAsync("/api/webhooks", new CreateWebhookApiRequest
        {
            Name = $"secret_test_{Guid.NewGuid():N}"[..30],
            Table = tableName,
            Url = "https://example.com/webhook"
        });
        var created = await createResponse.Content.ReadFromJsonAsync<WebhookApiResponse>();
        var originalSecret = created!.Secret;

        // Act
        var response = await _client.PostAsync($"/api/webhooks/{created.Id}/regenerate-secret", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<RegenerateSecretResponse>();
        result.Should().NotBeNull();
        result!.Secret.Should().NotBeNullOrEmpty();
        result.Secret.Should().NotBe(originalSecret);
        result.Secret.Should().HaveLength(64); // 32 bytes as hex = 64 characters
    }

    [Fact]
    public async Task RegenerateSecret_WithNonExistentWebhook_ShouldReturnNotFound()
    {
        // Act
        var response = await _client.PostAsync($"/api/webhooks/{Guid.NewGuid()}/regenerate-secret", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region Delivery History

    [Fact]
    public async Task GetDeliveryHistory_WithExistingWebhook_ShouldReturnEmptyList()
    {
        // Arrange
        var tableName = await CreateTestTableAsync();
        var createResponse = await _client.PostAsJsonAsync("/api/webhooks", new CreateWebhookApiRequest
        {
            Name = $"history_test_{Guid.NewGuid():N}"[..30],
            Table = tableName,
            Url = "https://example.com/webhook"
        });
        var created = await createResponse.Content.ReadFromJsonAsync<WebhookApiResponse>();

        // Act
        var response = await _client.GetAsync($"/api/webhooks/{created!.Id}/deliveries");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var deliveries = await response.Content.ReadFromJsonAsync<IReadOnlyList<DeliveryApiResponse>>();
        deliveries.Should().NotBeNull();
        deliveries.Should().BeEmpty(); // No deliveries yet
    }

    [Fact]
    public async Task GetDeliveryHistory_WithNonExistentWebhook_ShouldReturnNotFound()
    {
        // Act
        var response = await _client.GetAsync($"/api/webhooks/{Guid.NewGuid()}/deliveries");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region Delivery Triggering

    /// <summary>
    /// The registration, listing and history endpoints above all answer correctly on their own,
    /// which is exactly what let a webhook that never fires look healthy. This drives an actual
    /// data change through the API and checks the one place that could not lie about it: whether
    /// a delivery record exists afterward.
    /// </summary>
    [Fact]
    public async Task DataChange_WithSubscribedWebhook_ShouldQueueADelivery()
    {
        // Arrange
        var tableName = await CreateTestTableAsync();
        var createResponse = await _client.PostAsJsonAsync("/api/webhooks", new CreateWebhookApiRequest
        {
            Name = $"trigger_test_{Guid.NewGuid():N}"[..30],
            Table = tableName,
            Url = "https://example.invalid/webhook",
            Events = ["insert"]
        });
        var created = await createResponse.Content.ReadFromJsonAsync<WebhookApiResponse>();

        // Act
        await _client.PostAsJsonAsync($"/api/data/{tableName}", new Dictionary<string, object?>
        {
            ["name"] = "Test",
            ["email"] = "test@example.com"
        });

        // The change listener reacts to a PostgreSQL NOTIFY asynchronously, same as the SignalR
        // broadcast tests above poll for their message to arrive.
        IReadOnlyList<DeliveryApiResponse>? deliveries = null;
        var timeout = Task.Delay(TimeSpan.FromSeconds(15));
        while ((deliveries is null || deliveries.Count == 0) && !timeout.IsCompleted)
        {
            await Task.Delay(100);
            var response = await _client.GetAsync($"/api/webhooks/{created!.Id}/deliveries");
            deliveries = await response.Content.ReadFromJsonAsync<IReadOnlyList<DeliveryApiResponse>>();
        }

        // Assert
        deliveries.Should().NotBeNullOrEmpty(
            "a matching data change must queue a delivery, not just broadcast one over SignalR");
        deliveries![0].Event.Should().Be("insert");
    }

    /// <summary>
    /// Regression: <c>WebhookFilterMatcher</c> compares filter keys against the row data the
    /// change listener hands it. Before the listener translated that row to logical column names
    /// (see ISSUE-morphdb-20260807-realtime-events-carry-physical-column-names.md), a filter
    /// written in the same logical vocabulary the registration API itself uses could never match
    /// anything — the row's keys were physical (<c>col_xxx</c>) and never equal a logical filter
    /// key, so a filtered webhook silently never fired regardless of the row's actual values.
    /// </summary>
    [Fact]
    public async Task DataChange_MatchingALogicalColumnFilter_ShouldQueueADelivery()
    {
        var tableName = await CreateTestTableAsync();
        var createResponse = await _client.PostAsJsonAsync("/api/webhooks", new CreateWebhookApiRequest
        {
            Name = $"filter_test_{Guid.NewGuid():N}"[..30],
            Table = tableName,
            Url = "https://example.invalid/webhook",
            Events = ["insert"],
            Filter = System.Text.Json.JsonDocument.Parse("""{"name":"vip"}""")
        });
        var created = await createResponse.Content.ReadFromJsonAsync<WebhookApiResponse>();

        await _client.PostAsJsonAsync($"/api/data/{tableName}", new Dictionary<string, object?>
        {
            ["name"] = "vip",
            ["email"] = "vip@example.com"
        });

        IReadOnlyList<DeliveryApiResponse>? deliveries = null;
        var timeout = Task.Delay(TimeSpan.FromSeconds(15));
        while ((deliveries is null || deliveries.Count == 0) && !timeout.IsCompleted)
        {
            await Task.Delay(100);
            var response = await _client.GetAsync($"/api/webhooks/{created!.Id}/deliveries");
            deliveries = await response.Content.ReadFromJsonAsync<IReadOnlyList<DeliveryApiResponse>>();
        }

        deliveries.Should().NotBeNullOrEmpty(
            "a filter written in the same logical column names the API itself uses must be able to match a real row");
    }

    [Fact]
    public async Task DataChange_ForAnUnsubscribedTable_ShouldNotQueueADelivery()
    {
        // Arrange — a webhook on a *different* table must not react to this one.
        var watchedTable = await CreateTestTableAsync();
        var otherTable = await CreateTestTableAsync();
        var createResponse = await _client.PostAsJsonAsync("/api/webhooks", new CreateWebhookApiRequest
        {
            Name = $"unrelated_test_{Guid.NewGuid():N}"[..30],
            Table = watchedTable,
            Url = "https://example.invalid/webhook",
            Events = ["insert"]
        });
        var created = await createResponse.Content.ReadFromJsonAsync<WebhookApiResponse>();

        // Act
        var response = await _client.PostAsJsonAsync($"/api/data/{otherTable}", new Dictionary<string, object?>
        {
            ["name"] = "Test",
            ["email"] = "test@example.com"
        });
        response.EnsureSuccessStatusCode();
        await Task.Delay(TimeSpan.FromSeconds(1)); // give the listener a chance to (wrongly) react

        // Assert
        var deliveriesResponse = await _client.GetAsync($"/api/webhooks/{created!.Id}/deliveries");
        var deliveries = await deliveriesResponse.Content.ReadFromJsonAsync<IReadOnlyList<DeliveryApiResponse>>();
        deliveries.Should().BeEmpty();
    }

    #endregion

    #region Project Isolation

    [Fact]
    public async Task Webhook_DifferentProjects_ShouldBeIsolated()
    {
        // Arrange
        var project1Client = _fixture.Api.CreateClientWithProject(Guid.NewGuid());
        var project2Client = _fixture.Api.CreateClientWithProject(Guid.NewGuid());

        // Create table for project 1
        var tableName1 = $"project1_table_{Guid.NewGuid():N}"[..30];
        await project1Client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = tableName1,
            Columns = [new CreateColumnApiRequest { Name = "data", Type = "text" }]
        });

        // Create webhook for project 1
        var webhookResponse = await project1Client.PostAsJsonAsync("/api/webhooks", new CreateWebhookApiRequest
        {
            Name = "project1_webhook",
            Table = tableName1,
            Url = "https://example.com/project1"
        });
        var webhook = await webhookResponse.Content.ReadFromJsonAsync<WebhookApiResponse>();

        // Act - Project 2 tries to access project 1's webhook
        var response = await project2Client.GetAsync($"/api/webhooks/{webhook!.Id}");

        // Assert - Should not find it (different project)
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Project 2's list should be empty
        var listResponse = await project2Client.GetAsync("/api/webhooks");
        var webhooks = await listResponse.Content.ReadFromJsonAsync<IReadOnlyList<WebhookApiResponse>>();
        webhooks.Should().BeEmpty();
    }

    #endregion
}
