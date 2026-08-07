using System.Text.Json;
using System.Xml;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OData.Edm.Csdl;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Pipeline;
using MorphDB.Npgsql.Repositories;
using MorphDB.Service.Services;

namespace MorphDB.Service.OData;

/// <summary>
/// Dynamic OData controller for MorphDB entities.
/// Handles all OData operations for dynamically created entity sets.
/// </summary>
[Route("odata")]
[ApiController]
public sealed partial class MorphODataController : ControllerBase
{
    private readonly IEdmModelProvider _modelProvider;
    private readonly ODataQueryHandler _queryHandler;
    private readonly IMorphDataService _dataService;
    private readonly IWritePipeline _writePipeline;
    private readonly IMetadataRepository _metadataRepository;
    private readonly IProjectContextAccessor _projectAccessor;
    private readonly ILogger<MorphODataController> _logger;

    public MorphODataController(
        IEdmModelProvider modelProvider,
        ODataQueryHandler queryHandler,
        IMorphDataService dataService,
        IWritePipeline writePipeline,
        IMetadataRepository metadataRepository,
        IProjectContextAccessor projectAccessor,
        ILogger<MorphODataController> logger)
    {
        _modelProvider = modelProvider;
        _queryHandler = queryHandler;
        _dataService = dataService;
        _writePipeline = writePipeline;
        _metadataRepository = metadataRepository;
        _projectAccessor = projectAccessor;
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
        var projectId = _projectAccessor.ProjectId;
        var modelResult = await _modelProvider.GetModelWithMappingAsync(projectId, cancellationToken);

        var options = new ODataQueryOptions
        {
            Filter = filter,
            OrderBy = orderBy,
            Top = top ?? 0,
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
            return NotFound(new { error = $"Entity with key '{key}' not found in '{entitySet}'." });
        }

        var response = new ODataSingleResponse
        {
            Context = $"{Request.Scheme}://{Request.Host}/odata/$metadata#{entitySet}/$entity",
            Value = entity
        };

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
                return NotFound(new { error = $"Table '{tableName}' not found." });
            }

            var data = JsonElementToDictionary(body);

            var writeResult = await _writePipeline.InsertAsync(
                projectId, tableMetadata, data, cancellationToken: cancellationToken);

            if (!writeResult.Success)
            {
                return BadRequest(new { error = string.Join("; ", writeResult.Errors.Select(e => e.Message)) });
            }

            var result = writeResult.Data ?? data;
            var response = new ODataSingleResponse
            {
                Context = $"{Request.Scheme}://{Request.Host}/odata/$metadata#{entitySet}/$entity",
                Value = result
            };

            var id = result.TryGetValue("_id", out var idValue) && idValue is Guid guidId ? guidId : Guid.Empty;
            return Created($"/odata/{entitySet}({id})", response);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
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
                return NotFound(new { error = $"Table '{tableName}' not found." });
            }

            var existing = await _dataService.GetByIdAsync(projectId, tableName, key, cancellationToken);
            var data = JsonElementToDictionary(body);

            var writeResult = await _writePipeline.UpdateAsync(
                projectId, tableMetadata, key, data, existing, cancellationToken: cancellationToken);

            if (!writeResult.Success)
            {
                return BadRequest(new { error = string.Join("; ", writeResult.Errors.Select(e => e.Message)) });
            }

            var response = new ODataSingleResponse
            {
                Context = $"{Request.Scheme}://{Request.Host}/odata/$metadata#{entitySet}/$entity",
                Value = writeResult.Data ?? data
            };

            return Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
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
                return NotFound(new { error = $"Table '{tableName}' not found." });
            }

            var writeResult = await _writePipeline.DeleteAsync(
                projectId, tableMetadata, key, cancellationToken: cancellationToken);

            if (!writeResult.Success)
            {
                return NotFound(new { error = $"Entity with key '{key}' not found in '{entitySet}'." });
            }

            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Executes a batch of operations.
    /// POST /odata/$batch
    /// </summary>
    [HttpPost("$batch")]
    public async Task<IActionResult> ExecuteBatch(
        [FromBody] ODataBatchRequest batchRequest,
        CancellationToken cancellationToken)
    {
        var projectId = _projectAccessor.ProjectId;
        var modelResult = await _modelProvider.GetModelWithMappingAsync(projectId, cancellationToken);
        var responses = new List<ODataBatchResponseItem>();

        foreach (var request in batchRequest.Requests)
        {
            var response = await ExecuteBatchItemAsync(projectId, request, modelResult.EntitySetToTableNameMap, cancellationToken);
            responses.Add(response);
        }

        return Ok(new ODataBatchResponse { Responses = responses });
    }

    private async Task<ODataBatchResponseItem> ExecuteBatchItemAsync(
        Guid projectId,
        ODataBatchRequestItem request,
        IReadOnlyDictionary<string, string> entitySetToTableNameMap,
        CancellationToken cancellationToken)
    {
        try
        {
            var tableName = ResolveTableName(request.EntitySet, entitySetToTableNameMap);

            // Resolve table metadata for write operations
            var tableMetadata = await _metadataRepository.GetTableByNameAsync(
                projectId, tableName, includeColumns: true, cancellationToken);

            switch (request.Method.ToUpperInvariant())
            {
                case "POST":
                    if (tableMetadata is null)
                    {
                        return new ODataBatchResponseItem { Id = request.Id, Status = 404, Error = $"Table '{tableName}' not found." };
                    }
                    var insertData = request.Body != null
                        ? JsonElementToDictionary(request.Body.Value)
                        : new Dictionary<string, object?>();
                    var insertWriteResult = await _writePipeline.InsertAsync(projectId, tableMetadata, insertData, cancellationToken: cancellationToken);
                    if (!insertWriteResult.Success)
                    {
                        return new ODataBatchResponseItem { Id = request.Id, Status = 400, Error = string.Join("; ", insertWriteResult.Errors.Select(e => e.Message)) };
                    }
                    return new ODataBatchResponseItem
                    {
                        Id = request.Id,
                        Status = 201,
                        Body = insertWriteResult.Data
                    };

                case "PATCH":
                case "PUT":
                    if (!request.Key.HasValue)
                    {
                        return new ODataBatchResponseItem
                        {
                            Id = request.Id,
                            Status = 400,
                            Error = "Key is required for PATCH/PUT operations."
                        };
                    }
                    if (tableMetadata is null)
                    {
                        return new ODataBatchResponseItem { Id = request.Id, Status = 404, Error = $"Table '{tableName}' not found." };
                    }
                    var existing = await _dataService.GetByIdAsync(projectId, tableName, request.Key.Value, cancellationToken);
                    var updateData = request.Body != null
                        ? JsonElementToDictionary(request.Body.Value)
                        : new Dictionary<string, object?>();
                    var updateWriteResult = await _writePipeline.UpdateAsync(projectId, tableMetadata, request.Key.Value, updateData, existing, cancellationToken: cancellationToken);
                    if (!updateWriteResult.Success)
                    {
                        return new ODataBatchResponseItem { Id = request.Id, Status = 400, Error = string.Join("; ", updateWriteResult.Errors.Select(e => e.Message)) };
                    }
                    return new ODataBatchResponseItem
                    {
                        Id = request.Id,
                        Status = 200,
                        Body = updateWriteResult.Data
                    };

                case "DELETE":
                    if (!request.Key.HasValue)
                    {
                        return new ODataBatchResponseItem
                        {
                            Id = request.Id,
                            Status = 400,
                            Error = "Key is required for DELETE operations."
                        };
                    }
                    if (tableMetadata is null)
                    {
                        return new ODataBatchResponseItem { Id = request.Id, Status = 404, Error = $"Table '{tableName}' not found." };
                    }
                    var deleteWriteResult = await _writePipeline.DeleteAsync(projectId, tableMetadata, request.Key.Value, cancellationToken: cancellationToken);
                    return new ODataBatchResponseItem
                    {
                        Id = request.Id,
                        Status = deleteWriteResult.Success ? 204 : 404,
                        Error = deleteWriteResult.Success ? null : "Entity not found."
                    };

                case "GET":
                    if (request.Key.HasValue)
                    {
                        var entity = await _dataService.GetByIdAsync(projectId, tableName, request.Key.Value, cancellationToken);
                        if (entity == null)
                        {
                            return new ODataBatchResponseItem
                            {
                                Id = request.Id,
                                Status = 404,
                                Error = "Entity not found."
                            };
                        }
                        return new ODataBatchResponseItem
                        {
                            Id = request.Id,
                            Status = 200,
                            Body = entity
                        };
                    }
                    else
                    {
                        var query = _dataService.Query(projectId).From(tableName).SelectAll();
                        if (request.Top > 0)
                            query = query.Limit(request.Top);
                        var records = await query.ToListAsync(cancellationToken);
                        return new ODataBatchResponseItem
                        {
                            Id = request.Id,
                            Status = 200,
                            Body = records
                        };
                    }

                default:
                    return new ODataBatchResponseItem
                    {
                        Id = request.Id,
                        Status = 400,
                        Error = $"Unsupported method: {request.Method}"
                    };
            }
        }
        catch (Exception ex)
        {
            LogBatchItemError(_logger, request.Id, ex);
            return new ODataBatchResponseItem
            {
                Id = request.Id,
                Status = 500,
                Error = "An unexpected error occurred"
            };
        }
    }

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
}

/// <summary>
/// OData single entity response format.
/// </summary>
internal sealed class ODataSingleResponse
{
    [System.Text.Json.Serialization.JsonPropertyName("@odata.context")]
    public string? Context { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("value")]
    public IDictionary<string, object?>? Value { get; set; }
}

/// <summary>
/// OData batch request format.
/// </summary>
public sealed class ODataBatchRequest
{
    [System.Text.Json.Serialization.JsonPropertyName("requests")]
    public IReadOnlyList<ODataBatchRequestItem> Requests { get; set; } = [];
}

/// <summary>
/// Individual request item in a batch.
/// </summary>
public sealed class ODataBatchRequestItem
{
    [System.Text.Json.Serialization.JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("method")]
    public string Method { get; set; } = "GET";

    [System.Text.Json.Serialization.JsonPropertyName("entitySet")]
    public string EntitySet { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("key")]
    public Guid? Key { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("body")]
    public System.Text.Json.JsonElement? Body { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("top")]
    public int Top { get; set; }
}

/// <summary>
/// OData batch response format.
/// </summary>
public sealed class ODataBatchResponse
{
    [System.Text.Json.Serialization.JsonPropertyName("responses")]
    public IReadOnlyList<ODataBatchResponseItem> Responses { get; set; } = [];
}

/// <summary>
/// Individual response item in a batch.
/// </summary>
public sealed class ODataBatchResponseItem
{
    [System.Text.Json.Serialization.JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("status")]
    public int Status { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("body")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public object? Body { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("error")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? Error { get; set; }
}
