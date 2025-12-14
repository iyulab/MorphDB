using System.Text;

namespace MorphDB.Npgsql.Dml;

/// <summary>
/// Builds DML SQL statements for PostgreSQL.
/// </summary>
public static class DmlBuilder
{
    /// <summary>
    /// Builds an INSERT statement with RETURNING clause.
    /// </summary>
    /// <param name="tableName">Physical table name.</param>
    /// <param name="columnNames">Physical column names.</param>
    /// <param name="parameterNames">Parameter placeholder names (e.g., @p0, @p1).</param>
    /// <returns>INSERT SQL with RETURNING *.</returns>
    public static string BuildInsert(string tableName, IEnumerable<string> columnNames, IEnumerable<string> parameterNames)
    {
        var columns = columnNames.ToList();
        var parameters = parameterNames.ToList();

        if (columns.Count == 0)
            throw new ArgumentException("At least one column is required", nameof(columnNames));

        if (columns.Count != parameters.Count)
            throw new ArgumentException("Column count must match parameter count", nameof(parameterNames));

        var sb = new StringBuilder();
        sb.Append("INSERT INTO ");
        sb.Append(QuoteIdentifier(tableName));
        sb.Append(" (");
        sb.Append(string.Join(", ", columns.Select(QuoteIdentifier)));
        sb.Append(") VALUES (");
        sb.Append(string.Join(", ", parameters));
        sb.Append(") RETURNING *");

        return sb.ToString();
    }

    /// <summary>
    /// Builds a bulk INSERT statement using UNNEST for optimal performance.
    /// </summary>
    /// <param name="tableName">Physical table name.</param>
    /// <param name="columnNames">Physical column names.</param>
    /// <param name="arrayParameterNames">Array parameter names (e.g., @arr0, @arr1).</param>
    /// <returns>Bulk INSERT SQL with RETURNING *.</returns>
    public static string BuildBulkInsert(string tableName, IEnumerable<string> columnNames, IEnumerable<string> arrayParameterNames)
    {
        var columns = columnNames.ToList();
        var arrayParams = arrayParameterNames.ToList();

        if (columns.Count == 0)
            throw new ArgumentException("At least one column is required", nameof(columnNames));

        if (columns.Count != arrayParams.Count)
            throw new ArgumentException("Column count must match array parameter count", nameof(arrayParameterNames));

        var sb = new StringBuilder();
        sb.Append("INSERT INTO ");
        sb.Append(QuoteIdentifier(tableName));
        sb.Append(" (");
        sb.Append(string.Join(", ", columns.Select(QuoteIdentifier)));
        sb.AppendLine(")");
        sb.Append("SELECT * FROM UNNEST(");
        sb.Append(string.Join(", ", arrayParams));
        sb.Append(") RETURNING *");

        return sb.ToString();
    }

    /// <summary>
    /// Builds an UPDATE statement with WHERE clause and RETURNING clause.
    /// </summary>
    /// <param name="tableName">Physical table name.</param>
    /// <param name="setColumns">Column-to-parameter mappings for SET clause.</param>
    /// <param name="whereClause">WHERE clause (e.g., "id = @id").</param>
    /// <returns>UPDATE SQL with RETURNING *.</returns>
    public static string BuildUpdate(
        string tableName,
        IEnumerable<(string ColumnName, string ParameterName)> setColumns,
        string whereClause)
    {
        var sets = setColumns.ToList();

        if (sets.Count == 0)
            throw new ArgumentException("At least one column is required", nameof(setColumns));

        var sb = new StringBuilder();
        sb.Append("UPDATE ");
        sb.Append(QuoteIdentifier(tableName));
        sb.Append(" SET ");
        sb.Append(string.Join(", ", sets.Select(s => $"{QuoteIdentifier(s.ColumnName)} = {s.ParameterName}")));
        sb.Append(" WHERE ");
        sb.Append(whereClause);
        sb.Append(" RETURNING *");

        return sb.ToString();
    }

    /// <summary>
    /// Builds a batch UPDATE statement that updates multiple rows matching a condition.
    /// </summary>
    /// <param name="tableName">Physical table name.</param>
    /// <param name="setColumns">Column-to-parameter mappings for SET clause.</param>
    /// <param name="whereClause">WHERE clause.</param>
    /// <returns>UPDATE SQL returning affected row count.</returns>
    public static string BuildBatchUpdate(
        string tableName,
        IEnumerable<(string ColumnName, string ParameterName)> setColumns,
        string whereClause)
    {
        var sets = setColumns.ToList();

        if (sets.Count == 0)
            throw new ArgumentException("At least one column is required", nameof(setColumns));

        var sb = new StringBuilder();
        sb.Append("UPDATE ");
        sb.Append(QuoteIdentifier(tableName));
        sb.Append(" SET ");
        sb.Append(string.Join(", ", sets.Select(s => $"{QuoteIdentifier(s.ColumnName)} = {s.ParameterName}")));
        sb.Append(" WHERE ");
        sb.Append(whereClause);

        return sb.ToString();
    }

    /// <summary>
    /// Builds a DELETE statement with WHERE clause.
    /// </summary>
    /// <param name="tableName">Physical table name.</param>
    /// <param name="whereClause">WHERE clause (e.g., "id = @id").</param>
    /// <returns>DELETE SQL.</returns>
    public static string BuildDelete(string tableName, string whereClause)
    {
        var sb = new StringBuilder();
        sb.Append("DELETE FROM ");
        sb.Append(QuoteIdentifier(tableName));
        sb.Append(" WHERE ");
        sb.Append(whereClause);

        return sb.ToString();
    }

    /// <summary>
    /// Builds a SELECT statement by ID.
    /// </summary>
    /// <param name="tableName">Physical table name.</param>
    /// <param name="idColumnName">Physical ID column name.</param>
    /// <param name="columnNames">Columns to select (null for *).</param>
    /// <returns>SELECT SQL.</returns>
    public static string BuildSelectById(string tableName, string idColumnName, IEnumerable<string>? columnNames = null)
    {
        var sb = new StringBuilder();
        sb.Append("SELECT ");

        var columns = columnNames?.ToList();
        if (columns is null || columns.Count == 0)
        {
            sb.Append('*');
        }
        else
        {
            sb.Append(string.Join(", ", columns.Select(QuoteIdentifier)));
        }

        sb.Append(" FROM ");
        sb.Append(QuoteIdentifier(tableName));
        sb.Append(" WHERE ");
        sb.Append(QuoteIdentifier(idColumnName));
        sb.Append(" = @id");

        return sb.ToString();
    }

    /// <summary>
    /// Builds an UPSERT (INSERT ... ON CONFLICT UPDATE) statement.
    /// </summary>
    /// <param name="tableName">Physical table name.</param>
    /// <param name="columnNames">Physical column names.</param>
    /// <param name="parameterNames">Parameter placeholder names.</param>
    /// <param name="keyColumnNames">Conflict target column names.</param>
    /// <returns>UPSERT SQL with RETURNING *.</returns>
    public static string BuildUpsert(
        string tableName,
        IEnumerable<string> columnNames,
        IEnumerable<string> parameterNames,
        IEnumerable<string> keyColumnNames)
    {
        var columns = columnNames.ToList();
        var parameters = parameterNames.ToList();
        var keyColumns = keyColumnNames.ToList();

        if (columns.Count == 0)
            throw new ArgumentException("At least one column is required", nameof(columnNames));

        if (columns.Count != parameters.Count)
            throw new ArgumentException("Column count must match parameter count", nameof(parameterNames));

        if (keyColumns.Count == 0)
            throw new ArgumentException("At least one key column is required", nameof(keyColumnNames));

        // Non-key columns for update
        var updateColumns = columns.Except(keyColumns).ToList();

        var sb = new StringBuilder();
        sb.Append("INSERT INTO ");
        sb.Append(QuoteIdentifier(tableName));
        sb.Append(" (");
        sb.Append(string.Join(", ", columns.Select(QuoteIdentifier)));
        sb.Append(") VALUES (");
        sb.Append(string.Join(", ", parameters));
        sb.Append(") ON CONFLICT (");
        sb.Append(string.Join(", ", keyColumns.Select(QuoteIdentifier)));
        sb.Append(") DO UPDATE SET ");

        if (updateColumns.Count > 0)
        {
            sb.Append(string.Join(", ", updateColumns.Select(c => QuoteIdentifier(c) + " = EXCLUDED." + QuoteIdentifier(c))));
        }
        else
        {
            // If all columns are keys, just do a no-op update
            var keyCol = QuoteIdentifier(keyColumns[0]);
            sb.Append(keyCol);
            sb.Append(" = EXCLUDED.");
            sb.Append(keyCol);
        }

        sb.Append(" RETURNING *");

        return sb.ToString();
    }

    /// <summary>
    /// Builds a WHERE clause for a single ID column.
    /// </summary>
    public static string BuildIdWhereClause(string idColumnName, string parameterName = "@id")
    {
        return $"{QuoteIdentifier(idColumnName)} = {parameterName}";
    }

    /// <summary>
    /// Quotes a PostgreSQL identifier to handle special characters.
    /// </summary>
    public static string QuoteIdentifier(string identifier)
    {
        return $"\"{identifier.Replace("\"", "\"\"")}\"";
    }
}
