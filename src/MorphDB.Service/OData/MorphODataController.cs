using System.Text.Json;
using System.Xml;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.OData.Edm.Csdl;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Models;
using MorphDB.Core.Pipeline;
using MorphDB.Npgsql.Repositories;
using MorphDB.Service.Filters;
using MorphDB.Service.Models.Api;
using MorphDB.Service.Services;

namespace MorphDB.Service.OData;

/// <summary>
/// Dynamic OData controller for MorphDB entities.
/// Handles all OData operations for dynamically created entity sets.
/// </summary>
[Route("odata")]
[ApiController]
[RequireProject]
[TypeFilter(typeof(ODataErrorFilter))]
public sealed partial class MorphODataController : ControllerBase
{
    private readonly IEdmModelProvider _modelProvider;
    private readonly ODataQueryHandler _queryHandler;
    private readonly IMorphDataService _dataService;
    private readonly IWritePipeline _writePipeline;
    private readonly IMetadataRepository _metadataRepository;
    private readonly IProjectContextAccessor _projectAccessor;
    private readonly IUnitOfWorkFactory _units;
    private readonly ILogger<MorphODataController> _logger;

    public MorphODataController(
        IEdmModelProvider modelProvider,
        ODataQueryHandler queryHandler,
        IMorphDataService dataService,
        IWritePipeline writePipeline,
        IMetadataRepository metadataRepository,
        IProjectContextAccessor projectAccessor,
        IUnitOfWorkFactory units,
        ILogger<MorphODataController> logger)
    {
        _modelProvider = modelProvider;
        _queryHandler = queryHandler;
        _dataService = dataService;
        _writePipeline = writePipeline;
        _metadataRepository = metadataRepository;
        _projectAccessor = projectAccessor;
        _units = units;
        _logger = logger;
    }

    /// <summary>
    /// Returns the OData $metadata document.
    /// </summary>
    [HttpGet("$metadata")]
    [Produces("application/xml")]
    public async Task<IActionResult> GetMetadata(CancellationToken cancellationToken)
    {
        var projectId = _projectAccessor.ProjectId;
        var model = await _modelProvider.GetModelAsync(projectId, cancellationToken);

        // Serialize EDM model to CSDL XML
        using var stream = new MemoryStream();
        using (var writer = XmlWriter.Create(stream, new XmlWriterSettings
        {
            Indent = true,
            Async = true
        }))
        {
            CsdlWriter.TryWriteCsdl(model, writer, CsdlTarget.OData, out _);
        }

        stream.Position = 0;
        return File(stream.ToArray(), "application/xml");
    }

    /// <summary>
    /// Queries an entity set with OData query options.
    /// GET /odata/{entitySet}?$filter=...&$orderby=...&$top=...&$skip=...&$select=...&$count=true
    /// </summary>
    [HttpGet("{entitySet}")]
    public async Task<IActionResult> GetEntitySet(
        string entitySet,
        [FromQuery(Name = "$filter")] string? filter,
        [FromQuery(Name = "$orderby")] string? orderBy,
        [FromQuery(Name = "$top")] int? top,
        [FromQuery(Name = "$skip")] int? skip,
        [FromQuery(Name = "$select")] string? select,
        [FromQuery(Name = "$count")] bool count,
        CancellationToken cancellationToken)
    {
        // The request's own options are read here, where they are the request's; a batch item states its own.
        if (ODataSystemOptions.Problem(Request.Query, entity: false) is { } problem)
        {
            return OptionsRefused(problem);
        }

        return await ReadEntitySetAsync(entitySet, filter, orderBy, top, skip, select, count, cancellationToken);
    }

    private async Task<IActionResult> ReadEntitySetAsync(
        string entitySet, string? filter, string? orderBy, int? top, int? skip, string? select, bool count, CancellationToken cancellationToken)
    {
        var projectId = _projectAccessor.ProjectId;
        var modelResult = await _modelProvider.GetModelWithMappingAsync(projectId, cancellationToken);

        var options = new ODataQueryOptions
        {
            Filter = filter,
            OrderBy = orderBy,
            Top = top,
            Skip = skip ?? 0,
            Select = select,
            Count = count
        };

        var result = await _queryHandler.ExecuteQueryAsync(
            projectId,
            entitySet,
            modelResult.Model,
            options,
            modelResult.EntitySetToTableNameMap,
            cancellationToken);

        // Format as OData response
        var response = new ODataResponse
        {
            Context = $"{Request.Scheme}://{Request.Host}/odata/$metadata#{entitySet}",
            Value = result.Records
        };

        if (count && result.TotalCount.HasValue)
        {
            response.Count = result.TotalCount.Value;
        }

        if (result.NextSkip is { } nextSkip)
        {
            // The same request, the next page: every option kept, $skip moved on.
            var next = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (var (name, value) in new (string Name, string? Value)[]
                     {
                         ("$filter", filter), ("$orderby", orderBy), ("$select", select), ("$count", count ? "true" : null),
                     })
            {
                if (value is not null)
                {
                    next[name] = value;
                }
            }

            next["$skip"] = nextSkip.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (result.NextTop is { } nextTop)
            {
                next["$top"] = nextTop.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            response.NextLink = Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString(
                $"{Request.Scheme}://{Request.Host}/odata/{entitySet}", next);
        }

        return Ok(response);
    }

    /// <summary>
    /// Gets a single entity by key.
    /// GET /odata/{entitySet}({key})
    /// </summary>
    [HttpGet("{entitySet}({key})")]
    public async Task<IActionResult> GetEntity(
        string entitySet,
        Guid key,
        [FromQuery(Name = "$select")] string? select,
        CancellationToken cancellationToken)
    {
        if (ODataSystemOptions.Problem(Request.Query, entity: true) is { } problem)
        {
            return OptionsRefused(problem);
        }

        return await ReadEntityAsync(entitySet, key, select, cancellationToken);
    }

    private async Task<IActionResult> ReadEntityAsync(string entitySet, Guid key, string? select, CancellationToken cancellationToken)
    {
        var projectId = _projectAccessor.ProjectId;
        var modelResult = await _modelProvider.GetModelWithMappingAsync(projectId, cancellationToken);

        var options = new ODataQueryOptions
        {
            Select = select
        };

        var entity = await _queryHandler.GetByIdAsync(
            projectId,
            entitySet,
            key,
            options,
            modelResult.EntitySetToTableNameMap,
            cancellationToken);

        if (entity == null)
        {
            return EntityNotFound(entitySet, key);
        }

        var response = SingleEntity(entitySet, entity);

        return Ok(response);
    }

    /// <summary>
    /// Creates a new entity.
    /// POST /odata/{entitySet}
    /// </summary>
    [HttpPost("{entitySet}")]
    public async Task<IActionResult> CreateEntity(
        string entitySet,
        [FromBody] JsonElement body,
        CancellationToken cancellationToken)
    {
        try
        {
            var projectId = _projectAccessor.ProjectId;
            var modelResult = await _modelProvider.GetModelWithMappingAsync(projectId, cancellationToken);
            var tableName = ResolveTableName(entitySet, modelResult.EntitySetToTableNameMap);

            var tableMetadata = await _metadataRepository.GetTableByNameAsync(
                projectId, tableName, includeColumns: true, cancellationToken);
            if (tableMetadata is null)
            {
                return TableNotFound(tableName);
            }

            var data = JsonElementToDictionary(body);

            var writeResult = await _writePipeline.InsertAsync(
                projectId, tableMetadata, data, cancellationToken: cancellationToken);

            if (!writeResult.Success)
            {
                return WriteRefused(writeResult);
            }

            var result = writeResult.Data ?? data;
            var response = SingleEntity(entitySet, result);

            var id = SystemColumns.RequireRecordId(result);
            return Created($"/odata/{entitySet}({id})", response);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ODataError.Of("INVALID_ARGUMENT", ex.Message));
        }
    }

    /// <summary>
    /// Updates an entity (partial update).
    /// PATCH /odata/{entitySet}({key})
    /// </summary>
    [HttpPatch("{entitySet}({key})")]
    public async Task<IActionResult> UpdateEntity(
        string entitySet,
        Guid key,
        [FromBody] JsonElement body,
        CancellationToken cancellationToken)
    {
        try
        {
            var projectId = _projectAccessor.ProjectId;
            var modelResult = await _modelProvider.GetModelWithMappingAsync(projectId, cancellationToken);
            var tableName = ResolveTableName(entitySet, modelResult.EntitySetToTableNameMap);

            var tableMetadata = await _metadataRepository.GetTableByNameAsync(
                projectId, tableName, includeColumns: true, cancellationToken);
            if (tableMetadata is null)
            {
                return TableNotFound(tableName);
            }

            var existing = await _dataService.GetByIdAsync(projectId, tableName, key, cancellationToken);
            var data = JsonElementToDictionary(body);

            var writeResult = await _writePipeline.UpdateAsync(
                projectId, tableMetadata, key, data, existing, cancellationToken: cancellationToken);

            if (ErrorHandling.WriteFailure.IsNotFound(writeResult))
            {
                return EntityNotFound(entitySet, key);
            }

            if (!writeResult.Success)
            {
                return WriteRefused(writeResult);
            }

            var response = SingleEntity(entitySet, writeResult.Data ?? data);

            return Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ODataError.Of("INVALID_ARGUMENT", ex.Message));
        }
    }

    /// <summary>
    /// One entity as OData JSON writes it (OData JSON Format 4.01 §10): the context annotation, then
    /// the entity's own properties at the top level of the object — not wrapped in <c>value</c>, which
    /// is the property a collection's members travel in.
    /// </summary>
    private Dictionary<string, object?> SingleEntity(string entitySet, IDictionary<string, object?> entity)
    {
        var body = new Dictionary<string, object?>(entity.Count + 1, StringComparer.Ordinal)
        {
            ["@odata.context"] = $"{Request.Scheme}://{Request.Host}/odata/$metadata#{entitySet}/$entity",
        };
        foreach (var (name, value) in entity)
        {
            body[name] = value;
        }

        return body;
    }

    /// <summary>
    /// Deletes an entity.
    /// DELETE /odata/{entitySet}({key})
    /// </summary>
    [HttpDelete("{entitySet}({key})")]
    public async Task<IActionResult> DeleteEntity(
        string entitySet,
        Guid key,
        CancellationToken cancellationToken)
    {
        try
        {
            var projectId = _projectAccessor.ProjectId;
            var modelResult = await _modelProvider.GetModelWithMappingAsync(projectId, cancellationToken);
            var tableName = ResolveTableName(entitySet, modelResult.EntitySetToTableNameMap);

            var tableMetadata = await _metadataRepository.GetTableByNameAsync(
                projectId, tableName, includeColumns: true, cancellationToken);
            if (tableMetadata is null)
            {
                return TableNotFound(tableName);
            }

            var writeResult = await _writePipeline.DeleteAsync(
                projectId, tableMetadata, key, cancellationToken: cancellationToken);

            if (ErrorHandling.WriteFailure.IsNotFound(writeResult))
            {
                return EntityNotFound(entitySet, key);
            }

            if (!writeResult.Success)
            {
                return WriteRefused(writeResult);
            }

            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ODataError.Of("INVALID_ARGUMENT", ex.Message));
        }
    }

    /// <summary>
    /// Executes a JSON batch (OData JSON Format 4.01 §19): each request names a method and a URL
    /// relative to the service root (<c>Customers</c>, <c>Customers(…)</c>, <c>Customers?$filter=…</c>)
    /// and is answered exactly as the same request on its own would be — the same route does the work.
    /// Requests that share an <c>atomicityGroup</c> are applied all or none: they run in one unit of work,
    /// where each sees what the earlier ones wrote, and when one fails nothing of the group is kept — the
    /// failing request keeps its own answer, the others are answered <c>424</c>.
    /// POST /odata/$batch
    /// </summary>
    [HttpPost("$batch")]
    public async Task<IActionResult> ExecuteBatch(
        [FromBody] ODataBatchRequest batchRequest,
        CancellationToken cancellationToken)
    {
        if (ODataBatchGroups.Problem(batchRequest.Requests) is { } problem)
        {
            return BadRequest(ODataError.Of("INVALID_ARGUMENT", problem));
        }

        var responses = new List<ODataBatchResponseItem>(batchRequest.Requests.Count);
        var dependencies = new ODataBatchGroups.Dependencies(batchRequest.Requests);
        var references = new ODataBatchGroups.References();
        foreach (var segment in ODataBatchGroups.Segments(batchRequest.Requests))
        {
            if (segment.Group is null)
            {
                var request = segment.Requests[0];
                var response = dependencies.Refusal(request) ?? await ExecuteBatchItemAsync(request, references, cancellationToken);
                dependencies.Record(request.Id, response.Status);
                references.Record(request, response);
                responses.Add(response);
                continue;
            }

            var answers = await ExecuteAtomicityGroupAsync(segment.Group, segment.Requests, dependencies, references, cancellationToken);
            dependencies.Record(segment.Group, answers.All(a => a.Status < StatusCodes.Status400BadRequest) ? StatusCodes.Status200OK : StatusCodes.Status424FailedDependency);
            responses.AddRange(answers);
        }

        return Ok(new ODataBatchResponse { Responses = responses });
    }

    /// <summary>
    /// One atomicity group, in one unit of work. The requests run in order until one fails; then the unit
    /// ends without a commit, the failing request keeps its answer, and every other request of the group —
    /// run or not — is answered <c>424</c>, since none of it was applied.
    /// </summary>
    private async Task<List<ODataBatchResponseItem>> ExecuteAtomicityGroupAsync(
        string group,
        IReadOnlyList<ODataBatchRequestItem> requests,
        ODataBatchGroups.Dependencies dependencies,
        ODataBatchGroups.References references,
        CancellationToken cancellationToken)
    {
        var answers = new List<ODataBatchResponseItem>(requests.Count);
        string? failedRequest = null;
        await using (var unit = _units.Begin())
        {
            foreach (var request in requests)
            {
                var response = dependencies.Refusal(request) ?? await ExecuteBatchItemAsync(request, references, cancellationToken);
                dependencies.Record(request.Id, response.Status);
                references.Record(request, response);
                answers.Add(response);
                if (response.Status >= StatusCodes.Status400BadRequest)
                {
                    failedRequest = request.Id;
                    break;
                }
            }

            if (failedRequest is null)
            {
                try
                {
                    await unit.CommitAsync(cancellationToken);
                    return answers;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    var (status, envelope) = ErrorHandling.ErrorClassification.Envelope(ex);
                    LogBatchItemError(_logger, group, ex);
                    return [.. requests.Select(r => Item(r.Id, status, ODataError.From(envelope)))];
                }
            }
        }

        return
        [
            .. requests.Select(r => r.Id == failedRequest
                ? answers.Single(a => a.Id == r.Id)
                : Item(r.Id, StatusCodes.Status424FailedDependency, ODataError.Of("FAILED_DEPENDENCY",
                    $"Request '{r.Id}' was not applied: request '{failedRequest}' of atomicity group '{group}' failed, so none of the group was."))),
        ];
    }

    private async Task<ODataBatchResponseItem> ExecuteBatchItemAsync(ODataBatchRequestItem request, ODataBatchGroups.References references, CancellationToken cancellationToken)
    {
        try
        {
            if (references.Resolve(request, out var unresolved) is not { } url)
            {
                return Item(request.Id, StatusCodes.Status400BadRequest, ODataError.Of("INVALID_ARGUMENT", unresolved!));
            }

            if (!ODataBatchUrl.TryParse(url, out var target, out var problem))
            {
                return Item(request.Id, StatusCodes.Status400BadRequest, ODataError.Of("INVALID_ARGUMENT", $"Request '{request.Id}': {problem}"));
            }

            // A read is held to the options the same request on its own is.
            if (request.Method.Equals("GET", StringComparison.OrdinalIgnoreCase)
                && ODataSystemOptions.Problem(target.Options, entity: target.Key is not null) is { } optionsProblem)
            {
                return Item(request.Id, StatusCodes.Status400BadRequest, ODataError.Of("INVALID_ARGUMENT", $"Request '{request.Id}': {optionsProblem}"));
            }

            var body = request.Body ?? JsonSerializer.SerializeToElement(new Dictionary<string, object?>());
            var result = (request.Method.ToUpperInvariant(), target.Key) switch
            {
                ("GET", null) => await ReadEntitySetAsync(target.EntitySet, target.Query("$filter"), target.Query("$orderby"),
                    target.IntQuery("$top"), target.IntQuery("$skip"), target.Query("$select"),
                    string.Equals(target.Query("$count"), "true", StringComparison.OrdinalIgnoreCase), cancellationToken),
                ("GET", { } key) => await ReadEntityAsync(target.EntitySet, key, target.Query("$select"), cancellationToken),
                ("POST", null) => await CreateEntity(target.EntitySet, body, cancellationToken),
                ("PATCH", { } key) => await UpdateEntity(target.EntitySet, key, body, cancellationToken),
                ("DELETE", { } key) => await DeleteEntity(target.EntitySet, key, cancellationToken),
                _ => StatusCode(StatusCodes.Status405MethodNotAllowed, ODataError.Of("METHOD_NOT_ALLOWED",
                    $"Request '{request.Id}': {request.Method.ToUpperInvariant()} is not served on {(target.Key is null ? "an entity set" : "an entity")}. " +
                    "An entity set takes GET and POST; an entity GET, PATCH and DELETE.")),
            };

            return FromResult(request.Id, result);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // One request's failure is that request's answer, classified as it would be on its own: a
            // caller's mistake keeps its status and code, a service defect stays unexplained.
            var (status, envelope) = ErrorHandling.ErrorClassification.Envelope(ex);
            if (status >= StatusCodes.Status500InternalServerError)
            {
                LogBatchItemError(_logger, request.Id, ex);
            }

            return Item(request.Id, status, ODataError.From(envelope));
        }
    }

    private static ODataBatchResponseItem FromResult(string id, IActionResult result) => result switch
    {
        CreatedResult created => Item(id, created.StatusCode ?? StatusCodes.Status201Created, created.Value,
            created.Location is { } location ? new Dictionary<string, string> { ["location"] = location } : null),
        ObjectResult o => Item(id, o.StatusCode ?? StatusCodes.Status200OK, o.Value is ErrorResponse e ? ODataError.From(e) : o.Value),
        JsonResult j => Item(id, j.StatusCode ?? StatusCodes.Status200OK, j.Value is ErrorResponse e ? ODataError.From(e) : j.Value),
        IStatusCodeActionResult s => Item(id, s.StatusCode ?? StatusCodes.Status200OK, null),
        _ => throw new InvalidOperationException($"A batch request was answered with an unexpected result ({result.GetType().Name})."),
    };

    private static ODataBatchResponseItem Item(string id, int status, object? body, IReadOnlyDictionary<string, string>? headers = null) =>
        new() { Id = id, Status = status, Body = body, Headers = headers };

    private BadRequestObjectResult OptionsRefused(string problem) => BadRequest(ODataError.Of("INVALID_ARGUMENT", problem));

    private NotFoundObjectResult EntityNotFound(string entitySet, Guid key) => NotFound(new ODataError(EntityMissing(entitySet, key)));

    private NotFoundObjectResult TableNotFound(string tableName) => NotFound(new ODataError(TableMissing(tableName)));

    private BadRequestObjectResult WriteRefused(WriteResult result) => BadRequest(new ODataError(Refusal(result)));

    private static ODataErrorDetail EntityMissing(string entitySet, Guid key) =>
        new("RECORD_NOT_FOUND", $"Entity with key '{key}' not found in '{entitySet}'.");

    private static ODataErrorDetail TableMissing(string tableName) =>
        new("TABLE_NOT_FOUND", $"Table '{tableName}' not found.");

    /// <summary>A refused write: the REST code for it, each cause a <c>details</c> entry.</summary>
    private static ODataErrorDetail Refusal(WriteResult result) =>
        new(ErrorHandling.WriteFailure.CodeFor(result), string.Join("; ", result.Errors.Select(e => e.Message)))
        {
            Details = [.. result.Errors.Select(e => new ODataErrorDetail(e.Code, e.Message) { Target = e.Field })],
        };

    [LoggerMessage(Level = LogLevel.Error, Message = "Error executing batch item {RequestId}")]
    private static partial void LogBatchItemError(ILogger logger, string requestId, Exception exception);

    private static Dictionary<string, object?> JsonElementToDictionary(JsonElement element)
    {
        var dict = new Dictionary<string, object?>();

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                dict[property.Name] = JsonElementToObject(property.Value);
            }
        }

        return dict;
    }

    private static object? JsonElementToObject(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number when element.TryGetInt32(out var i) => i,
            JsonValueKind.Number when element.TryGetInt64(out var l) => l,
            JsonValueKind.Number => element.GetDecimal(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Array => element.EnumerateArray().Select(JsonElementToObject).ToList(),
            JsonValueKind.Object => JsonElementToDictionary(element),
            _ => element.ToString()
        };
    }

    /// <summary>
    /// Resolves the table name from the entity set name using the mapping.
    /// Falls back to simple lowercase conversion if mapping not found.
    /// </summary>
    private static string ResolveTableName(string entitySetName, IReadOnlyDictionary<string, string> mapping)
    {
        if (mapping.TryGetValue(entitySetName, out var tableName))
        {
            return tableName;
        }

        // Fallback: convert PascalCase to snake_case (may be lossy for complex names)
        var result = new System.Text.StringBuilder();
        for (int i = 0; i < entitySetName.Length; i++)
        {
            var c = entitySetName[i];
            if (i > 0 && char.IsUpper(c))
            {
                result.Append('_');
            }
            result.Append(char.ToLowerInvariant(c));
        }
        return result.ToString();
    }
}

/// <summary>
/// OData collection response format.
/// </summary>
internal sealed class ODataResponse
{
    [System.Text.Json.Serialization.JsonPropertyName("@odata.context")]
    public string? Context { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("@odata.count")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public long? Count { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("value")]
    public IReadOnlyList<IDictionary<string, object?>>? Value { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("@odata.nextLink")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? NextLink { get; set; }
}

/// <summary>
/// A JSON batch request (OData JSON Format 4.01 §19).
/// </summary>
public sealed class ODataBatchRequest
{
    [System.Text.Json.Serialization.JsonPropertyName("requests")]
    public IReadOnlyList<ODataBatchRequestItem> Requests { get; set; } = [];
}

/// <summary>
/// One request of a JSON batch: a method and a URL relative to the service root, the body a POST or
/// PATCH carries, and the ids of earlier requests it may run only after.
/// </summary>
public sealed class ODataBatchRequestItem
{
    [System.Text.Json.Serialization.JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("method")]
    public string Method { get; set; } = "GET";

    [System.Text.Json.Serialization.JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("headers")]
    public IReadOnlyDictionary<string, string>? Headers { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("body")]
    public System.Text.Json.JsonElement? Body { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("dependsOn")]
    public IReadOnlyList<string>? DependsOn { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("atomicityGroup")]
    public string? AtomicityGroup { get; set; }
}

/// <summary>
/// A JSON batch response: one response per request, in request order.
/// </summary>
public sealed class ODataBatchResponse
{
    [System.Text.Json.Serialization.JsonPropertyName("responses")]
    public IReadOnlyList<ODataBatchResponseItem> Responses { get; set; } = [];
}

/// <summary>
/// One response of a JSON batch: the status and body the same request would have answered on its own —
/// an error is an OData error object in <c>body</c>.
/// </summary>
public sealed class ODataBatchResponseItem
{
    [System.Text.Json.Serialization.JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("status")]
    public int Status { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("headers")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, string>? Headers { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("body")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public object? Body { get; set; }
}
