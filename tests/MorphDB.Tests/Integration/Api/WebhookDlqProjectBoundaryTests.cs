using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Models;
using MorphDB.Service.Models.Api;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration.Api;

/// <summary>
/// A dead letter carries the row its delivery was about, so it is there for its webhook's project and
/// nobody else: another project's caller does not see it listed, cannot read, resolve or replay it by
/// id — answered as an id never issued — and does not archive it.
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class WebhookDlqProjectBoundaryTests(ApiIntegrationFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private IWebhookManager Webhooks => fixture.Api.Services.GetRequiredService<IWebhookManager>();

    /// <summary>A webhook in a project that is not the caller's, with one delivery dead-lettered.</summary>
    private async Task<(HttpClient Owner, Guid WebhookId, Guid DlqId)> ArrangeElsewhereAsync()
    {
        var owner = await fixture.Api.CreateClientWithNewProjectAsync(Ct);
        var table = $"dlq_{Guid.NewGuid():N}"[..24];
        (await owner.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = table,
            Columns = [new CreateColumnApiRequest { Name = "secret_note", Type = "text", Nullable = true }],
        }, Ct)).EnsureSuccessStatusCode();
        var created = await owner.PostAsJsonAsync("/api/webhooks", new CreateWebhookApiRequest
        {
            Name = "elsewhere",
            Table = table,
            Url = "https://webhook.invalid/hook",
        }, Ct);
        created.EnsureSuccessStatusCode();
        var webhook = (await created.Content.ReadFromJsonAsync<WebhookApiResponse>(Ct))!;

        var delivery = await Webhooks.CreateDeliveryAsync(new CreateDeliveryRequest
        {
            WebhookId = webhook.Id,
            Event = WebhookEvent.Insert,
            Payload = JsonDocument.Parse("""{"secret_note":"not yours"}"""),
        }, Ct);
        var dead = await Webhooks.MoveToDlqAsync(new MoveToDlqRequest
        {
            DeliveryId = delivery!.DeliveryId,
            Reason = DlqReason.MaxRetriesExceeded,
        }, Ct);
        return (owner, webhook.Id, dead.DlqId);
    }

    [Fact]
    public async Task Another_projects_dead_letter_is_not_listed_read_resolved_replayed_or_archived()
    {
        var (owner, webhookId, dlqId) = await ArrangeElsewhereAsync();
        var caller = fixture.Api.Client;

        var listed = await caller.GetFromJsonAsync<List<DlqMessageApiResponse>>("/api/webhooks/dlq?limit=1000", Ct);
        Assert.DoesNotContain(listed!, m => m.DlqId == dlqId);
        var byWebhook = await caller.GetFromJsonAsync<List<DlqMessageApiResponse>>($"/api/webhooks/dlq?webhookId={webhookId}", Ct);
        Assert.Empty(byWebhook!);

        Assert.Equal(HttpStatusCode.NotFound, (await caller.GetAsync($"/api/webhooks/dlq/{dlqId}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await caller.PostAsJsonAsync($"/api/webhooks/dlq/{dlqId}/resolve",
            new ResolveDlqApiRequest { ResolutionNotes = "not mine" }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await caller.PostAsync($"/api/webhooks/dlq/{dlqId}/replay", null, Ct)).StatusCode);
        (await caller.PostAsync("/api/webhooks/dlq/archive", null, Ct)).EnsureSuccessStatusCode();

        var untouched = await Webhooks.GetDlqMessageAsync(dlqId, Ct);
        Assert.Equal(DlqStatus.PendingReview, untouched!.Status);

        // Its own project sees it.
        var own = await owner.GetFromJsonAsync<List<DlqMessageApiResponse>>($"/api/webhooks/dlq?webhookId={webhookId}", Ct);
        Assert.Contains(own!, m => m.DlqId == dlqId);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/api/webhooks/dlq/{dlqId}", Ct)).StatusCode);
        var pending = await owner.GetFromJsonAsync<List<DlqMessageApiResponse>>(
            $"/api/webhooks/dlq?webhookId={webhookId}&status=pending_review", Ct);
        Assert.Equal([dlqId], pending!.Select(m => m.DlqId));
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.GetAsync("/api/webhooks/dlq?status=nonsense", Ct)).StatusCode);
    }
}
