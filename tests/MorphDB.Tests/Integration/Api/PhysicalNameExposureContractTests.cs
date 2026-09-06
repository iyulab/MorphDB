using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Models;
using MorphDB.Service.Models.Api;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration.Api;

/// <summary>
/// The logical/physical separation ("Virtual DOM for Database") is the layer's reason to exist —
/// <c>docs/CONSTITUTION.md</c> says the contract is broken if physical structure ever reaches a
/// consumer-facing surface. It leaked on this same class of surface four times before (project_id,
/// three more surfaces, the view builder's join/expression translation) with each fix scoped to
/// only the surface that had just been caught. These tests pin the same predicate
/// (<see cref="PhysicalNameGuard"/>) across every surface a row's shape reaches a caller through —
/// REST, GraphQL, export, and view — so the next surface added is checked by construction rather
/// than by the next backlog-discover run happening to look there.
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class PhysicalNameExposureContractTests
{
    private readonly ApiIntegrationFixture _fixture;
    private readonly HttpClient _client;

    public PhysicalNameExposureContractTests(ApiIntegrationFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.Api.Client;
    }

    private async Task<string> CreateTableWithRowAsync(string prefix)
    {
        var tableName = $"{prefix}_{Guid.NewGuid():N}"[..30];
        var create = await _client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = tableName,
            Columns =
            [
                new CreateColumnApiRequest { Name = "customer_email", Type = "text", Nullable = false },
                new CreateColumnApiRequest { Name = "order_total", Type = "decimal", Nullable = true }
            ]
        });
        create.EnsureSuccessStatusCode();

        var insert = await _client.PostAsJsonAsync($"/api/data/{tableName}",
            new Dictionary<string, object?> { ["customer_email"] = "a@example.com", ["order_total"] = 42.5m });
        insert.EnsureSuccessStatusCode();
        return tableName;
    }

    [Fact]
    public async Task Rest_data_and_schema_responses_carry_no_physical_names()
    {
        var tableName = await CreateTableWithRowAsync("physrest");

        var dataResponse = await _client.GetAsync($"/api/data/{tableName}");
        dataResponse.EnsureSuccessStatusCode();
        PhysicalNameGuard.FindPhysicalNames(await dataResponse.Content.ReadAsStringAsync())
            .Should().BeEmpty("REST data rows must carry only the columns the caller declared");

        var schemaResponse = await _client.GetAsync($"/api/schema/tables/{tableName}");
        schemaResponse.EnsureSuccessStatusCode();
        PhysicalNameGuard.FindPhysicalNames(await schemaResponse.Content.ReadAsStringAsync())
            .Should().BeEmpty("the schema surface exists precisely to describe columns by their logical names");
    }

    [Fact]
    public async Task GraphQl_query_and_mutation_responses_carry_no_physical_names()
    {
        var tableName = await CreateTableWithRowAsync("physgql");

        const string query = """
            query($table: String!) {
              records(table: $table, first: 5) {
                edges { node { id data } }
              }
            }
            """;
        var response = await _client.PostAsJsonAsync("/graphql", new { query, variables = new { table = tableName } });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();

        JsonDocument.Parse(body).RootElement.TryGetProperty("errors", out _).Should().BeFalse(body);
        PhysicalNameGuard.FindPhysicalNames(body).Should().BeEmpty(
            "the GraphQL row surface reuses the same logical row dictionaries REST does, and must stay in step with it");
    }

    [Fact]
    public async Task Exported_csv_carries_no_physical_column_names()
    {
        var tableName = await CreateTableWithRowAsync("physexport");

        // The test host removes the BulkJobProcessor background service (it polls tables that
        // don't exist yet at fixture start-up — see ApiTestFixture), so the export is driven
        // directly through IBulkOperationService here instead of waiting on HTTP polling for a
        // background pass that will never run — the same workaround BulkApiTests already uses.
        using var scope = _fixture.Api.Services.CreateScope();
        var bulkService = scope.ServiceProvider.GetRequiredService<IBulkOperationService>();

        var exportJob = await bulkService.StartCsvExportAsync(
            _fixture.Api.ProjectId, tableName, new CsvExportOptions { Delimiter = ',', IncludeHeader = true });

        using var exportStream = new MemoryStream();
        await bulkService.StreamExportAsync(exportJob.JobId, exportStream);
        exportStream.Position = 0;
        var csv = await new StreamReader(exportStream, Encoding.UTF8).ReadToEndAsync();

        PhysicalNameGuard.FindPhysicalNames(csv).Should().BeEmpty(
            "an exported file is a data interchange contract with the same logical-only rule as any API response");
    }

    [Fact]
    public async Task View_query_results_carry_no_physical_names_across_a_join()
    {
        // A join without an alias is exactly the shape that once leaked: the view builder quoted
        // the *logical* table name into the FROM/JOIN clause instead of its physical one.
        var customersTable = $"physview_customers_{Guid.NewGuid():N}"[..30];
        var createCustomers = await _client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = customersTable,
            Columns = [new CreateColumnApiRequest { Name = "tier", Type = "text", Nullable = true }]
        });
        createCustomers.EnsureSuccessStatusCode();
        var customerInsert = await _client.PostAsJsonAsync($"/api/data/{customersTable}",
            new Dictionary<string, object?> { ["tier"] = "gold" });
        customerInsert.EnsureSuccessStatusCode();
        var customer = await customerInsert.Content.ReadFromJsonAsync<DataRecordResponse>();

        var ordersTable = $"physview_orders_{Guid.NewGuid():N}"[..30];
        var createOrders = await _client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = ordersTable,
            Columns =
            [
                new CreateColumnApiRequest { Name = "customer_ref", Type = "uuid", Nullable = false },
                new CreateColumnApiRequest { Name = "order_total", Type = "decimal", Nullable = true }
            ]
        });
        createOrders.EnsureSuccessStatusCode();
        (await _client.PostAsJsonAsync($"/api/data/{ordersTable}",
            new Dictionary<string, object?> { ["customer_ref"] = customer!.Id, ["order_total"] = 42.5m }))
            .EnsureSuccessStatusCode();

        var viewName = $"physview_{Guid.NewGuid():N}"[..30];
        var createView = await _client.PostAsJsonAsync("/api/views", new CreateViewApiRequest
        {
            Name = viewName,
            BaseTable = ordersTable,
            Columns =
            [
                new ViewColumnApiSpec { Source = "order_total", Alias = "order_total" },
                new ViewColumnApiSpec { Source = $"{customersTable}.tier", Alias = "customer_tier" }
            ],
            Joins =
            [
                // No Alias: the joined table is reachable only by its physical name in the
                // FROM/JOIN clause, which is exactly the case the old pass-through translator broke
                // — it quoted the *logical* table name instead, a name the query never introduces.
                new ViewJoinApiSpec
                {
                    Table = customersTable,
                    JoinType = "Inner",
                    Condition = $"{ordersTable}.customer_ref = {customersTable}._id"
                }
            ]
        });
        createView.EnsureSuccessStatusCode();

        var response = await _client.GetAsync($"/api/views/{viewName}/data");
        response.EnsureSuccessStatusCode();
        PhysicalNameGuard.FindPhysicalNames(await response.Content.ReadAsStringAsync()).Should().BeEmpty(
            "a view built over an unaliased join is the exact shape that once leaked physical names into its rows");
    }
}
