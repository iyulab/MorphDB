using HotChocolate.Execution.Configuration;

namespace MorphDB.Service.GraphQL;

/// <summary>
/// Registers the GraphQL schema MorphDB serves.
/// <para>
/// The schema's shape is fixed and comes from CLR types; it does not describe any particular
/// table. Tables and rows are served as <em>data</em> by resolvers that read metadata per request,
/// which is why creating a table changes no GraphQL type and why building this schema needs no
/// database.
/// </para>
/// </summary>
public static class DynamicSchemaBuilder
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
            .AddSubscriptionType<DynamicSubscription>()
            // Type extensions (extend root Query and Mutation)
            .AddTypeExtension<DynamicQuery>()
            .AddTypeExtension<DynamicMutation>()
            // DataLoaders
            .AddDataLoader<TableByNameDataLoader>()
            .AddDataLoader<TableByIdDataLoader>()
            .AddDataLoader<RecordByIdDataLoader>()
            .AddDataLoader<RelatedRecordsDataLoader>()
            // In-memory subscriptions
            .AddInMemorySubscriptions();
    }
}
