using HotChocolate.Execution.Configuration;
using HotChocolate.Types;

namespace MorphDB.Service.GraphQL;

/// <summary>
/// Registers the GraphQL schema MorphDB serves. "Schema" here is the GraphQL one — the types a
/// client can query — not a table's.
/// <para>
/// The schema's shape is fixed and comes from CLR types; it does not describe any particular
/// table. Tables and rows are served as <em>data</em> by resolvers that read metadata per request,
/// which is why creating a table changes no GraphQL type and why building this schema needs no
/// database.
/// </para>
/// </summary>
public static class MorphDbSchemaBuilder
{
    /// <summary>
    /// Adds MorphDB's root types and their extensions to the GraphQL schema.
    /// </summary>
    public static IRequestExecutorBuilder AddMorphDbTypes(this IRequestExecutorBuilder builder)
    {
        return builder
            // Root types
            .AddQueryType<Query>()
            .AddMutationType<Mutation>()
            .AddSubscriptionType<MorphDbSubscription>()
            // Type extensions (extend root Query and Mutation)
            .AddTypeExtension<MorphDbQuery>()
            .AddTypeExtension<MorphDbMutation>()
            // Every mutation answers with the same envelope, and left to itself the schema names
            // each closed generic after its CLR shape -- MutationResultOfRecordNode, and worse,
            // MutationResultOfIReadOnlyListOfRecordNode, which puts a .NET interface name in a
            // published schema. Naming them here keeps the wire independent of the CLR types that
            // happen to implement it, the same way the subscription root does.
            .AddType(new ObjectType<MutationResult<RecordNode>>(d => d.Name("RecordMutationResult")))
            .AddType(new ObjectType<MutationResult<IReadOnlyList<RecordNode>>>(d => d.Name("RecordListMutationResult")))
            .AddType(new ObjectType<MutationResult<bool>>(d => d.Name("BooleanMutationResult")))
            // DataLoaders
            .AddDataLoader<TableByNameDataLoader>()
            .AddDataLoader<TableByIdDataLoader>()
            .AddDataLoader<RecordByIdDataLoader>()
            .AddDataLoader<RelatedRecordsDataLoader>()
            // In-memory subscriptions
            .AddInMemorySubscriptions();
    }
}
