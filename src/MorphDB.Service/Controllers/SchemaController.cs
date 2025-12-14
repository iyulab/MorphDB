using Microsoft.AspNetCore.Mvc;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Exceptions;
using MorphDB.Service.Models.Api;

namespace MorphDB.Service.Controllers;

internal static partial class SchemaControllerLogs
{
    [LoggerMessage(LogLevel.Information, "Created table {TableName} for tenant {TenantId}")]
    public static partial void TableCreated(ILogger logger, string tableName, Guid tenantId);

    [LoggerMessage(LogLevel.Information, "Deleted table {TableName} for tenant {TenantId}")]
    public static partial void TableDeleted(ILogger logger, string tableName, Guid tenantId);

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
    private readonly ILogger<SchemaController> _logger;

    public SchemaController(ISchemaManager schemaManager, ILogger<SchemaController> logger)
    {
        _schemaManager = schemaManager;
        _logger = logger;
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
        [FromHeader(Name = "X-Tenant-Id")] Guid tenantId,
        CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty)
        {
            return BadRequest(new ErrorResponse
            {
                Error = "InvalidTenant",
                Message = "X-Tenant-Id header is required"
            });
        }

        try
        {
            var createRequest = new CreateTableRequest
            {
                TenantId = tenantId,
                LogicalName = request.Name,
                Columns = request.Columns.Select(c => new CreateColumnRequest
                {
                    LogicalName = c.Name,
                    DataType = ApiModelExtensions.ParseDataType(c.Type),
                    IsNullable = c.Nullable,
                    IsUnique = c.Unique,
                    IsIndexed = c.Indexed,
                    DefaultValue = c.Default
                }).ToList()
            };

            var table = await _schemaManager.CreateTableAsync(createRequest, cancellationToken);
            var response = TableApiResponse.FromMetadata(table);

            SchemaControllerLogs.TableCreated(_logger, table.LogicalName, tenantId);

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
    }

    /// <summary>
    /// Lists all tables for a tenant.
    /// </summary>
    [HttpGet("tables")]
    [ProducesResponseType(typeof(IReadOnlyList<TableApiResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListTables(
        [FromHeader(Name = "X-Tenant-Id")] Guid tenantId,
        CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty)
        {
            return BadRequest(new ErrorResponse
            {
                Error = "InvalidTenant",
                Message = "X-Tenant-Id header is required"
            });
        }

        var tables = await _schemaManager.ListTablesAsync(tenantId, cancellationToken);
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
        [FromHeader(Name = "X-Tenant-Id")] Guid tenantId,
        CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty)
        {
            return BadRequest(new ErrorResponse
            {
                Error = "InvalidTenant",
                Message = "X-Tenant-Id header is required"
            });
        }

        var table = await _schemaManager.GetTableAsync(tenantId, name, cancellationToken);

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
        [FromHeader(Name = "X-Tenant-Id")] Guid tenantId,
        CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty)
        {
            return BadRequest(new ErrorResponse
            {
                Error = "InvalidTenant",
                Message = "X-Tenant-Id header is required"
            });
        }

        try
        {
            var table = await _schemaManager.GetTableAsync(tenantId, name, cancellationToken);
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
        [FromHeader(Name = "X-Tenant-Id")] Guid tenantId,
        CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty)
        {
            return BadRequest(new ErrorResponse
            {
                Error = "InvalidTenant",
                Message = "X-Tenant-Id header is required"
            });
        }

        try
        {
            var table = await _schemaManager.GetTableAsync(tenantId, name, cancellationToken);
            if (table is null)
            {
                return NotFound(new ErrorResponse
                {
                    Error = "TableNotFound",
                    Message = $"Table '{name}' not found"
                });
            }

            await _schemaManager.DeleteTableAsync(table.TableId, cancellationToken);
            SchemaControllerLogs.TableDeleted(_logger, name, tenantId);

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
        [FromHeader(Name = "X-Tenant-Id")] Guid tenantId,
        CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty)
        {
            return BadRequest(new ErrorResponse
            {
                Error = "InvalidTenant",
                Message = "X-Tenant-Id header is required"
            });
        }

        try
        {
            var table = await _schemaManager.GetTableAsync(tenantId, tableName, cancellationToken);
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
                ExpectedVersion = table.SchemaVersion
            };

            var column = await _schemaManager.AddColumnAsync(addRequest, cancellationToken);
            var response = ColumnApiResponse.FromMetadata(column);

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
                ExpectedVersion = request.Version
            };

            var column = await _schemaManager.UpdateColumnAsync(updateRequest, cancellationToken);
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
        [FromHeader(Name = "X-Tenant-Id")] Guid tenantId,
        CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty)
        {
            return BadRequest(new ErrorResponse
            {
                Error = "InvalidTenant",
                Message = "X-Tenant-Id header is required"
            });
        }

        try
        {
            var table = await _schemaManager.GetTableAsync(tenantId, tableName, cancellationToken);
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
        [FromHeader(Name = "X-Tenant-Id")] Guid tenantId,
        CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty)
        {
            return BadRequest(new ErrorResponse
            {
                Error = "InvalidTenant",
                Message = "X-Tenant-Id header is required"
            });
        }

        try
        {
            // Resolve table and column names to IDs
            var sourceTable = await _schemaManager.GetTableAsync(tenantId, request.SourceTable, cancellationToken);
            if (sourceTable is null)
            {
                return BadRequest(new ErrorResponse
                {
                    Error = "TableNotFound",
                    Message = $"Source table '{request.SourceTable}' not found"
                });
            }

            var targetTable = await _schemaManager.GetTableAsync(tenantId, request.TargetTable, cancellationToken);
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
                TenantId = tenantId,
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
}
