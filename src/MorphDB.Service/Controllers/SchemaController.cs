using Microsoft.AspNetCore.Mvc;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Exceptions;
using MorphDB.Core.Models;
using MorphDB.Npgsql.Services;
using MorphDB.Service.Models.Api;
using MorphDB.Service.OData;
using MorphDB.Service.Realtime;
using MorphDB.Service.Services;

namespace MorphDB.Service.Controllers;

internal static partial class SchemaControllerLogs
{
    [LoggerMessage(LogLevel.Information, "Created table {TableName} for project {ProjectId}")]
    public static partial void TableCreated(ILogger logger, string tableName, Guid projectId);

    [LoggerMessage(LogLevel.Information, "Deleted table {TableName} for project {ProjectId}")]
    public static partial void TableDeleted(ILogger logger, string tableName, Guid projectId);

    [LoggerMessage(LogLevel.Information, "Added column {ColumnName} to table {TableName}")]
    public static partial void ColumnAdded(ILogger logger, string columnName, string tableName);

    [LoggerMessage(LogLevel.Information, "Created index {IndexName} on table {TableName}")]
    public static partial void IndexCreated(ILogger logger, string indexName, string tableName);

    [LoggerMessage(LogLevel.Information, "Created relation {RelationName}")]
    public static partial void RelationCreated(ILogger logger, string relationName);
}

/// <summary>
/// Schema management API endpoints.
/// </summary>
[ApiController]
[Route("api/schema")]
[Produces("application/json")]
public sealed class SchemaController : ControllerBase
{
    private readonly ISchemaManager _schemaManager;
    private readonly IChangeLogger _changeLogger;
    private readonly ILogger<SchemaController> _logger;
    private readonly ChangeNotificationSetup _changeNotificationSetup;
    private readonly IEdmModelProvider _edmModelProvider;
    private readonly IProjectContextAccessor _projectContext;

    public SchemaController(
        ISchemaManager schemaManager,
        IChangeLogger changeLogger,
        ILogger<SchemaController> logger,
        ChangeNotificationSetup changeNotificationSetup,
        IEdmModelProvider edmModelProvider,
        IProjectContextAccessor projectContext)
    {
        _schemaManager = schemaManager;
        _changeLogger = changeLogger;
        _logger = logger;
        _changeNotificationSetup = changeNotificationSetup;
        _edmModelProvider = edmModelProvider;
        _projectContext = projectContext;
    }

    private Guid GetProjectId()
    {
        var projectId = _projectContext.ProjectIdOrNull;
        if (!projectId.HasValue || projectId.Value == Guid.Empty)
        {
            throw new UnauthorizedAccessException("Valid API key is required");
        }
        return projectId.Value;
    }

    #region Tables

    /// <summary>
    /// Creates a new table.
    /// </summary>
    [HttpPost("tables")]
    [ProducesResponseType(typeof(TableApiResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateTable(
        [FromBody] CreateTableApiRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var projectId = GetProjectId();
            var createRequest = new CreateTableRequest
            {
                ProjectId = projectId,
                LogicalName = request.Name,
                Columns = request.Columns.Select(c => new CreateColumnRequest
                {
                    LogicalName = c.Name,
                    DataType = ApiModelExtensions.ParseDataType(c.Type),
                    IsNullable = c.Nullable,
                    IsUnique = c.Unique,
                    IsIndexed = c.Indexed,
                    DefaultValue = c.Default,
                    CheckExpression = c.Check,
                    LookupConfig = c.Lookup?.ToModel(),
                    RollupConfig = c.Rollup?.ToModel(),
                    FormulaConfig = c.Formula?.ToModel()
                }).ToList(),
                SystemColumns = request.SystemColumns?.ToOptions()
            };

            var table = await _schemaManager.CreateTableAsync(createRequest, cancellationToken);
            var response = TableApiResponse.FromMetadata(table);

            // Create notification trigger for realtime updates (using schema-qualified table name)
            await _changeNotificationSetup.CreateTriggerAsync(projectId, table.PhysicalName, cancellationToken);

            // Invalidate cached EDM model so OData picks up the new table
            _edmModelProvider.InvalidateModel(projectId);

            SchemaControllerLogs.TableCreated(_logger, table.LogicalName, projectId);

            return CreatedAtAction(nameof(GetTable), new { name = table.LogicalName }, response);
        }
        catch (DuplicateException ex)
        {
            return Conflict(new ErrorResponse
            {
                Error = "DuplicateTable",
                Message = ex.Message
            });
        }
        catch (ValidationException ex)
        {
            return BadRequest(new ErrorResponse
            {
                Error = "ValidationError",
                Message = ex.Message
            });
        }
        catch (MorphDB.Core.Exceptions.SchemaException ex)
        {
            return BadRequest(new ErrorResponse
            {
                Error = "SchemaError",
                Message = ex.Message
            });
        }
    }

    /// <summary>
    /// Lists all tables for a project.
    /// </summary>
    [HttpGet("tables")]
    [ProducesResponseType(typeof(IReadOnlyList<TableApiResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ListTables(CancellationToken cancellationToken)
    {
        var projectId = GetProjectId();
        var tables = await _schemaManager.ListTablesAsync(projectId, cancellationToken);
        var response = tables.Select(TableApiResponse.FromMetadata).ToList();

        return Ok(response);
    }

    /// <summary>
    /// Gets a table by name.
    /// </summary>
    [HttpGet("tables/{name}")]
    [ProducesResponseType(typeof(TableApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTable(
        string name,
        CancellationToken cancellationToken)
    {
        var projectId = GetProjectId();
        var table = await _schemaManager.GetTableAsync(projectId, name, cancellationToken);

        if (table is null)
        {
            return NotFound(new ErrorResponse
            {
                Error = "TableNotFound",
                Message = $"Table '{name}' not found"
            });
        }

        return Ok(TableApiResponse.FromMetadata(table));
    }

    /// <summary>
    /// Updates a table.
    /// </summary>
    [HttpPatch("tables/{name}")]
    [ProducesResponseType(typeof(TableApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateTable(
        string name,
        [FromBody] UpdateTableApiRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var projectId = GetProjectId();
            var table = await _schemaManager.GetTableAsync(projectId, name, cancellationToken);
            if (table is null)
            {
                return NotFound(new ErrorResponse
                {
                    Error = "TableNotFound",
                    Message = $"Table '{name}' not found"
                });
            }

            var updateRequest = new UpdateTableRequest
            {
                TableId = table.TableId,
                LogicalName = request.Name,
                ExpectedVersion = request.Version
            };

            var updatedTable = await _schemaManager.UpdateTableAsync(updateRequest, cancellationToken);

            // Invalidate cached EDM model so OData picks up schema changes
            _edmModelProvider.InvalidateModel(projectId);

            return Ok(TableApiResponse.FromMetadata(updatedTable));
        }
        catch (ConcurrencyException ex)
        {
            return Conflict(new ErrorResponse
            {
                Error = "ConcurrencyConflict",
                Message = ex.Message
            });
        }
        catch (NotFoundException ex)
        {
            return NotFound(new ErrorResponse
            {
                Error = "TableNotFound",
                Message = ex.Message
            });
        }
    }

    /// <summary>
    /// Deletes a table (soft delete).
    /// </summary>
    [HttpDelete("tables/{name}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteTable(
        string name,
        CancellationToken cancellationToken)
    {
        try
        {
            var projectId = GetProjectId();
            var table = await _schemaManager.GetTableAsync(projectId, name, cancellationToken);
            if (table is null)
            {
                return NotFound(new ErrorResponse
                {
                    Error = "TableNotFound",
                    Message = $"Table '{name}' not found"
                });
            }

            await _schemaManager.DeleteTableAsync(table.TableId, cancellationToken);

            // Invalidate cached EDM model so OData picks up schema changes
            _edmModelProvider.InvalidateModel(projectId);

            SchemaControllerLogs.TableDeleted(_logger, name, projectId);

            return NoContent();
        }
        catch (NotFoundException ex)
        {
            return NotFound(new ErrorResponse
            {
                Error = "TableNotFound",
                Message = ex.Message
            });
        }
    }

    #endregion

    #region Columns

    /// <summary>
    /// Adds a column to a table.
    /// </summary>
    [HttpPost("tables/{tableName}/columns")]
    [ProducesResponseType(typeof(ColumnApiResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddColumn(
        string tableName,
        [FromBody] AddColumnApiRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var projectId = GetProjectId();
            var table = await _schemaManager.GetTableAsync(projectId, tableName, cancellationToken);
            if (table is null)
            {
                return NotFound(new ErrorResponse
                {
                    Error = "TableNotFound",
                    Message = $"Table '{tableName}' not found"
                });
            }

            var addRequest = new AddColumnRequest
            {
                TableId = table.TableId,
                LogicalName = request.Name,
                DataType = ApiModelExtensions.ParseDataType(request.Type),
                IsNullable = request.Nullable,
                IsUnique = request.Unique,
                IsIndexed = request.Indexed,
                DefaultValue = request.Default,
                CheckExpression = request.Check,
                ExpectedVersion = table.SchemaVersion,
                LookupConfig = request.Lookup?.ToModel(),
                RollupConfig = request.Rollup?.ToModel(),
                FormulaConfig = request.Formula?.ToModel()
            };

            var column = await _schemaManager.AddColumnAsync(addRequest, cancellationToken);
            var response = ColumnApiResponse.FromMetadata(column);

            // Invalidate cached EDM model so OData picks up schema changes
            _edmModelProvider.InvalidateModel(projectId);

            SchemaControllerLogs.ColumnAdded(_logger, column.LogicalName, tableName);

            return Created($"/api/schema/columns/{column.ColumnId}", response);
        }
        catch (ValidationException ex)
        {
            return BadRequest(new ErrorResponse
            {
                Error = "ValidationError",
                Message = ex.Message
            });
        }
        catch (DuplicateException ex)
        {
            return Conflict(new ErrorResponse
            {
                Error = "DuplicateColumn",
                Message = ex.Message
            });
        }
        catch (MorphDB.Core.Exceptions.SchemaException ex)
        {
            return BadRequest(new ErrorResponse
            {
                Error = "SchemaError",
                Message = ex.Message
            });
        }
    }

    /// <summary>
    /// Updates a column.
    /// </summary>
    [HttpPatch("columns/{id:guid}")]
    [ProducesResponseType(typeof(ColumnApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateColumn(
        Guid id,
        [FromBody] UpdateColumnApiRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var updateRequest = new UpdateColumnRequest
            {
                ColumnId = id,
                LogicalName = request.Name,
                DefaultValue = request.Default,
                DataType = request.Type != null ? ApiModelExtensions.ParseDataType(request.Type) : null,
                IsNullable = request.Nullable,
                IsUnique = request.Unique,
                CheckExpression = request.Check,
                ExpectedVersion = request.Version,
                ForceCast = request.ForceCast
            };

            var column = await _schemaManager.UpdateColumnAsync(updateRequest, cancellationToken);

            // Invalidate all cached EDM models (column operations don't have project context)
            _edmModelProvider.InvalidateAll();

            return Ok(ColumnApiResponse.FromMetadata(column));
        }
        catch (NotFoundException ex)
        {
            return NotFound(new ErrorResponse
            {
                Error = "ColumnNotFound",
                Message = ex.Message
            });
        }
        catch (ValidationException ex)
        {
            return BadRequest(new ErrorResponse
            {
                Error = "ValidationError",
                Message = ex.Message
            });
        }
        catch (ConcurrencyException ex)
        {
            return Conflict(new ErrorResponse
            {
                Error = "ConcurrencyConflict",
                Message = ex.Message
            });
        }
    }

    /// <summary>
    /// Deletes a column.
    /// </summary>
    [HttpDelete("columns/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteColumn(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await _schemaManager.DeleteColumnAsync(id, cancellationToken);

            // Invalidate all cached EDM models (column operations don't have project context)
            _edmModelProvider.InvalidateAll();

            return NoContent();
        }
        catch (NotFoundException ex)
        {
            return NotFound(new ErrorResponse
            {
                Error = "ColumnNotFound",
                Message = ex.Message
            });
        }
    }

    #endregion

    #region Indexes

    /// <summary>
    /// Creates an index on a table.
    /// </summary>
    [HttpPost("tables/{tableName}/indexes")]
    [ProducesResponseType(typeof(IndexApiResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateIndex(
        string tableName,
        [FromBody] CreateIndexApiRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var projectId = GetProjectId();
            var table = await _schemaManager.GetTableAsync(projectId, tableName, cancellationToken);
            if (table is null)
            {
                return NotFound(new ErrorResponse
                {
                    Error = "TableNotFound",
                    Message = $"Table '{tableName}' not found"
                });
            }

            // Resolve column names to IDs
            var columnIds = new List<Guid>();
            foreach (var columnName in request.Columns)
            {
                var column = table.Columns.FirstOrDefault(c => c.LogicalName == columnName);
                if (column is null)
                {
                    return BadRequest(new ErrorResponse
                    {
                        Error = "ColumnNotFound",
                        Message = $"Column '{columnName}' not found in table '{tableName}'"
                    });
                }
                columnIds.Add(column.ColumnId);
            }

            var createRequest = new CreateIndexRequest
            {
                TableId = table.TableId,
                LogicalName = request.Name,
                ColumnIds = columnIds,
                IndexType = ApiModelExtensions.ParseIndexType(request.Type),
                IsUnique = request.Unique,
                WhereClause = request.Where
            };

            var index = await _schemaManager.CreateIndexAsync(createRequest, cancellationToken);
            var response = IndexApiResponse.FromMetadata(index);

            // Invalidate cached EDM model so OData picks up schema changes
            _edmModelProvider.InvalidateModel(projectId);

            SchemaControllerLogs.IndexCreated(_logger, index.LogicalName, tableName);

            return Created($"/api/schema/indexes/{index.IndexId}", response);
        }
        catch (ValidationException ex)
        {
            return BadRequest(new ErrorResponse
            {
                Error = "ValidationError",
                Message = ex.Message
            });
        }
    }

    /// <summary>
    /// Deletes an index.
    /// </summary>
    [HttpDelete("indexes/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteIndex(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await _schemaManager.DeleteIndexAsync(id, cancellationToken);

            // Invalidate all cached EDM models (index operations don't have project context)
            _edmModelProvider.InvalidateAll();

            return NoContent();
        }
        catch (NotFoundException ex)
        {
            return NotFound(new ErrorResponse
            {
                Error = "IndexNotFound",
                Message = ex.Message
            });
        }
    }

    #endregion

    #region Relations

    /// <summary>
    /// Creates a relation between tables.
    /// </summary>
    [HttpPost("relations")]
    [ProducesResponseType(typeof(RelationApiResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateRelation(
        [FromBody] CreateRelationApiRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var projectId = GetProjectId();
            // Resolve table and column names to IDs
            var sourceTable = await _schemaManager.GetTableAsync(projectId, request.SourceTable, cancellationToken);
            if (sourceTable is null)
            {
                return BadRequest(new ErrorResponse
                {
                    Error = "TableNotFound",
                    Message = $"Source table '{request.SourceTable}' not found"
                });
            }

            var targetTable = await _schemaManager.GetTableAsync(projectId, request.TargetTable, cancellationToken);
            if (targetTable is null)
            {
                return BadRequest(new ErrorResponse
                {
                    Error = "TableNotFound",
                    Message = $"Target table '{request.TargetTable}' not found"
                });
            }

            var sourceColumn = sourceTable.Columns.FirstOrDefault(c => c.LogicalName == request.SourceColumn);
            if (sourceColumn is null)
            {
                return BadRequest(new ErrorResponse
                {
                    Error = "ColumnNotFound",
                    Message = $"Source column '{request.SourceColumn}' not found in table '{request.SourceTable}'"
                });
            }

            var targetColumn = targetTable.Columns.FirstOrDefault(c => c.LogicalName == request.TargetColumn);
            if (targetColumn is null)
            {
                return BadRequest(new ErrorResponse
                {
                    Error = "ColumnNotFound",
                    Message = $"Target column '{request.TargetColumn}' not found in table '{request.TargetTable}'"
                });
            }

            var createRequest = new CreateRelationRequest
            {
                ProjectId = projectId,
                LogicalName = request.Name,
                SourceTableId = sourceTable.TableId,
                SourceColumnId = sourceColumn.ColumnId,
                TargetTableId = targetTable.TableId,
                TargetColumnId = targetColumn.ColumnId,
                RelationType = ApiModelExtensions.ParseRelationType(request.Type),
                OnDelete = ApiModelExtensions.ParseOnDeleteAction(request.OnDelete)
            };

            var relation = await _schemaManager.CreateRelationAsync(createRequest, cancellationToken);
            var response = RelationApiResponse.FromMetadata(relation);

            // Invalidate cached EDM model so OData picks up schema changes
            _edmModelProvider.InvalidateModel(projectId);

            SchemaControllerLogs.RelationCreated(_logger, relation.LogicalName);

            return Created($"/api/schema/relations/{relation.RelationId}", response);
        }
        catch (ValidationException ex)
        {
            return BadRequest(new ErrorResponse
            {
                Error = "ValidationError",
                Message = ex.Message
            });
        }
    }

    /// <summary>
    /// Deletes a relation.
    /// </summary>
    [HttpDelete("relations/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteRelation(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await _schemaManager.DeleteRelationAsync(id, cancellationToken);

            // Invalidate all cached EDM models (relation operations don't have project context)
            _edmModelProvider.InvalidateAll();

            return NoContent();
        }
        catch (NotFoundException ex)
        {
            return NotFound(new ErrorResponse
            {
                Error = "RelationNotFound",
                Message = ex.Message
            });
        }
    }

    #endregion

    #region Batch DDL Operations

    /// <summary>
    /// Executes multiple DDL operations atomically. All operations succeed or all are rolled back.
    /// </summary>
    [HttpPost("batch")]
    [ProducesResponseType(typeof(BatchDdlResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ExecuteBatchDdl(
        [FromBody] BatchDdlApiRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var batchRequest = new BatchDdlRequest
            {
                TableId = request.TableId,
                ExpectedVersion = request.Version,
                Operations = request.Operations.Select(MapBatchOperation).ToList()
            };

            var result = await _schemaManager.ExecuteBatchDdlAsync(batchRequest, cancellationToken);

            _edmModelProvider.InvalidateAll();

            return Ok(result);
        }
        catch (NotFoundException ex)
        {
            return NotFound(new ErrorResponse { Error = "TableNotFound", Message = ex.Message });
        }
        catch (ValidationException ex)
        {
            return BadRequest(new ErrorResponse { Error = "ValidationError", Message = ex.Message });
        }
        catch (ConcurrencyException ex)
        {
            return Conflict(new ErrorResponse { Error = "ConcurrencyConflict", Message = ex.Message });
        }
        catch (Core.Exceptions.SchemaException ex)
        {
            return BadRequest(new ErrorResponse { Error = "BatchDdlFailed", Message = ex.Message });
        }
    }

    private static BatchDdlOperation MapBatchOperation(BatchDdlOperationApiRequest op)
    {
        return new BatchDdlOperation
        {
            Type = op.Type,
            AddColumn = op.AddColumn is not null ? new AddColumnRequest
            {
                LogicalName = op.AddColumn.Name,
                DataType = ApiModelExtensions.ParseDataType(op.AddColumn.Type),
                IsNullable = op.AddColumn.Nullable,
                IsUnique = op.AddColumn.Unique,
                IsIndexed = op.AddColumn.Indexed,
                DefaultValue = op.AddColumn.Default,
                CheckExpression = op.AddColumn.Check
            } : null,
            UpdateColumn = op.UpdateColumn,
            DeleteColumnId = op.DeleteColumnId,
            CreateIndex = op.CreateIndex is not null ? new CreateIndexRequest
            {
                LogicalName = op.CreateIndex.Name,
                ColumnIds = op.CreateIndex.Columns.Select(Guid.Parse).ToList(),
                IsUnique = op.CreateIndex.Unique,
                WhereClause = op.CreateIndex.Where
            } : null,
            DeleteIndexId = op.DeleteIndexId,
            CreateRelation = op.CreateRelation is not null ? new CreateRelationRequest
            {
                LogicalName = op.CreateRelation.Name,
                SourceColumnId = Guid.Parse(op.CreateRelation.SourceColumn),
                TargetTableId = Guid.Parse(op.CreateRelation.TargetTable),
                TargetColumnId = Guid.Parse(op.CreateRelation.TargetColumn),
                RelationType = Enum.Parse<RelationType>(op.CreateRelation.Type ?? "ManyToOne", ignoreCase: true),
                OnDelete = Enum.Parse<OnDeleteAction>(op.CreateRelation.OnDelete ?? "NoAction", ignoreCase: true)
            } : null,
            DeleteRelationId = op.DeleteRelationId
        };
    }

    #endregion

    #region Changelog Operations

    /// <summary>
    /// Gets the schema change history for a specific table.
    /// </summary>
    [HttpGet("tables/{name}/history")]
    [ProducesResponseType(typeof(IReadOnlyList<SchemaChangeApiResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTableHistory(
        string name,
        [FromQuery] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var projectId = _projectContext.ProjectIdOrNull
                ?? throw new UnauthorizedAccessException("Project context required");

            var table = await _schemaManager.GetTableAsync(projectId, name, cancellationToken)
                ?? throw new NotFoundException("Table", name);

            var history = await _changeLogger.GetHistoryAsync(
                table.TableId, Math.Clamp(limit, 1, 500), cancellationToken);

            return Ok(history.Select(SchemaChangeApiResponse.FromEntry).ToList());
        }
        catch (NotFoundException ex)
        {
            return NotFound(new ErrorResponse
            {
                Error = "TableNotFound",
                Message = ex.Message
            });
        }
    }

    /// <summary>
    /// Gets the global schema changelog across all tables.
    /// </summary>
    [HttpGet("changelog")]
    [ProducesResponseType(typeof(IReadOnlyList<SchemaChangeApiResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetChangelog(
        [FromQuery] int limit = 50,
        [FromQuery] int offset = 0,
        CancellationToken cancellationToken = default)
    {
        var changelog = await _changeLogger.GetChangelogAsync(
            Math.Clamp(limit, 1, 500),
            Math.Max(offset, 0),
            cancellationToken);

        return Ok(changelog.Select(SchemaChangeApiResponse.FromEntry).ToList());
    }

    #endregion
}
