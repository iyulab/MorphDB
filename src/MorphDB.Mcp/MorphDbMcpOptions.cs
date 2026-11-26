namespace MorphDB.Mcp;

/// <summary>Bound from <c>MorphDB:Mcp</c> (<c>MorphDB__Mcp__Enabled</c>, …).</summary>
public sealed class MorphDbMcpOptions
{
    /// <summary>The configuration section this binds from.</summary>
    public const string SectionName = "MorphDB:Mcp";

    /// <summary>Gets or sets whether the door is open. Default <see langword="false"/>.</summary>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets the path the door answers on. Default <c>/mcp</c>.</summary>
    public string Path { get; set; } = "/mcp";

    /// <summary>
    /// Gets or sets the most rows one <c>query_records</c> or <c>read_view</c> call returns, and the most groups one
    /// <c>aggregate</c> call returns. Default 100. <c>query_records</c> and <c>read_view</c> read by the REST reads'
    /// rules, so a page of rows is at most 1000 whatever this is set to.
    /// </summary>
    public int MaxPageSize { get; set; } = 100;
}
