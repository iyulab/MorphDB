using HotChocolate.Execution.Configuration;
using HotChocolate.Types;
using MorphDB.Core.Models;

namespace MorphDB.Service.GraphQL;

/// <summary>
/// Builds dynamic GraphQL schema from MorphDB table metadata.
/// </summary>
public static class DynamicSchemaBuilder
{
    /// <summary>
    /// Adds dynamic MorphDB types to the GraphQL schema.
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

    /// <summary>
    /// Maps MorphDB data types to GraphQL scalar types.
    /// </summary>
    public static IOutputType MapToGraphQLType(MorphDataType dataType, bool isNullable)
    {
        IOutputType baseType = dataType switch
        {
            MorphDataType.Text => new StringType(),
            MorphDataType.Integer => new IntType(),
            MorphDataType.BigInteger => new LongType(),
            MorphDataType.Decimal => new DecimalType(),
            MorphDataType.Boolean => new BooleanType(),
            MorphDataType.DateTime => new DateTimeType(),
            MorphDataType.Date => new DateType(),
            MorphDataType.Time => new TimeSpanType(),
            MorphDataType.Uuid => new UuidType(),
            MorphDataType.Json => new AnyType(),
            MorphDataType.Attachment => new StringType(), // URLs or Base64 encoded
            _ => new StringType()
        };

        return isNullable ? baseType : new NonNullType(baseType);
    }

    /// <summary>
    /// Maps MorphDB data types to GraphQL input types.
    /// </summary>
    public static IInputType MapToGraphQLInputType(MorphDataType dataType, bool isNullable)
    {
        IInputType baseType = dataType switch
        {
            MorphDataType.Text => new StringType(),
            MorphDataType.Integer => new IntType(),
            MorphDataType.BigInteger => new LongType(),
            MorphDataType.Decimal => new DecimalType(),
            MorphDataType.Boolean => new BooleanType(),
            MorphDataType.DateTime => new DateTimeType(),
            MorphDataType.Date => new DateType(),
            MorphDataType.Time => new TimeSpanType(),
            MorphDataType.Uuid => new UuidType(),
            MorphDataType.Json => new AnyType(),
            MorphDataType.Attachment => new StringType(),
            _ => new StringType()
        };

        return isNullable ? baseType : new NonNullType(baseType);
    }
}
