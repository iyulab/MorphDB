using HotChocolate.Execution;
using Microsoft.Extensions.DependencyInjection;
using MorphDB.Service.GraphQL;

namespace MorphDB.Tests.Unit;

/// <summary>
/// Holds the GraphQL schema's independence from the database.
/// <para>
/// The schema's <em>shape</em> comes from CLR types; only the <em>data</em> it serves is read from
/// table metadata, and that read happens inside resolvers at request time. Nothing about building
/// the schema needs a reachable database — and that is a property worth pinning rather than
/// assuming, because it is the property that decides whether a server which composes its schema
/// eagerly at startup can host this service at all. A server that builds the schema before the
/// first request is only a problem for a schema whose shape depends on something not yet there.
/// </para>
/// <para>
/// If a later change makes schema construction reach for table metadata, the shape stops being
/// static and this test fails — which is the point. The failure says the startup contract changed,
/// not merely that a test broke.
/// </para>
/// </summary>
public class GraphQlSchemaIndependenceTests
{
    [Fact]
    public async Task The_schema_builds_with_no_database_and_no_service_registrations()
    {
        // Deliberately bare: no connection string, no repositories, no metadata source. Anything
        // the schema genuinely needed at build time would be missing here.
        var provider = new ServiceCollection()
            .AddGraphQLServer()
            .AddMorphDbTypes()
            .Services
            .BuildServiceProvider();

        var executor = await provider.GetRequestExecutorAsync();

        executor.Schema.QueryType.Should().NotBeNull("the schema's shape comes from CLR types");
    }

    [Fact]
    public async Task The_dynamic_operations_are_present_on_a_schema_built_without_a_database()
    {
        // The previous test would still pass if the dynamic extensions silently dropped out, so
        // this one names what the schema must actually carry.
        var provider = new ServiceCollection()
            .AddGraphQLServer()
            .AddMorphDbTypes()
            .Services
            .BuildServiceProvider();

        var schema = (await provider.GetRequestExecutorAsync()).Schema;

        schema.QueryType.Fields.Should().Contain(f => f.Name == "tables",
            "the table listing is a dynamic-extension field and must survive a database-less build");
        schema.MutationType.Should().NotBeNull("writes are extensions on the mutation root");
    }
}
