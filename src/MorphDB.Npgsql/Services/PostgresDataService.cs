using System.Dynamic;
using Dapper;
using Microsoft.Extensions.Options;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Encryption;
using MorphDB.Core.Exceptions;
using MorphDB.Core.Models;
using MorphDB.Core.Security;
using MorphDB.Npgsql.Dml;
using MorphDB.Npgsql.Infrastructure;
using MorphDB.Npgsql.Query;
using MorphDB.Npgsql.Repositories;
using Npgsql;

namespace MorphDB.Npgsql.Services;

/// <summary>
/// PostgreSQL implementation of IMorphDataService.
/// Handles CRUD operations with logical-to-physical name translation
/// and transparent data encryption.
/// </summary>
public sealed class PostgresDataService : IMorphDataService
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly IMetadataRepository _metadataRepository;
    private readonly ISecurityPolicyService _securityPolicyService;
    private readonly ISecurityContextAccessor _securityContextAccessor;
    private readonly ILookupResolver? _lookupResolver;
    private readonly IRollupResolver? _rollupResolver;
    private readonly IFormulaResolver? _formulaResolver;
    private readonly IDataEncryptionService? _encryptionService;
    private readonly DataEncryptionOptions _encryptionOptions;
    private readonly string _primaryKeyLogicalName;

    /// <summary>
    /// Creates a new PostgresDataService.
    /// </summary>
    public PostgresDataService(
        NpgsqlDataSource dataSource,
        IMetadataRepository metadataRepository,
        ISecurityPolicyService securityPolicyService,
        ISecurityContextAccessor securityContextAccessor,
        ILookupResolver? lookupResolver = null,
        IRollupResolver? rollupResolver = null,
        IFormulaResolver? formulaResolver = null,
        IDataEncryptionService? encryptionService = null,
        IOptions<DataEncryptionOptions>? encryptionOptions = null,
        string primaryKeyLogicalName = "id")
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        _metadataRepository = metadataRepository ?? throw new ArgumentNullException(nameof(metadataRepository));
        _securityPolicyService = securityPolicyService ?? throw new ArgumentNullException(nameof(securityPolicyService));
        _securityContextAccessor = securityContextAccessor ?? throw new ArgumentNullException(nameof(securityContextAccessor));
        _lookupResolver = lookupResolver;
        _rollupResolver = rollupResolver;
        _formulaResolver = formulaResolver;
        _encryptionService = encryptionService;
        _encryptionOptions = encryptionOptions?.Value ?? new DataEncryptionOptions();
        _primaryKeyLogicalName = primaryKeyLogicalName;
    }

    /// <inheritdoc />
    public IMorphQueryBuilder Query(Guid projectId)
    {
        return new MorphQueryBuilder(
            _dataSource,
            _metadataRepository,
            _securityPolicyService,
            _securityContextAccessor,
            projectId,
            _lookupResolver,
            _rollupResolver,
            _formulaResolver);
    }

    /// <inheritdoc />
    public async Task<IDictionary<string, object?>?> GetByIdAsync(
        Guid projectId,
        string tableName,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var table = await GetTableWithColumnsAsync(projectId, tableName, cancellationToken);
        var idColumn = GetPrimaryKeyColumn(table);

        var sql = DmlBuilder.BuildSelectById(table.PhysicalName, idColumn.PhysicalName);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var result = await connection.QuerySingleOrDefaultAsync<dynamic>(
            new CommandDefinition(sql, new { id }, cancellationToken: cancellationToken));

        if (result is null)
            return null;

        var mapped = MapToLogicalDictionary(result, table.Columns);

        // Decrypt encrypted columns
        return DecryptRowData(projectId, tableName, mapped, table.Columns);
    }

    /// <inheritdoc />
    public async Task<IDictionary<string, object?>> InsertAsync(
        Guid projectId,
        string tableName,
        IDictionary<string, object?> data,
        CancellationToken cancellationToken = default)
    {
        var table = await GetTableWithColumnsAsync(projectId, tableName, cancellationToken);

        // Ensure project_id is set
        var dataWithProject = EnsureProjectId(data, projectId);

        // Encrypt data before storing
        var encryptedData = EncryptRowData(projectId, tableName, dataWithProject, table.Columns);

        // Map logical names to physical and prepare parameters
        var (columns, parameters, values) = PrepareInsertParameters(encryptedData, table.Columns);

        var sql = DmlBuilder.BuildInsert(table.PhysicalName, columns, parameters);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var result = await connection.QuerySingleAsync<dynamic>(
            new CommandDefinition(sql, values, cancellationToken: cancellationToken));

        var mapped = MapToLogicalDictionary(result, table.Columns);

        // Return decrypted data to the caller
        return DecryptRowData(projectId, tableName, mapped, table.Columns);
    }

    /// <inheritdoc />
    public async Task<IDictionary<string, object?>> UpdateAsync(
        Guid projectId,
        string tableName,
        Guid id,
        IDictionary<string, object?> data,
        CancellationToken cancellationToken = default)
    {
        var table = await GetTableWithColumnsAsync(projectId, tableName, cancellationToken);
        var idColumn = GetPrimaryKeyColumn(table);

        // Encrypt data before storing
        var encryptedData = EncryptRowData(projectId, tableName, data, table.Columns);

        // Map logical names to physical and prepare parameters
        var (setColumns, values) = PrepareUpdateParameters(encryptedData, table.Columns);
        values.id = id;

        var whereClause = DmlBuilder.BuildIdWhereClause(idColumn.PhysicalName);
        var sql = DmlBuilder.BuildUpdate(table.PhysicalName, setColumns, whereClause);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var result = await connection.QuerySingleOrDefaultAsync<dynamic>(
            new CommandDefinition(sql, values, cancellationToken: cancellationToken));

        if (result is null)
            throw new NotFoundException($"Record with id '{id}' not found in table '{tableName}'");

        var mapped = MapToLogicalDictionary(result, table.Columns);

        // Return decrypted data to the caller
        return DecryptRowData(projectId, tableName, mapped, table.Columns);
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(
        Guid projectId,
        string tableName,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var table = await GetTableWithColumnsAsync(projectId, tableName, cancellationToken);
        var idColumn = GetPrimaryKeyColumn(table);

        var whereClause = DmlBuilder.BuildIdWhereClause(idColumn.PhysicalName);
        var sql = DmlBuilder.BuildDelete(table.PhysicalName, whereClause);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var affectedRows = await connection.ExecuteAsync(
            new CommandDefinition(sql, new { id }, cancellationToken: cancellationToken));

        return affectedRows > 0;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<IDictionary<string, object?>>> InsertBatchAsync(
        Guid projectId,
        string tableName,
        IReadOnlyList<IDictionary<string, object?>> records,
        CancellationToken cancellationToken = default)
    {
        if (records.Count == 0)
            return Array.Empty<IDictionary<string, object?>>();

        var table = await GetTableWithColumnsAsync(projectId, tableName, cancellationToken);

        // For batch insert, use individual inserts in a transaction for simplicity
        // This could be optimized with COPY or multi-row VALUES in future
        var results = new List<IDictionary<string, object?>>(records.Count);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            foreach (var record in records)
            {
                // Ensure project_id is set for each record
                var recordWithProject = EnsureProjectId(record, projectId);

                // Encrypt data before storing
                var encryptedRecord = EncryptRowData(projectId, tableName, recordWithProject, table.Columns);

                var (columns, parameters, values) = PrepareInsertParameters(encryptedRecord, table.Columns);
                var sql = DmlBuilder.BuildInsert(table.PhysicalName, columns, parameters);

                var result = await connection.QuerySingleAsync<dynamic>(
                    new CommandDefinition(sql, values, transaction: transaction, cancellationToken: cancellationToken));

                var mapped = MapToLogicalDictionary(result, table.Columns);

                // Return decrypted data to the caller
                results.Add(DecryptRowData(projectId, tableName, mapped, table.Columns));
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        return results;
    }

    /// <inheritdoc />
    public async Task<int> UpdateBatchAsync(
        Guid projectId,
        string tableName,
        IDictionary<string, object?> data,
        IMorphQuery whereClause,
        CancellationToken cancellationToken = default)
    {
        var table = await GetTableWithColumnsAsync(projectId, tableName, cancellationToken);

        // Map logical names to physical and prepare parameters
        var (setColumns, values) = PrepareUpdateParameters(data, table.Columns);

        // Get physical WHERE clause SQL from the query
        var (whereSql, whereParams) = await whereClause.GetPhysicalWhereClauseAsync(cancellationToken);

        // Merge parameters
        var valuesDict = (IDictionary<string, object?>)values;
        foreach (var (key, value) in whereParams)
        {
            valuesDict[key] = value;
        }

        var sql = DmlBuilder.BuildBatchUpdate(table.PhysicalName, setColumns, whereSql);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var affectedRows = await connection.ExecuteAsync(
            new CommandDefinition(sql, values, cancellationToken: cancellationToken));

        return affectedRows;
    }

    /// <inheritdoc />
    public async Task<int> DeleteBatchAsync(
        Guid projectId,
        string tableName,
        IMorphQuery whereClause,
        CancellationToken cancellationToken = default)
    {
        var table = await GetTableWithColumnsAsync(projectId, tableName, cancellationToken);

        // Get physical WHERE clause SQL from the query
        var (whereSql, whereParams) = await whereClause.GetPhysicalWhereClauseAsync(cancellationToken);

        // Build DELETE statement with WHERE clause if present
        var sql = string.IsNullOrEmpty(whereSql)
            ? $"DELETE FROM {table.PhysicalName}"
            : $"DELETE FROM {table.PhysicalName} WHERE {whereSql}";

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var affectedRows = await connection.ExecuteAsync(
            new CommandDefinition(sql, whereParams, cancellationToken: cancellationToken));

        return affectedRows;
    }

    private static string ExtractWhereClause(IMorphQuery query)
    {
        // Get full SQL and extract WHERE clause
        var fullSql = query.ToSql();
        var whereIndex = fullSql.IndexOf("WHERE", StringComparison.OrdinalIgnoreCase);
        if (whereIndex < 0)
            return "";

        // Find the end of WHERE clause (before ORDER BY, LIMIT, etc.)
        var endKeywords = new[] { "ORDER BY", "GROUP BY", "HAVING", "LIMIT", "OFFSET" };
        var endIndex = fullSql.Length;

        foreach (var keyword in endKeywords)
        {
            var keywordIndex = fullSql.IndexOf(keyword, whereIndex, StringComparison.OrdinalIgnoreCase);
            if (keywordIndex > 0 && keywordIndex < endIndex)
            {
                endIndex = keywordIndex;
            }
        }

        // Extract the WHERE clause and strip the "WHERE" keyword since BuildBatchUpdate adds it
        var whereClause = fullSql[whereIndex..endIndex].Trim();
        if (whereClause.StartsWith("WHERE", StringComparison.OrdinalIgnoreCase))
        {
            whereClause = whereClause[5..].TrimStart(); // Remove "WHERE" and leading whitespace
        }

        return whereClause;
    }

    /// <inheritdoc />
    public async Task<IDictionary<string, object?>> UpsertAsync(
        Guid projectId,
        string tableName,
        IDictionary<string, object?> data,
        string[] keyColumns,
        CancellationToken cancellationToken = default)
    {
        var table = await GetTableWithColumnsAsync(projectId, tableName, cancellationToken);

        // Validate key columns exist
        var columnMap = table.Columns.ToDictionary(c => c.LogicalName, c => c);
        foreach (var key in keyColumns)
        {
            if (!columnMap.ContainsKey(key))
                throw new ValidationException($"Key column '{key}' not found in table '{tableName}'");
        }

        // Map logical key columns to physical
        var physicalKeyColumns = keyColumns.Select(k => columnMap[k].PhysicalName).ToList();

        // Ensure project_id is set
        var dataWithProject = EnsureProjectId(data, projectId);

        // Encrypt data before storing
        var encryptedData = EncryptRowData(projectId, tableName, dataWithProject, table.Columns);

        // Prepare insert parameters
        var (columns, parameters, values) = PrepareInsertParameters(encryptedData, table.Columns);

        var sql = DmlBuilder.BuildUpsert(table.PhysicalName, columns, parameters, physicalKeyColumns);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var result = await connection.QuerySingleAsync<dynamic>(
            new CommandDefinition(sql, values, cancellationToken: cancellationToken));

        var mapped = MapToLogicalDictionary(result, table.Columns);

        // Return decrypted data to the caller
        return DecryptRowData(projectId, tableName, mapped, table.Columns);
    }

    #region Private Helper Methods

    private async Task<TableMetadata> GetTableWithColumnsAsync(
        Guid projectId,
        string tableName,
        CancellationToken cancellationToken)
    {
        var table = await _metadataRepository.GetTableByNameAsync(projectId, tableName, includeColumns: true, cancellationToken);

        if (table is null)
            throw new NotFoundException($"Table '{tableName}' not found for project '{projectId}'");

        if (table.Columns.Count == 0)
            throw new InvalidOperationException($"Table '{tableName}' has no columns");

        return table;
    }

    private ColumnMetadata GetPrimaryKeyColumn(TableMetadata table)
    {
        var pkColumn = table.Columns.FirstOrDefault(c => c.IsPrimaryKey)
            ?? table.Columns.FirstOrDefault(c => c.LogicalName == _primaryKeyLogicalName);

        if (pkColumn is null)
            throw new InvalidOperationException($"Table '{table.LogicalName}' has no primary key column");

        return pkColumn;
    }

    /// <summary>
    /// Ensures the project_id is included in the data dictionary.
    /// If not present, adds it; if present, verifies it matches.
    /// </summary>
    private static IDictionary<string, object?> EnsureProjectId(IDictionary<string, object?> data, Guid projectId)
    {
        const string ProjectIdColumn = "project_id";

        if (data.TryGetValue(ProjectIdColumn, out var existingValue))
        {
            // If project_id is already in data, verify it matches
            if (existingValue is Guid existingGuid && existingGuid != projectId)
            {
                throw new ValidationException($"Provided project_id '{existingGuid}' does not match the expected project_id '{projectId}'");
            }
            return data;
        }

        // Create a new dictionary with project_id added
        var result = new Dictionary<string, object?>(data)
        {
            [ProjectIdColumn] = projectId
        };
        return result;
    }

    private static (List<string> Columns, List<string> Parameters, dynamic Values) PrepareInsertParameters(
        IDictionary<string, object?> data,
        IReadOnlyList<ColumnMetadata> columns)
    {
        var columnMap = columns.ToDictionary(c => c.LogicalName, c => c);
        var physicalColumns = new List<string>();
        var parameterNames = new List<string>();
        dynamic values = new ExpandoObject();
        var valuesDict = (IDictionary<string, object?>)values;

        int paramIndex = 0;
        foreach (var (logicalName, value) in data)
        {
            if (!columnMap.TryGetValue(logicalName, out var column))
            {
                throw new ValidationException($"Column '{logicalName}' not found in table metadata");
            }

            physicalColumns.Add(column.PhysicalName);
            var paramName = $"@p{paramIndex}";
            parameterNames.Add(TypeMapper.IsJsonbType(column.DataType) ? $"{paramName}::jsonb" : paramName);

            // Convert value to database type
            var dbValue = TypeMapper.ToDbValue(value, column.DataType);
            valuesDict[$"p{paramIndex}"] = dbValue;

            paramIndex++;
        }

        return (physicalColumns, parameterNames, values);
    }

    private static (List<(string ColumnName, string ParameterName)> SetColumns, dynamic Values) PrepareUpdateParameters(
        IDictionary<string, object?> data,
        IReadOnlyList<ColumnMetadata> columns)
    {
        var columnMap = columns.ToDictionary(c => c.LogicalName, c => c);
        var setColumns = new List<(string ColumnName, string ParameterName)>();
        dynamic values = new ExpandoObject();
        var valuesDict = (IDictionary<string, object?>)values;

        int paramIndex = 0;
        foreach (var (logicalName, value) in data)
        {
            if (!columnMap.TryGetValue(logicalName, out var column))
            {
                throw new ValidationException($"Column '{logicalName}' not found in table metadata");
            }

            // Skip primary key columns in SET clause
            if (column.IsPrimaryKey)
                continue;

            var paramName = $"@p{paramIndex}";
            var paramExpr = TypeMapper.IsJsonbType(column.DataType) ? $"{paramName}::jsonb" : paramName;
            setColumns.Add((column.PhysicalName, paramExpr));

            // Convert value to database type
            var dbValue = TypeMapper.ToDbValue(value, column.DataType);
            valuesDict[$"p{paramIndex}"] = dbValue;

            paramIndex++;
        }

        return (setColumns, values);
    }

    private static Dictionary<string, object?> MapToLogicalDictionary(
        dynamic row,
        IReadOnlyList<ColumnMetadata> columns)
    {
        var physicalToLogical = columns.ToDictionary(c => c.PhysicalName.ToLowerInvariant(), c => c);
        var result = new Dictionary<string, object?>();

        var rowDict = (IDictionary<string, object?>)row;

        foreach (var (key, value) in rowDict)
        {
            var normalizedKey = key.ToLowerInvariant();
            if (physicalToLogical.TryGetValue(normalizedKey, out var column))
            {
                // Convert from database type to .NET type
                var convertedValue = TypeMapper.FromDbValue(value, column.DataType);
                result[column.LogicalName] = convertedValue;
            }
            else
            {
                // Unknown column, keep as-is
                result[key] = value;
            }
        }

        return result;
    }

    /// <summary>
    /// Encrypts row data for storage.
    /// Only encrypts columns that are marked for encryption or configured for auto-encryption.
    /// </summary>
    private IDictionary<string, object?> EncryptRowData(
        Guid projectId,
        string tableName,
        IDictionary<string, object?> data,
        IReadOnlyList<ColumnMetadata> columns)
    {
        if (_encryptionService is null || !_encryptionService.IsEnabled)
            return data;

        // Determine which columns should be encrypted
        var encryptedColumnNames = GetEncryptedColumnNames(columns);

        if (encryptedColumnNames.Count == 0)
            return data;

        return _encryptionService.EncryptRow(projectId, tableName, data, encryptedColumnNames);
    }

    /// <summary>
    /// Decrypts row data for retrieval.
    /// </summary>
    private IDictionary<string, object?> DecryptRowData(
        Guid projectId,
        string tableName,
        IDictionary<string, object?> data,
        IReadOnlyList<ColumnMetadata> columns)
    {
        if (_encryptionService is null || !_encryptionService.IsEnabled)
            return data;

        // Determine which columns should be decrypted
        var encryptedColumnNames = GetEncryptedColumnNames(columns);

        if (encryptedColumnNames.Count == 0)
            return data;

        return _encryptionService.DecryptRow(projectId, tableName, data, encryptedColumnNames);
    }

    /// <summary>
    /// Gets the set of column names that should be encrypted.
    /// </summary>
    private HashSet<string> GetEncryptedColumnNames(IReadOnlyList<ColumnMetadata> columns)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var column in columns)
        {
            // Skip excluded columns (id, project_id, timestamps, etc.)
            if (_encryptionOptions.ExcludedColumns.Contains(column.LogicalName))
                continue;

            // Include if explicitly marked as encrypted
            if (column.IsEncrypted)
            {
                result.Add(column.LogicalName);
                continue;
            }

            // Include if encrypt all by default is enabled and column type is encryptable
            if (_encryptionOptions.EncryptAllByDefault && IsEncryptableDataType(column.DataType))
            {
                result.Add(column.LogicalName);
            }
        }

        return result;
    }

    /// <summary>
    /// Determines if a data type is suitable for encryption.
    /// </summary>
    private static bool IsEncryptableDataType(MorphDataType dataType)
    {
        return dataType switch
        {
            MorphDataType.Text => true,
            MorphDataType.LongText => true,
            MorphDataType.Email => true,
            MorphDataType.Phone => true,
            MorphDataType.Url => true,
            MorphDataType.Json => true,
            MorphDataType.Integer => true,
            MorphDataType.BigInteger => true,
            MorphDataType.Decimal => true,
            // Don't encrypt: Boolean, Date/Time, UUID (used for joins), relations, computed fields
            _ => false
        };
    }

    #endregion
}
