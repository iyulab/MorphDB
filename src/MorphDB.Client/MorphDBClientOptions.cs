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
    /// Number of retry attempts for failed requests. Default is 3.
    /// </summary>
    public int RetryCount { get; set; } = 3;

    /// <summary>
    /// Delay between retry attempts. Default is 1 second.
    /// </summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Custom HTTP message handler for testing or proxy scenarios.
    /// </summary>
    public HttpMessageHandler? HttpMessageHandler { get; set; }
}
