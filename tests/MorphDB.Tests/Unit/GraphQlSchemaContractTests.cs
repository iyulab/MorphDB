using HotChocolate.Execution;
using Microsoft.Extensions.DependencyInjection;
using MorphDB.Service.GraphQL;

namespace MorphDB.Tests.Unit;

/// <summary>
/// Holds the served GraphQL schema to the names clients see.
/// <para>
/// The suite had no gate on this at all, and the gap is not theoretical: renaming the CLR class
/// registered as the subscription root renamed the type in the served schema, and every one of the
/// hundreds of other tests stayed green while it happened. A GraphQL schema is a published contract
/// the same way a route or a package is — it just does not go through NuGet, which is what made it
/// easy to treat a rename here as an internal tidy.
/// </para>
/// <para>
/// Building the schema needs no database: the shape comes from CLR types, and resolvers read
/// metadata per request. So this is a unit test, and it stays fast enough to be worth having.
/// </para>
/// </summary>
public class GraphQlSchemaContractTests
{
    private static async Task<string> ServedSchemaAsync()
    {
        var schema = await new ServiceCollection()
            .AddGraphQLServer()
            .AddMorphDbTypes()
            .BuildSchemaAsync();

        return schema.ToString();
    }

    [Fact]
    public async Task The_three_root_operation_types_are_named_the_way_GraphQL_names_them()
    {
        var sdl = await ServedSchemaAsync();

        // Query and Mutation are named by their own CLR types and have always read this way.
        // Subscription is named by the class that carries the resolvers, so it is the one that
        // moves if someone renames a class -- which is exactly what this pins.
        sdl.Should().Contain("type Query {");
        sdl.Should().Contain("type Mutation {");
        sdl.Should().Contain("type Subscription {");
        sdl.Should().Contain("subscription: Subscription");
    }

    [Fact]
    public async Task No_served_type_is_named_after_the_class_that_happens_to_implement_it()
    {
        var sdl = await ServedSchemaAsync();

        // A CLR name leaking into the schema is a naming decision made by accident. It has happened
        // once; naming the shape of the mistake is cheaper than rediscovering it.
        sdl.Should().NotContain("MorphDb", "internal class names are not part of the published schema");
        sdl.Should().NotContain("Dynamic", "nor are the ones they replaced");
        sdl.Should().NotContain("MutationResultOf", "nor are closed generics named after their CLR shape");
        sdl.Should().NotContain("IReadOnlyList", "and least of all a .NET interface name");
    }

    [Fact]
    public async Task The_operations_clients_call_are_all_present()
    {
        var sdl = await ServedSchemaAsync();

        // Reads and writes live on type extensions, whose CLR names never reach the schema -- so
        // renaming those classes is safe, and this is what says so.
        foreach (var field in new[] { "tables:", "table(name:", "records(", "record(table:", "aggregate(" })
        {
            sdl.Should().Contain(field);
        }

        foreach (var field in new[] { "createRecord(", "updateRecord(", "deleteRecord(", "upsertRecord(", "createRecords(" })
        {
            sdl.Should().Contain(field);
        }

        foreach (var field in new[] { "onRecordCreated(", "onRecordUpdated(", "onRecordDeleted(", "onRecordChanged(" })
        {
            sdl.Should().Contain(field);
        }
    }

    [Fact]
    public async Task Every_mutation_answers_with_a_named_envelope()
    {
        var sdl = await ServedSchemaAsync();

        // One envelope shape, three payloads. The names are stated at registration rather than
        // derived from the closed generic, so changing the CLR type that carries them changes
        // nothing a client sees.
        sdl.Should().Contain("type RecordMutationResult {");
        sdl.Should().Contain("type RecordListMutationResult {");
        sdl.Should().Contain("type BooleanMutationResult {");
    }
}
