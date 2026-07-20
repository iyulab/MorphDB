namespace MorphDB.Client;

/// <summary>
/// Main client for interacting with MorphDB.
/// </summary>
public sealed class MorphDBClient : IAsyncDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _disposeHttpClient;
    private readonly MorphDBClientOptions _options;
    private readonly string _baseUrl;

    /// <summary>
    /// Creates a new MorphDB client.
    /// </summary>
    /// <param name="baseUrl">Base URL of the MorphDB server.</param>
    /// <param name="options">Client configuration options.</param>
    public MorphDBClient(string baseUrl, MorphDBClientOptions? options = null)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _options = options ?? new MorphDBClientOptions();

        HttpMessageHandler handler = _options.HttpMessageHandler ?? new HttpClientHandler();
        _httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri(_baseUrl),
            Timeout = _options.Timeout
        };
        _disposeHttpClient = true;

        ConfigureDefaultHeaders();

        Schema = new SchemaClient(_httpClient);
        Data = new DataClient(_httpClient);
        Batch = new BatchClient(_httpClient);
        Webhooks = new WebhookClient(_httpClient);
        Bulk = new BulkClient(_httpClient);
        Transactions = new TransactionClient(_httpClient);
        Views = new ViewClient(_httpClient);
        Realtime = new RealtimeClient(_baseUrl, _options);
    }

    /// <summary>
    /// Creates a new MorphDB client with an existing HttpClient.
    /// </summary>
    /// <param name="httpClient">Existing HttpClient instance.</param>
    /// <param name="options">Client configuration options.</param>
    public MorphDBClient(HttpClient httpClient, MorphDBClientOptions? options = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _baseUrl = httpClient.BaseAddress?.ToString().TrimEnd('/') ?? throw new ArgumentException("HttpClient must have a BaseAddress set");
        _options = options ?? new MorphDBClientOptions();
        _disposeHttpClient = false;

        ConfigureDefaultHeaders();

        Schema = new SchemaClient(_httpClient);
        Data = new DataClient(_httpClient);
        Batch = new BatchClient(_httpClient);
        Webhooks = new WebhookClient(_httpClient);
        Bulk = new BulkClient(_httpClient);
        Transactions = new TransactionClient(_httpClient);
        Views = new ViewClient(_httpClient);
        Realtime = new RealtimeClient(_baseUrl, _options);
    }

    /// <summary>
    /// Schema management operations.
    /// </summary>
    public SchemaClient Schema { get; }

    /// <summary>
    /// Data operations.
    /// </summary>
    public DataClient Data { get; }

    /// <summary>
    /// Batch data operations (many writes in one request).
    /// </summary>
    public BatchClient Batch { get; }

    /// <summary>
    /// Webhook operations.
    /// </summary>
    public WebhookClient Webhooks { get; }

    /// <summary>
    /// Bulk import/export operations.
    /// </summary>
    public BulkClient Bulk { get; }

    /// <summary>
    /// Transaction operations (atomic batch, finalize).
    /// </summary>
    public TransactionClient Transactions { get; }

    /// <summary>
    /// View operations (create, query, refresh).
    /// </summary>
    public ViewClient Views { get; }

    /// <summary>
    /// Real-time subscription operations.
    /// </summary>
    public RealtimeClient Realtime { get; }

    /// <summary>
    /// Sets the project ID for all requests.
    /// </summary>
    public void SetProjectId(Guid projectId)
    {
        _httpClient.DefaultRequestHeaders.Remove("X-Project-Id");
        _httpClient.DefaultRequestHeaders.Add("X-Project-Id", projectId.ToString());
    }

    /// <summary>
    /// Sets the API key for authentication.
    /// </summary>
    public void SetApiKey(string apiKey)
    {
        _httpClient.DefaultRequestHeaders.Remove("X-API-Key");
        _httpClient.DefaultRequestHeaders.Add("X-API-Key", apiKey);
    }

    /// <summary>
    /// Sets the JWT token for authentication.
    /// </summary>
    public void SetJwtToken(string token)
    {
        _httpClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
    }

    private void ConfigureDefaultHeaders()
    {
        if (_options.ProjectId != Guid.Empty)
        {
            _httpClient.DefaultRequestHeaders.Add("X-Project-Id", _options.ProjectId.ToString());
        }

        if (!string.IsNullOrEmpty(_options.ApiKey))
        {
            _httpClient.DefaultRequestHeaders.Add("X-API-Key", _options.ApiKey);
        }

        if (!string.IsNullOrEmpty(_options.JwtToken))
        {
            _httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _options.JwtToken);
        }

        _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await Realtime.DisposeAsync();

        if (_disposeHttpClient)
        {
            _httpClient.Dispose();
        }
    }
}
