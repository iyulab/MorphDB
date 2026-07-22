using System.Net;
using System.Net.Http.Json;
using MorphDB.Core.Security;
using MorphDB.Service.Controllers;
using MorphDB.Service.Models.Api;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration.Api;

/// <summary>
/// The security surface after the authentication sunset: the policy endpoints answer any caller
/// (the service has no identity to demand), and row-level security still binds over HTTP because
/// the pipeline supplies the ambient security context the query layer evaluates against.
/// <para>
/// The second half is the one that has to be measured. An absent context does not fail a query —
/// it skips policy evaluation entirely ("allow all"), so losing the context middleware would not
/// break a single existing test; it would silently stop enforcing every policy. The RLS test here
/// goes red if <c>UseSecurityContext()</c> leaves the pipeline.
/// </para>
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class SecurityApiTests
{
    private readonly HttpClient _client;

    public SecurityApiTests(ApiIntegrationFixture fixture)
    {
        _client = fixture.Api.Client;
    }

    private async Task<string> SetupTestTableAsync()
    {
        var tableName = $"rls_test_{Guid.NewGuid():N}"[..30];
        var response = await _client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = tableName,
            Columns =
            [
                new CreateColumnApiRequest { Name = "name", Type = "text", Nullable = false }
            ]
        });
        response.EnsureSuccessStatusCode();
        return tableName;
    }

    [Fact]
    public async Task The_policy_endpoints_answer_a_caller_with_no_identity()
    {
        var tableName = await SetupTestTableAsync();

        var response = await _client.GetAsync($"/api/security/policies/{tableName}");

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "the service is unauthenticated by design; there is no identity it could demand");
    }

    [Fact]
    public async Task A_select_policy_binds_a_plain_http_query()
    {
        var tableName = await SetupTestTableAsync();

        var insert = await _client.PostAsJsonAsync($"/api/data/{tableName}",
            new Dictionary<string, object?> { ["name"] = "visible before the policy" });
        insert.StatusCode.Should().Be(HttpStatusCode.Created);

        var before = await _client.GetFromJsonAsync<PagedResponse<DataRecordResponse>>(
            $"/api/data/{tableName}");
        before!.Data.Should().HaveCount(1, "the row exists and no policy restricts it yet");

        var policy = await _client.PostAsJsonAsync("/api/security/policies", new CreateSecurityPolicyRequest
        {
            Name = "nobody_reads",
            TableName = tableName,
            PolicyType = PolicyType.Select,
            Expression = "1 = 0"
        });
        policy.StatusCode.Should().Be(HttpStatusCode.Created);

        var after = await _client.GetFromJsonAsync<PagedResponse<DataRecordResponse>>(
            $"/api/data/{tableName}");
        after!.Data.Should().BeEmpty(
            "the policy filters every row; if this holds rows, policy evaluation was skipped — " +
            "the ambient security context is not reaching the query layer");
    }
}
