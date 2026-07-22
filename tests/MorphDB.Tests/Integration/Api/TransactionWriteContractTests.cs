using System.Net.Http.Json;
using System.Text.Json;
using MorphDB.Service.Models.Api;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration.Api;

/// <summary>
/// The write contract must be one contract, however a row arrives — the transaction door included.
/// Cycle-70 pinned REST↔GraphQL equivalence; §3.10-C4 asked the same of the transaction service,
/// whose pipeline integration (ConnectionScope, commit 4ca28b0) predates these tests but had no
/// equivalence coverage: nothing proved a bad row is refused through <c>POST /api/batch/transaction</c>
/// for the same reason REST refuses it, or that a transactional row carries the same
/// pipeline-applied system columns. Per-door unit coverage cannot catch the doors drifting apart.
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class TransactionWriteContractTests
{
    private readonly HttpClient _client;

    public TransactionWriteContractTests(ApiIntegrationFixture fixture)
    {
        _client = fixture.Api.Client;
    }

    private async Task<string> CreateTableAsync(params CreateColumnApiRequest[] columns)
    {
        var tableName = $"txnwrite_{Guid.NewGuid():N}"[..30];
        var response = await _client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = tableName,
            Columns = columns,
        });
        response.EnsureSuccessStatusCode();
        return tableName;
    }

    private async Task<TransactionApiResponse> ExecuteAsync(object request)
    {
        var response = await _client.PostAsJsonAsync("/api/batch/transaction", request);
        var body = await response.Content.ReadFromJsonAsync<TransactionApiResponse>();
        return body!;
    }

    [Fact]
    public async Task An_unknown_field_is_refused_through_the_transaction_door_with_the_rest_code()
    {
        var table = await CreateTableAsync(
            new CreateColumnApiRequest { Name = "email", Type = "text", Nullable = true });

        // The same bad row REST refuses with UNKNOWN_COLUMN (ErrorSurfaceContractTests).
        var result = await ExecuteAsync(new
        {
            operations = new[]
            {
                new { method = "INSERT", table, data = new Dictionary<string, object?> { ["emial_typo"] = "lost?" } },
            },
        });

        result.Success.Should().BeFalse("a typo'd field must not vanish inside a transaction either");
        var failed = result.Results.Should().ContainSingle().Subject;
        failed.ValidationErrors.Should().NotBeNull();
        failed.ValidationErrors!.Should().Contain(e => e.Code == "UNKNOWN_COLUMN" && e.Field == "emial_typo",
            "the transaction door must refuse for the same machine-readable reason as REST");
    }

    [Fact]
    public async Task A_check_violation_is_refused_through_the_transaction_door()
    {
        var table = await CreateTableAsync(
            new CreateColumnApiRequest { Name = "name", Type = "text", Nullable = false },
            new CreateColumnApiRequest
            {
                Name = "status",
                Type = "text",
                Nullable = false,
                Check = "status = 'active' OR status = 'pending'",
            });

        var result = await ExecuteAsync(new
        {
            operations = new[]
            {
                new
                {
                    method = "INSERT",
                    table,
                    data = new Dictionary<string, object?> { ["name"] = "t", ["status"] = "banned" },
                },
            },
        });

        result.Success.Should().BeFalse("virtual CHECK constraints must hold on every door");
        result.Results.Single().ValidationErrors!.Should().Contain(e => e.Code == "CHECK_VIOLATION");
    }

    [Fact]
    public async Task A_transactional_row_carries_the_same_pipeline_system_columns_as_a_rest_row()
    {
        var table = await CreateTableAsync(
            new CreateColumnApiRequest { Name = "email", Type = "text", Nullable = true });

        var viaRest = await _client.PostAsJsonAsync($"/api/data/{table}",
            new Dictionary<string, object?> { ["email"] = "rest@example.com" });
        viaRest.EnsureSuccessStatusCode();
        var restRow = JsonDocument.Parse(await viaRest.Content.ReadAsStringAsync())
            .RootElement.GetProperty("data");

        var txn = await ExecuteAsync(new
        {
            returnFullRecords = true,
            operations = new[]
            {
                new { method = "INSERT", table, data = new Dictionary<string, object?> { ["email"] = "txn@example.com" } },
            },
        });

        txn.Success.Should().BeTrue();
        var txnRow = txn.Results.Single().Data!;
        foreach (var systemKey in new[] { "_id", "_created_at", "_updated_at", "_version" })
        {
            restRow.TryGetProperty(systemKey, out _).Should().BeTrue($"REST row must carry {systemKey}");
            txnRow.Keys.Should().Contain(systemKey,
                $"a transactional row must carry the same pipeline-applied {systemKey}");
        }

        txnRow.Keys.Should().NotContain("project_id", "B2 non-exposure holds on this door too");
    }

    [Fact]
    public async Task A_failed_operation_rolls_back_the_ones_before_it()
    {
        var table = await CreateTableAsync(
            new CreateColumnApiRequest { Name = "email", Type = "text", Nullable = true });

        var result = await ExecuteAsync(new
        {
            operations = new object[]
            {
                new { method = "INSERT", table, data = new Dictionary<string, object?> { ["email"] = "first@example.com" } },
                new { method = "INSERT", table, data = new Dictionary<string, object?> { ["emial_typo"] = "boom" } },
            },
        });

        result.Success.Should().BeFalse();
        var rows = await _client.GetFromJsonAsync<JsonElement>($"/api/data/{table}");
        rows.GetProperty("data").GetArrayLength().Should().Be(0,
            "the pipeline write ran inside the transaction's connection scope, so the rollback must take it too");
    }
}
