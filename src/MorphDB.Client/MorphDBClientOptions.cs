namespace MorphDB.Client;

/// <summary>
/// Configuration options for the MorphDB client.
/// </summary>
public sealed class MorphDBClientOptions
{
    /// <summary>
    /// The project every request from this client is scoped to. A project is a schema namespace,
    /// not a trust boundary -- see the security note in the README.
    /// </summary>
    public Guid ProjectId { get; set; }

    /// <summary>
    /// Request timeout. Default is 30 seconds.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The secret every request presents, as <c>Authorization: Bearer &lt;secret&gt;</c> — the REST calls
    /// and the real-time connection alike. Null sends none, which only a service started without a
    /// master secret accepts. Managing secrets (<see cref="MorphDBClient.Security"/>) needs the master
    /// secret itself.
    /// </summary>
    public string? Secret { get; set; }

    /// <summary>
    /// Custom HTTP message handler for testing or proxy scenarios.
    /// </summary>
    public HttpMessageHandler? HttpMessageHandler { get; set; }
}
