using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Models;
using MorphDB.Core.Security;
using MorphDB.Npgsql.Security;
using MorphDB.Service.Controllers;
using MorphDB.Service.Models.Api;
using MorphDB.Service.Security;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration.Api;

/// <summary>
/// The detection tests for connection secrets.
/// <para>
/// The previous authentication machinery was removed because it advertised a boundary nothing
/// enforced: a production image had no way to mint a key, so no request was ever actually refused.
/// The condition for bringing the concept back was that turning enforcement off has to turn this
/// file red. Each test below names what it would stop detecting if the corresponding production
/// code were deleted, and the removal was run to confirm it.
/// </para>
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class SecretEnforcementTests
{
    private const string MasterSecret = "mdb_test_master_secret_value";

    private static readonly string[] ExpectedExemptPrefixes = ["/health", "/metrics"];
    private static readonly string[] ReservedRoleAttempts = ["master", "service", "MASTER"];

    private readonly ApiIntegrationFixture _fixture;

    public SecretEnforcementTests(ApiIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    private HttpClient EnforcedClient(string? secret = null, Guid? projectId = null)
    {
        var client = _fixture.Api.WithMasterSecret(MasterSecret).CreateClient();

        var scope = projectId ?? _fixture.Api.ProjectId;
        if (scope != Guid.Empty)
        {
            client.DefaultRequestHeaders.Add("X-Project-Id", scope.ToString());
        }

        if (secret is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secret);
        }

        return client;
    }

    // (1) The body of the boundary. Delete the deny branch in SecretAuthenticationMiddleware and
    // this goes green on a 200.
    [Fact]
    public async Task A_request_with_no_secret_is_refused_when_a_master_secret_is_injected()
    {
        var response = await EnforcedClient().GetAsync("/api/schema/tables");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        error!.Code.Should().Be("UNAUTHENTICATED");
    }

    // (2) The bypass that a check placed inside SecurityContextMiddleware would have left open:
    // that middleware only runs its body when X-Project-Id is present and parses, so enforcement
    // living there could be skipped by omitting the header entirely.
    [Fact]
    public async Task Omitting_the_project_header_does_not_skip_authentication()
    {
        var client = _fixture.Api.WithMasterSecret(MasterSecret).CreateClient();

        var response = await client.GetAsync("/api/schema/tables");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "an unauthenticated request must be refused on its own account, not only when it also " +
            "names a project — otherwise the header is the switch that turns the boundary off");
    }

    // (3) Presence is not the same as being correct: a check that only asked whether a header
    // existed would pass (1) and (2) and still authenticate anybody.
    [Fact]
    public async Task An_unrecognized_secret_is_refused()
    {
        var response = await EnforcedClient("mdb_not_a_real_secret").GetAsync("/api/schema/tables");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The_master_secret_is_accepted()
    {
        var response = await EnforcedClient(MasterSecret).GetAsync("/api/schema/tables");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // (4) Enforcement is opt-in on the master secret being injected. Without this, a deployment
    // that supplies nothing would break on upgrade — and the claim the docs make about the
    // unconfigured shape would stop being true.
    [Fact]
    public async Task Without_an_injected_master_secret_nothing_is_required()
    {
        var response = await _fixture.Api.Client.GetAsync("/api/schema/tables");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // (5) The exemption list is the service's entire unauthenticated surface, so it is pinned
    // rather than left to whoever edits the middleware next.
    [Theory]
    [InlineData("/health")]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    [InlineData("/metrics")]
    public async Task Machine_surfaces_answer_without_a_secret(string route)
    {
        var response = await EnforcedClient().GetAsync(route);

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized,
            "an orchestrator polls health and a scraper collects metrics before any credential is " +
            "distributed; neither reads project data");
    }

    [Fact]
    public void The_exemption_list_does_not_grow_unnoticed()
    {
        SecretAuthenticationMiddleware.ExemptPathPrefixes.Should().BeEquivalentTo(
            ExpectedExemptPrefixes,
            "every prefix here is a route that answers without a secret — adding one is a change to " +
            "the attack surface and belongs in a commit that says so");
    }

    // (6) The acyclic-bootstrap invariant. The previous machinery died of a bootstrap circle;
    // "there is no circle now" has to be a test, not a claim in a design document.
    [Fact]
    public async Task No_route_can_mint_a_reserved_role()
    {
        var master = EnforcedClient(MasterSecret);

        foreach (var reserved in ReservedRoleAttempts)
        {
            var response = await master.PostAsJsonAsync("/api/security/secrets", new IssueSecretApiRequest
            {
                Name = $"escalation-attempt-{reserved}",
                Role = reserved
            });

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
                $"'{reserved}' is reserved: privilege must originate at start-up, never in-band");
        }
    }

    [Fact]
    public async Task An_issued_secret_cannot_manage_secrets()
    {
        var master = EnforcedClient(MasterSecret);
        var issued = await IssueAsync(master, "delegate", "writer");

        var response = await EnforcedClient(issued).GetAsync("/api/security/secrets");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "issuing credentials is the master secret's authority alone");
    }

    [Fact]
    public async Task An_issued_secret_authenticates_and_a_revoked_one_stops()
    {
        var master = EnforcedClient(MasterSecret);
        var issued = await IssueAsync(master, "revocation-subject", "reader");

        var before = await EnforcedClient(issued).GetAsync("/api/schema/tables");
        before.StatusCode.Should().Be(HttpStatusCode.OK);

        var listed = await master.GetFromJsonAsync<List<SecretResponse>>("/api/security/secrets");
        var subject = listed!.First(s => s.Name == "revocation-subject");

        var revoked = await master.DeleteAsync($"/api/security/secrets/{subject.SecretId}");
        revoked.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var after = await EnforcedClient(issued).GetAsync("/api/schema/tables");
        after.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "revocation that leaves the credential working is bookkeeping, not revocation");
    }

    [Fact]
    public async Task A_secret_confined_to_a_project_cannot_address_another()
    {
        var master = EnforcedClient(MasterSecret);
        var confined = await IssueAsync(master, "confined", "reader", _fixture.Api.ProjectId);

        var ownProject = await EnforcedClient(confined).GetAsync("/api/schema/tables");
        ownProject.StatusCode.Should().Be(HttpStatusCode.OK);

        var otherProject = await EnforcedClient(confined, Guid.NewGuid()).GetAsync("/api/schema/tables");
        otherProject.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "a project column that no check reads is a boundary that exists only in the schema");
    }

    // (7) A placeholder existing is not the same as it being filled. The role has to survive the
    // whole way from the issued secret to the WHERE clause row-level security builds -- which is a
    // different proposition from 'SecurityContext has a Role property', and the one that matters.
    [Fact]
    public async Task The_role_an_issued_secret_carries_reaches_row_level_security()
    {
        var master = EnforcedClient(MasterSecret);
        var analystSecret = await IssueAsync(master, "analyst-key", "analyst");

        // Run the real middleware over a real request and keep the context it produces. Rebuilding
        // that context by hand here would make this test agree with itself rather than with the
        // code that runs in production.
        var services = _fixture.Api.Services;
        var accessor = new SecurityContextAccessor();

        // Captured inside the continuation rather than read after the await: the accessor stores the
        // context in an AsyncLocal, and an AsyncLocal assigned inside a method does not flow back
        // out to its caller. Downstream is the only place the context is ever observed in
        // production, so this is also the honest place to observe it here.
        SecurityContext? observed = null;
        var middleware = new SecretAuthenticationMiddleware(_ =>
        {
            observed = accessor.ContextOrNull;
            return Task.CompletedTask;
        });

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Authorization = $"Bearer {analystSecret}";
        accessor.SetContext(SecurityContext.Anonymous(_fixture.Api.ProjectId));

        await middleware.InvokeAsync(
            httpContext,
            new SecretOptions { MasterSecret = MasterSecret },
            services.GetRequiredService<ISecretService>(),
            accessor);

        observed.Should().NotBeNull("the request must have been let through, not denied");
        var authenticated = observed!;
        authenticated.IsAuthenticated.Should().BeTrue();
        authenticated.Role.Should().Be("analyst");
        authenticated.BypassRls.Should().BeFalse("only the master secret bypasses row-level security");

        // Now the second half: that context, fed to the real policy evaluator, must put the role
        // into the clause.
        var tableName = $"rls_role_probe_{Guid.NewGuid():N}"[..24];
        var schemaManager = services.GetRequiredService<MorphDB.Core.Abstractions.ISchemaManager>();
        await schemaManager.CreateTableAsync(new CreateTableRequest
        {
            ProjectId = _fixture.Api.ProjectId,
            LogicalName = tableName,
            Columns =
            [
                new CreateColumnRequest
                {
                    LogicalName = "title",
                    DataType = MorphDataType.Text,
                    IsNullable = true
                }
            ]
        });

        var policies = services.GetRequiredService<ISecurityPolicyService>();
        await policies.CreatePolicyAsync(_fixture.Api.ProjectId, new CreatePolicyRequest
        {
            Name = "analysts-only",
            TableName = tableName,
            PolicyType = PolicyType.Select,
            Expression = "{{role}} = 'analyst'"
        });

        var clause = await policies.EvaluatePoliciesAsync(
            _fixture.Api.ProjectId, tableName, PolicyType.Select, authenticated);

        clause.Should().NotBeNull();
        clause.Should().Contain("'analyst'",
            "the role the secret carries is what {{role}} resolves to — a placeholder that resolves " +
            "to NULL would silently match nothing and look like a working policy");
    }

    private static async Task<string> IssueAsync(
        HttpClient master,
        string name,
        string role,
        Guid? projectId = null)
    {
        var response = await master.PostAsJsonAsync("/api/security/secrets", new IssueSecretApiRequest
        {
            Name = name,
            Role = role,
            ProjectId = projectId
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var issued = await response.Content.ReadFromJsonAsync<IssuedSecretResponse>();
        issued!.Secret.Should().StartWith("mdb_");
        return issued.Secret;
    }

    // (5b) The exemption list is only meaningful if the *non*-exempt surfaces are actually covered.
    // This service is not only controllers: GraphQL and the SignalR hub are mapped separately, and
    // a middleware that happened to sit in the wrong place could leave either wide open while every
    // REST test above still passed. The docs now tell consumers that every endpoint but the probes
    // requires a secret -- these hold that sentence to the two surfaces it is easiest to forget.
    [Theory]
    [InlineData("/graphql")]
    [InlineData("/hubs/morph/negotiate")]
    public async Task Non_rest_surfaces_are_covered_too(string route)
    {
        var response = await EnforcedClient().PostAsync(route, new StringContent("{}", Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            $"{route} is not on the exemption list, so it must answer like every other endpoint -- " +
            "a boundary that holds on REST and not on GraphQL is not a boundary");
    }

    // The plaintext is promised to exist "exactly once, in the response that issues it", and only a
    // hash is stored. Audit logging runs one middleware after authentication and records every
    // request, so that promise is only true for as long as the audit record stays clear of the
    // Authorization header and the issuance response body.
    [Fact]
    public async Task The_audit_trail_records_who_acted_without_recording_the_credential()
    {
        var master = EnforcedClient(MasterSecret);
        var issued = await IssueAsync(master, "audit-subject", "reader");

        await EnforcedClient(issued).GetAsync("/api/schema/tables");

        var auditService = _fixture.Api.Services.GetRequiredService<IAuditService>();
        var entries = await auditService.QueryAsync(_fixture.Api.ProjectId, new AuditLogQuery());

        var serialized = JsonSerializer.Serialize(entries);

        serialized.Should().NotContain(issued,
            "an issued secret written into the audit trail is a credential stored in cleartext, " +
            "which is the whole thing hashing it at rest was for");
        serialized.Should().NotContain(MasterSecret,
            "the master secret rides on every authenticated request as a header");

        // The other half: an audit trail that cannot say who acted is worth less than one that can,
        // and now that the caller is identified there is no reason to keep recording nobody.
        var listed = await master.GetFromJsonAsync<List<SecretResponse>>("/api/security/secrets");
        var subject = listed!.First(s => s.Name == "audit-subject");

        serialized.Should().Contain(subject.SecretId.ToString(),
            "the secret's id names the actor without being the credential");
    }
}
