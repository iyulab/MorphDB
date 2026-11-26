namespace MorphDB.Service.Services;

/// <summary>How much one read over HTTP or MCP returns.</summary>
internal static class ReadLimits
{
    /// <summary>The most rows one read of a table or a view returns.</summary>
    internal const int MaxRows = 1000;
}
