using System.Net;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration.Api;

/// <summary>
/// Where the error envelope stops. <c>docs/API.md</c> tells callers to branch on <c>code</c>, and
/// every error this API answers carries one — but a URL matching no route never reaches this API,
/// so the framework answers it with an empty 404 and there is no code to read.
/// <para>
/// The distinction is worth holding rather than describing: a client that parses every non-2xx as an
/// envelope hits a parse failure here, and the cause is that the address is not part of the API at
/// all — not that a request was understood and refused. Its companion
/// (<c>ErrorEnvelopeCodeContractTests</c>) holds the other side, that a routed error always answers.
/// </para>
/// <para>
/// Pinning the empty body also means adopting a catch-all later cannot happen silently: it would be
/// a change to what callers can assume, and this is where that shows up.
/// </para>
/// <para>
/// This runs in process while the claim is about a deployed service, so it is worth saying why the
/// two cannot differ here: nothing on this path is conditioned on the environment. There is no
/// <c>UseStatusCodePages</c> at all — which is why the body is empty — and the exception handler,
/// though registered unconditionally, never runs because no endpoint threw. What the environment
/// does decide (CORS, the GraphQL tool) sits elsewhere in the pipeline. Add a status-code-pages
/// middleware and this fails, which is the point.
/// </para>
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class UnroutedRequestContractTests
{
    private readonly HttpClient _client;

    public UnroutedRequestContractTests(ApiIntegrationFixture fixture)
    {
        _client = fixture.Api.Client;
    }

    [Theory]
    // Under /api, which is where a caller following the docs would mistype, and outside it.
    [InlineData("/api/no-such-resource")]
    [InlineData("/api/schema/tables/name/not-a-subroute")]
    [InlineData("/not-even-the-api")]
    public async Task A_url_that_matches_no_route_is_answered_without_an_envelope(string path)
    {
        var response = await _client.GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).Should().BeEmpty(
            "nothing in this API handled the request, so there is no code for a caller to branch on — " +
            "docs/API.md says so, and a body appearing here would mean callers can assume more than that");
    }
}
