namespace MorphDB.Core.Exceptions;

/// <summary>
/// Base exception for all MorphDB errors.
/// </summary>
public class MorphDbException : Exception
{
    public string ErrorCode { get; }

    public MorphDbException(string errorCode, string message)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    public MorphDbException(string errorCode, string message, Exception innerException)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }
}

/// <summary>
/// Thrown when a schema operation fails.
/// </summary>
public class SchemaException : MorphDbException
{
    public SchemaException(string errorCode, string message)
        : base(errorCode, message) { }

    public SchemaException(string errorCode, string message, Exception innerException)
        : base(errorCode, message, innerException) { }
}

/// <summary>
/// Thrown when a table is not found.
/// </summary>
public class TableNotFoundException : SchemaException
{
    public string TableName { get; }

    public TableNotFoundException(string tableName)
        : base("TABLE_NOT_FOUND", $"Table '{tableName}' not found.")
    {
        TableName = tableName;
    }
}

/// <summary>
/// Thrown when a column is not found.
/// </summary>
public class ColumnNotFoundException : SchemaException
{
    public string TableName { get; }
    public string ColumnName { get; }

    public ColumnNotFoundException(string tableName, string columnName)
        : base("COLUMN_NOT_FOUND", $"Column '{columnName}' not found in table '{tableName}'.")
    {
        TableName = tableName;
        ColumnName = columnName;
    }
}

/// <summary>
/// Thrown when a schema version conflict occurs (optimistic locking).
/// </summary>
public class SchemaVersionConflictException : SchemaException
{
    public int ExpectedVersion { get; }
    public int ActualVersion { get; }

    public SchemaVersionConflictException(int expectedVersion, int actualVersion)
        : base("SCHEMA_VERSION_CONFLICT",
            $"Schema version conflict. Expected {expectedVersion}, but found {actualVersion}.")
    {
        ExpectedVersion = expectedVersion;
        ActualVersion = actualVersion;
    }
}

/// <summary>
/// Thrown when a duplicate name is detected.
/// </summary>
public class DuplicateNameException : SchemaException
{
    public string Name { get; }
    public string EntityType { get; }

    public DuplicateNameException(string entityType, string name)
        : base("DUPLICATE_NAME", $"{entityType} with name '{name}' already exists.")
    {
        EntityType = entityType;
        Name = name;
    }
}

/// <summary>
/// Thrown when a DDL lock cannot be acquired.
/// </summary>
public class LockAcquisitionException : MorphDbException
{
    public string Resource { get; }
    public TimeSpan Timeout { get; }

    public LockAcquisitionException(string resource, TimeSpan timeout)
        : base("LOCK_ACQUISITION_FAILED",
            $"Failed to acquire lock on '{resource}' within {timeout.TotalSeconds} seconds.")
    {
        Resource = resource;
        Timeout = timeout;
    }
}

/// <summary>
/// Thrown when data validation fails.
/// </summary>
public class DataValidationException : MorphDbException
{
    public IReadOnlyList<ValidationError> Errors { get; }

    public DataValidationException(IReadOnlyList<ValidationError> errors)
        : base("VALIDATION_FAILED", "Data validation failed.")
    {
        Errors = errors;
    }
}

/// <summary>
/// Represents a single validation error.
/// </summary>
public sealed record ValidationError(string Field, string Message, string ErrorCode);

/// <summary>
/// Thrown when a circular reference is detected.
/// </summary>
public class CircularReferenceException : SchemaException
{
    public IReadOnlyList<string> Path { get; }

    public CircularReferenceException(IReadOnlyList<string> path)
        : base("CIRCULAR_REFERENCE",
            $"Circular reference detected: {string.Join(" -> ", path)}")
    {
        Path = path;
    }
}

/// <summary>
/// Thrown when tenant isolation is violated.
/// </summary>
public class TenantIsolationException : MorphDbException
{
    public TenantIsolationException()
        : base("TENANT_ISOLATION_VIOLATION", "Access denied: tenant isolation violation.")
    {
    }
}

/// <summary>
/// Thrown when a resource is not found.
/// </summary>
public class NotFoundException : MorphDbException
{
    public NotFoundException(string message)
        : base("NOT_FOUND", message)
    {
    }

    public NotFoundException(string resourceType, string identifier)
        : base("NOT_FOUND", $"{resourceType} '{identifier}' not found.")
    {
    }
}

/// <summary>
/// Thrown when input validation fails.
/// </summary>
public class ValidationException : MorphDbException
{
    public ValidationException(string message)
        : base("VALIDATION_ERROR", message)
    {
    }

    public ValidationException(string field, string message)
        : base("VALIDATION_ERROR", $"Validation error for '{field}': {message}")
    {
    }
}
