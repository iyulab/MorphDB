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
    private Guid _projectId;

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

        Projects = new ProjectClient(_httpClient);
        Schema = new SchemaClient(_httpClient);
        Data = new DataClient(_httpClient);
        Batch = new BatchClient(_httpClient);
        Webhooks = new WebhookClient(_httpClient);
        Bulk = new BulkClient(_httpClient);
        Transactions = new TransactionClient(_httpClient);
        Views = new ViewClient(_httpClient);
        Security = new SecurityClient(_httpClient);
        Hierarchy = new HierarchyClient(_httpClient);
        Realtime = new RealtimeClient(_baseUrl, _options, () => _projectId);
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

        Projects = new ProjectClient(_httpClient);
        Schema = new SchemaClient(_httpClient);
        Data = new DataClient(_httpClient);
        Batch = new BatchClient(_httpClient);
        Webhooks = new WebhookClient(_httpClient);
        Bulk = new BulkClient(_httpClient);
        Transactions = new TransactionClient(_httpClient);
        Views = new ViewClient(_httpClient);
        Security = new SecurityClient(_httpClient);
        Hierarchy = new HierarchyClient(_httpClient);
        Realtime = new RealtimeClient(_baseUrl, _options, () => _projectId);
    }

    /// <summary>
    /// Project lifecycle operations.
    /// <para>
    /// The one client here that is not scoped to a project: these calls name the project in the
    /// route and work with or without <see cref="MorphDBClientOptions.ProjectId"/> being set —
    /// creating the first project necessarily happens before there is an id to send.
    /// </para>
    /// </summary>
    public ProjectClient Projects { get; }

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
    /// Walks over tables whose rows point at a parent row.
    /// </summary>
    public HierarchyClient Hierarchy { get; }

    /// <summary>
    /// Row-level security policies and issued secrets.
    /// </summary>
    public SecurityClient Security { get; }

    /// <summary>
    /// Real-time subscription operations.
    /// </summary>
    public RealtimeClient Realtime { get; }

    /// <summary>
    /// Sets the project every request is scoped to from now on. The real-time connection sends it
    /// when it connects, so a connection already open keeps the project it opened with.
    /// </summary>
    public void SetProjectId(Guid projectId)
    {
        _projectId = projectId;
        _httpClient.DefaultRequestHeaders.Remove("X-Project-Id");
        _httpClient.DefaultRequestHeaders.Add("X-Project-Id", projectId.ToString());
    }

    private void ConfigureDefaultHeaders()
    {
        _projectId = _options.ProjectId;
        if (_options.ProjectId != Guid.Empty)
        {
            _httpClient.DefaultRequestHeaders.Add("X-Project-Id", _options.ProjectId.ToString());
        }

        if (_options.Secret is { } secret)
        {
            _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", secret);
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
