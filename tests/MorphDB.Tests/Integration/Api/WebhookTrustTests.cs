using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Models;
using MorphDB.Service.Controllers;
using MorphDB.Service.Models.Api;
using MorphDB.Service.Services;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration.Api;

/// <summary>
/// A webhook makes the service send a signed request, carrying row data, to an address its registrant chose, from
/// the service's own place on the network. Who may register one and where it may point are the service's boundary:
/// the master secret registers, the address has to be an http(s) URL, a deployment can name the hosts it delivers
/// to, and a delivery never follows a redirect to an address nobody checked.
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class WebhookTrustTests
{
    private const string MasterSecret = "mdb_test_master_secret_value";

    private readonly ApiIntegrationFixture _fixture;

    public WebhookTrustTests(ApiIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient EnforcedClient(string secret)
    {
        var client = _fixture.Api.WithMasterSecret(MasterSecret).CreateClient();
        client.DefaultRequestHeaders.Add("X-Project-Id", _fixture.Api.ProjectId.ToString());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secret);
        return client;
    }

    private static CreateWebhookApiRequest Registration(string url) => new()
    {
        Name = $"trust_{Guid.NewGuid():N}"[..30],
        Table = "no_such_table_needed_to_be_refused",
        Url = url,
    };

    // The registrant of a webhook decides where the service sends rows, so registering is administration, as issuing a
    // secret is. Reading is held too: a webhook's headers can carry the receiver's credential and its delivery
    // history the receiver's responses.
    [Fact]
    public async Task Only_the_master_secret_manages_webhooks_while_secrets_are_enforced()
    {
        var master = EnforcedClient(MasterSecret);
        var issued = await master.PostAsJsonAsync("/api/security/secrets", new IssueSecretApiRequest { Name = "hook-trust", Role = "writer" }, Ct);
        issued.StatusCode.Should().Be(HttpStatusCode.Created);
        var secret = (await issued.Content.ReadFromJsonAsync<IssuedSecretResponse>(Ct))!.Secret;
        var delegated = EnforcedClient(secret);

        (await delegated.PostAsJsonAsync("/api/webhooks", Registration("https://example.invalid/hook"), Ct))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden, "an issued secret may not point the service at an address");
        (await delegated.GetAsync("/api/webhooks", Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await delegated.GetAsync("/api/webhooks/dlq", Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await delegated.DeleteAsync($"/api/webhooks/{Guid.NewGuid()}", Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await master.GetAsync("/api/webhooks", Ct)).StatusCode.Should().Be(HttpStatusCode.OK, "the master secret manages webhooks");
    }

    [Fact]
    public async Task Without_an_injected_master_secret_webhooks_are_managed_as_they_always_were()
    {
        var response = await _fixture.Api.Client.GetAsync("/api/webhooks", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("/relative/path")]
    [InlineData("ftp://example.com/hook")]
    [InlineData("file:///etc/passwd")]
    [InlineData("https://user:password@example.com/hook")]
    public async Task A_destination_that_is_not_an_http_url_without_credentials_is_refused(string url)
    {
        var response = await _fixture.Api.Client.PostAsJsonAsync("/api/webhooks", Registration(url), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<ErrorResponse>(Ct))!.Code.Should().Be("INVALID_WEBHOOK_URL");
    }

    [Fact]
    public async Task A_changed_destination_is_checked_the_same_way()
    {
        var response = await _fixture.Api.Client.PatchAsJsonAsync(
            $"/api/webhooks/{Guid.NewGuid()}", new UpdateWebhookApiRequest { Url = "ftp://example.com/hook" }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, "the address is refused before the webhook is looked up");
        (await response.Content.ReadFromJsonAsync<ErrorResponse>(Ct))!.Code.Should().Be("INVALID_WEBHOOK_URL");
    }

    [Theory]
    [InlineData("https://hooks.example.com/a", true)]
    [InlineData("https://HOOKS.EXAMPLE.COM/a", true)]
    [InlineData("https://a.internal.test/x", true)]
    [InlineData("https://deep.a.internal.test/x", true)]
    [InlineData("https://internal.test/x", false)]
    [InlineData("https://evilinternal.test/x", false)]
    [InlineData("https://hooks.example.com.evil.test/a", false)]
    [InlineData("https://169.254.169.254/latest", false)]
    public void Named_hosts_are_matched_by_name_or_by_a_domain_wildcard(string url, bool allowed)
    {
        var options = new WebhookDestinationOptions { AllowedHosts = ["hooks.example.com", "*.internal.test"] };

        WebhookDestination.TryValidate(url, options, out _).Should().Be(allowed);
    }

    [Fact]
    public void With_no_hosts_named_any_http_host_is_allowed()
    {
        WebhookDestination.TryValidate("http://10.0.0.5:8080/hook", new WebhookDestinationOptions(), out _).Should().BeTrue();
    }

    // The allow-list can be tightened after a webhook was registered; its next delivery is then refused instead of sent.
    [Fact]
    public async Task A_delivery_is_checked_against_the_hosts_named_now_and_is_not_sent_to_another()
    {
        var handler = new CountingHandler();
        var service = DeliveryService(handler, new WebhookDestinationOptions { AllowedHosts = ["hooks.example.com"] });

        var result = await service.DeliverAsync(Webhook("https://elsewhere.test/hook"), Delivery(), Ct);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Destination refused");
        handler.Requests.Should().Be(0, "a refused destination is not contacted");
    }

    // The destination was checked as an address. A redirect would carry the signed request to one that was not.
    [Fact]
    public async Task A_delivery_does_not_follow_a_redirect()
    {
        using var redirected = new TcpListener(IPAddress.Loopback, 0);
        redirected.Start();
        var redirectedPort = ((IPEndPoint)redirected.LocalEndpoint).Port;

        using var first = new TcpListener(IPAddress.Loopback, 0);
        first.Start();
        var firstPort = ((IPEndPoint)first.LocalEndpoint).Port;
        var serving = Task.Run(async () =>
        {
            using var client = await first.AcceptTcpClientAsync(Ct);
            var stream = client.GetStream();
            var buffer = new byte[8192];
            var read = 0;
            while (!Encoding.ASCII.GetString(buffer, 0, read).Contains("\r\n\r\n", StringComparison.Ordinal) && read < buffer.Length)
            {
                read += await stream.ReadAsync(buffer.AsMemory(read), Ct);
            }

            var response = $"HTTP/1.1 302 Found\r\nLocation: http://127.0.0.1:{redirectedPort}/elsewhere\r\nContent-Length: 0\r\nConnection: close\r\n\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(response), Ct);
        }, Ct);

        // The service as the host builds it: the typed client carries the configured handler.
        var service = _fixture.Api.Services.GetRequiredService<IWebhookDeliveryService>();
        var result = await service.DeliverAsync(Webhook($"http://127.0.0.1:{firstPort}/hook"), Delivery(), Ct);
        await serving;
        await Task.Delay(300, Ct);

        result.Success.Should().BeFalse("a redirect is an unsuccessful delivery");
        result.HttpStatusCode.Should().Be(302);
        redirected.Pending().Should().BeFalse("the redirect target was never contacted");
    }

    private WebhookDeliveryService DeliveryService(HttpMessageHandler handler, WebhookDestinationOptions options) =>
        new(
            new HttpClient(handler),
            _fixture.Api.Services.GetRequiredService<IWebhookManager>(),
            _fixture.Api.Services.GetRequiredService<WebhookSignatureService>(),
            NullLogger<WebhookDeliveryService>.Instance,
            Options.Create(options));

    private static WebhookMetadata Webhook(string url) => new()
    {
        WebhookId = Guid.NewGuid(),
        ProjectId = Guid.NewGuid(),
        TableId = Guid.NewGuid(),
        LogicalName = "trust",
        Url = url,
        Secret = "signing-secret",
    };

    private static WebhookDelivery Delivery() => new()
    {
        DeliveryId = Guid.NewGuid(),
        WebhookId = Guid.NewGuid(),
        Event = WebhookEvent.Insert,
        Payload = JsonDocument.Parse("""{"event":"insert"}"""),
    };

    private sealed class CountingHandler : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
