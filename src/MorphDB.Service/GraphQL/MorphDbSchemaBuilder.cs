using HotChocolate.Execution.Configuration;

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
            // DataLoaders
            .AddDataLoader<TableByNameDataLoader>()
            .AddDataLoader<TableByIdDataLoader>()
            .AddDataLoader<RecordByIdDataLoader>()
            .AddDataLoader<RelatedRecordsDataLoader>()
            // In-memory subscriptions
            .AddInMemorySubscriptions();
    }
}
