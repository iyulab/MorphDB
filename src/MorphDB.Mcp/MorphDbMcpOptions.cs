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

    /// <summary>Gets or sets the most rows one <c>query_records</c> call returns. Default 100.</summary>
    public int MaxPageSize { get; set; } = 100;
}
