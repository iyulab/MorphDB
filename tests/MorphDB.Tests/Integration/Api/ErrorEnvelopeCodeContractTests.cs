using System.Net.Http.Json;
using System.Text.Json;
using MorphDB.Service.Models.Api;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration.Api;

/// <summary>
/// The half of the error-code gate that looks at a response. Its companion
/// (<c>DocsErrorCodeParityTests</c>) compares the documented table to a hand-audited inventory of
/// production sites — two written lists, which can agree perfectly while the wire carries something
/// else entirely. Envelopes were reaching callers with <c>"code": null</c> across two controllers
/// while that gate stayed green, because a null is neither a documented code nor an undocumented
/// one: it is outside the set the gate compares.
/// <para>
/// So this probes real error paths and holds every reply to two claims: it carries a code, and the
/// code is one <c>docs/API.md</c> documents. Callers are told to branch on <c>code</c>; an absent
/// one sends them to their fallback branch for an error the API does describe, and an undocumented
/// one sends them there for an error they could have handled.
/// </para>
/// <para>
/// Which code each path answers is pinned elsewhere (<c>ErrorSurfaceContractTests</c>). Restating
/// it here would mean two places to update for one contract change, so this asks only the question
/// the other tests cannot: is the envelope answerable at all.
/// </para>
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class ErrorEnvelopeCodeContractTests
{
    private readonly ApiIntegrationFixture _fixture;
    private readonly HttpClient _client;

    public ErrorEnvelopeCodeContractTests(ApiIntegrationFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.Api.Client;
    }

    [Fact]
    public async Task Every_error_response_carries_a_documented_code()
    {
        var documented = DocsErrorCodes.Documented();
        var table = await CreateTableAsync();
        var probes = await ProbeAsync(table);

        probes.Should().HaveCountGreaterThan(10,
            "a probe list that shrank to nothing would pass this gate while proving nothing");

        var withoutCode = probes.Where(p => string.IsNullOrEmpty(p.Code)).ToList();
        withoutCode.Should().BeEmpty(
            "docs/API.md tells callers to branch on `code`; these paths answered an error without " +
            "one: " + Describe(withoutCode));

        var undocumented = probes.Where(p => !documented.Contains(p.Code!)).ToList();
        undocumented.Should().BeEmpty(
            "a code the server answers but the docs do not list is invisible to the caller who " +
            "would have handled it: " + Describe(undocumented));
    }

    /// <summary>
    /// Every request here is expected to fail. A probe that unexpectedly succeeds is reported
    /// rather than skipped — a path that stopped producing an error is no longer covering
    /// anything, and silently dropping it is how a gate's reach shrinks unnoticed.
    /// </summary>
    [Fact]
    public async Task Every_probe_still_reaches_an_error_path()
    {
        var table = await CreateTableAsync();

        var succeeded = (await ProbeAsync(table)).Where(p => !p.IsError).ToList();

        succeeded.Should().BeEmpty(
            "these probes no longer produce the error they were written to observe, so they are " +
            "not testing the envelope any more: " + Describe(succeeded));
    }

    private async Task<List<Probe>> ProbeAsync(string table)
    {
        var results = new List<Probe>();

        async Task ProbeRequestAsync(string label, Func<HttpClient, Task<HttpResponseMessage>> send)
        {
            using var response = await send(_client);
            var body = await response.Content.ReadAsStringAsync();
            string? code = null;
            try
            {
                code = JsonSerializer.Deserialize<ErrorEnvelope>(body,
                    JsonSerializerOptions.Web)?.Code;
            }
            catch (JsonException)
            {
                // Left null: an error reply that is not an envelope has no code by definition.
            }

            results.Add(new Probe(label, (int)response.StatusCode, code, body));
        }

        var reserved = Guid.NewGuid();

        await ProbeRequestAsync("missing table",
            c => c.GetAsync("/api/data/no_such_table_here"));
        await ProbeRequestAsync("unknown column type",
            c => c.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
            {
                Name = $"envl_{Guid.NewGuid():N}"[..24],
                Columns = [new CreateColumnApiRequest { Name = "a", Type = "varchar2" }]
            }));
        await ProbeRequestAsync("duplicate table",
            c => c.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
            {
                Name = table,
                Columns = [new CreateColumnApiRequest { Name = "a", Type = "text" }]
            }));
        await ProbeRequestAsync("reserved column name",
            c => c.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
            {
                Name = $"envl_{Guid.NewGuid():N}"[..24],
                Columns = [new CreateColumnApiRequest { Name = "_mine", Type = "text" }]
            }));
        await ProbeRequestAsync("unparseable check expression",
            c => c.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
            {
                Name = $"envl_{Guid.NewGuid():N}"[..24],
                Columns = [new CreateColumnApiRequest { Name = "a", Type = "integer", Check = "a > (1" }]
            }));
        await ProbeRequestAsync("missing index",
            c => c.DeleteAsync($"/api/schema/indexes/{reserved}"));
        await ProbeRequestAsync("missing relation",
            c => c.DeleteAsync($"/api/schema/relations/{reserved}"));
        await ProbeRequestAsync("missing webhook",
            c => c.GetAsync($"/api/webhooks/{reserved}"));
        await ProbeRequestAsync("missing view",
            c => c.GetAsync("/api/views/no_such_view_here"));
        await ProbeRequestAsync("missing record",
            c => c.GetAsync($"/api/data/{table}/{reserved}"));
        await ProbeRequestAsync("unknown field on write",
            c => c.PostAsJsonAsync($"/api/data/{table}",
                new Dictionary<string, object?> { ["nosuch_field"] = 1 }));
        await ProbeRequestAsync("filter naming an absent column",
            c => c.GetAsync($"/api/data/{table}?filter=nosuch:eq:1"));
        await ProbeRequestAsync("batch write with no rows",
            c => c.PostAsJsonAsync($"/api/batch/data/{table}/insert",
                new { records = Array.Empty<object>() }));
        await ProbeRequestAsync("missing bulk job",
            c => c.GetAsync($"/api/bulk/import/{reserved}"));
        await ProbeRequestAsync("retention window that cannot be applied",
            c => c.PostAsJsonAsync("/api/projects", new
            {
                Name = $"envl_{Guid.NewGuid():N}"[..24],
                Settings = new ProjectSettingsApiModel { AuditLogRetentionDays = 0 },
            }));

        // A request that never says which project it applies to takes a different door: the
        // envelope is written before any controller runs.
        using var noProject = _fixture.Api.CreateClientWithProject(Guid.NewGuid());
        noProject.DefaultRequestHeaders.Remove("X-Project-Id");
        await ProbeRequestAsync("no project header", _ => noProject.GetAsync($"/api/data/{table}"));

        return results;
    }

    private async Task<string> CreateTableAsync()
    {
        var name = $"envl_{Guid.NewGuid():N}"[..24];
        var response = await _client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = name,
            Columns = [new CreateColumnApiRequest { Name = "label", Type = "text", Nullable = true }]
        });
        response.EnsureSuccessStatusCode();
        return name;
    }

    private static string Describe(IEnumerable<Probe> probes) =>
        string.Join("; ", probes.Select(p => $"[{p.Label}] {p.Status} {Trim(p.Body)}"));

    private static string Trim(string body) =>
        body.Length <= 160 ? body : body[..160] + "…";

    private sealed record Probe(string Label, int Status, string? Code, string Body)
    {
        public bool IsError => Status >= 400;
    }

    private sealed record ErrorEnvelope(string? Error, string? Message, string? Code);
}
