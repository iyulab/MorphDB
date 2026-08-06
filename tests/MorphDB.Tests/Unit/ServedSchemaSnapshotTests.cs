using HotChocolate.Execution;
using Microsoft.Extensions.DependencyInjection;
using MorphDB.Service.GraphQL;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Unit;

/// <summary>
/// Holds the whole served schema, not the parts someone thought to name.
/// <para>
/// The other schema test pins the three root type names, because those had moved once. This one
/// exists because naming what to watch only catches what has already gone wrong: a server upgrade
/// changed a scalar's <c>@specifiedBy</c> URL and four scalar descriptions, and every test stayed
/// green — the schema is published to anyone who introspects it, so any part of it moving is a
/// change a consumer can see.
/// </para>
/// <para>
/// <b>When this fails:</b> read the diff and decide. An intended change is recorded in the release
/// notes and the file is updated in the same commit; an unintended one is the finding. Updating the
/// file to make the build green, without reading what moved, is the one use that defeats it — the
/// file is the contract, not a cache of the last run.
/// </para>
/// <para>
/// Building the schema needs no database: the shape comes from CLR types and resolvers read
/// metadata per request. So this stays a unit test.
/// </para>
/// </summary>
public class ServedSchemaSnapshotTests
{
    private const string SnapshotPath = "tests/MorphDB.Tests/Contracts/served-schema.graphql";

    [Fact]
    public async Task The_served_schema_is_the_one_recorded_as_published()
    {
        var schema = await new ServiceCollection()
            .AddGraphQLServer()
            .AddMorphDbTypes()
            .BuildSchemaAsync();

        // Line endings are the checkout's business, not the schema's.
        static string Normalize(string sdl) =>
            sdl.Replace("\r\n", "\n").TrimEnd() + "\n";

        Normalize(schema.ToString()).Should().Be(
            Normalize(ConstraintBoundaryDoc.ReadRepoFile(SnapshotPath)),
            "the served schema is a published contract; a difference here is either a release note "
            + "or a defect, and both are decided by reading the diff");
    }
}
