namespace MorphDB.Service.GraphQL;

/// <summary>
/// Root GraphQL query type.
/// </summary>
public sealed class Query
{
    /// <summary>
    /// Health check query.
    /// </summary>
    public string Health() => "OK";
}
