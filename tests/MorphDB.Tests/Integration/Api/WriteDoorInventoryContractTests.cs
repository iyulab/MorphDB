using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration.Api;

/// <summary>
/// The door × input matrices (<c>WriteDoor*ContractTests</c>, <c>UpsertKeepsWhatItDidNotSay…</c>) list the doors
/// that change a row by hand, and a count of zero broken cells says only as much as that list holds. So the host
/// is asked which routes change rows, and every one has to be written down here with where it is tested — in a
/// matrix, or only by tests of its own route. A route added later fails this until someone decides which it is,
/// and a route that is only tested on its own is a named gap, not a cell nobody knows is missing.
/// <para>
/// The GraphQL mutations are not routes; the matrices carry them as "graphql …" doors.
/// </para>
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class WriteDoorInventoryContractTests
{
    private readonly ApiIntegrationFixture _fixture;

    public WriteDoorInventoryContractTests(ApiIntegrationFixture fixture) => _fixture = fixture;

    private const string Matrix = "in a door matrix: ";
    private const string OwnTestsOnly = "route tests only (not in a door matrix): ";

    /// <summary>Every route that creates, changes or deletes rows of a table, and where it is tested.</summary>
    private static readonly Dictionary<string, string> Doors = new(StringComparer.Ordinal)
    {
        ["POST api/data/{table}"] = Matrix + "WriteDoorConstraint (single insert)",
        ["PATCH api/data/{table}/{id:guid}"] = Matrix + "WriteDoorSystemColumn · WriteDoorUpdateConstraint · WriteDoorAuditColumn · WriteDoorPolicy (rest patch)",
        ["DELETE api/data/{table}/{id:guid}"] = Matrix + "WriteDoorAuditColumn (rest delete)",
        ["POST api/batch/data"] = Matrix + "WriteDoorConstraint · WriteDoorSystemColumn · WriteDoorUpdateConstraint · WriteDoorAuditColumn · WriteDoorPolicy (batch operations)",
        ["POST api/batch/data/{table}/insert"] = Matrix + "WriteDoorConstraint (batch insert)",
        ["PATCH api/batch/data/{table}"] = Matrix + "WriteDoorSystemColumn · WriteDoorUpdateConstraint · WriteDoorAuditColumn · WriteDoorPolicy (update by filter)",
        ["DELETE api/batch/data/{table}"] = Matrix + "WriteDoorAuditColumn (delete by filter)",
        ["PUT api/batch/data/{table}"] = Matrix + "WriteDoorSystemColumn · WriteDoorUpdateConstraint · WriteDoorAuditColumn · WriteDoorPolicy · UpsertKeepsWhatItDidNotSay (rest put upsert)",
        ["POST api/batch/data/{table}/seed"] = Matrix + "the same matrices (seed)",
        ["POST api/bulk/{table}/import/csv"] = Matrix + "WriteDoorConstraint (csv import)",
        ["POST api/bulk/{table}/import/json"] = Matrix + "WriteDoorConstraint (json import)",
        ["PATCH odata/{entitySet}({key})"] = Matrix + "WriteDoorSystemColumn · WriteDoorUpdateConstraint (odata patch)",
        ["POST api/bulk/{table}/import/ndjson"] = OwnTestsOnly + "BulkApiTests",
        ["POST odata/{entitySet}"] = OwnTestsOnly + "ODataApiTests",
        ["DELETE odata/{entitySet}({key})"] = OwnTestsOnly + "ODataApiTests",
        ["POST odata/$batch"] = OwnTestsOnly + "ODataApiTests",
        ["POST api/batch/transaction"] = OwnTestsOnly + "TransactionWriteContractTests · TransactionUpsertContractTests",
        ["PATCH api/data/{table}/{id}/finalize"] = OwnTestsOnly + "TransactionApiTests (row-state finalize)",
        ["POST api/data/{table}/finalize"] = OwnTestsOnly + "TransactionApiTests (row-state finalize)",
    };

    private static bool ChangesRows(string method, string route) =>
        method is "POST" or "PUT" or "PATCH" or "DELETE"
        && (route.StartsWith("api/data/", StringComparison.Ordinal)
            || route.StartsWith("api/batch/", StringComparison.Ordinal)
            || route.StartsWith("odata/", StringComparison.Ordinal)
            || (route.StartsWith("api/bulk/", StringComparison.Ordinal) && route.Contains("/import/", StringComparison.Ordinal)))
        && !route.EndsWith("/query", StringComparison.Ordinal)
        && !route.EndsWith("/aggregate", StringComparison.Ordinal);

    [Fact]
    public void Every_route_that_changes_rows_is_written_down_with_where_it_is_tested()
    {
        var routed = _fixture.Api.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .SelectMany(e => (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? []).Select(m => (Method: m, Route: e.RoutePattern.RawText ?? string.Empty)))
            .Where(r => ChangesRows(r.Method, r.Route))
            .Select(r => $"{r.Method} {r.Route}")
            .ToHashSet(StringComparer.Ordinal);

        routed.Should().NotBeEmpty("the service maps routes that change rows");
        routed.Except(Doors.Keys).Should().BeEmpty("a route that changes rows is written down here, in a matrix or as a gap, before it ships");
        Doors.Keys.Except(routed).Should().BeEmpty("this list names a route the service maps");
    }
}
