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
/// Thrown when a request does not say which project it applies to.
/// <para>
/// Every schema and data operation is scoped to a project, so this is not an authorization failure —
/// the request is simply incomplete. It exists as a type because the alternative was matching on the
/// text of an <see cref="InvalidOperationException"/>, which made the message part of the contract
/// and let unrelated exceptions carrying the same words fall into the same branch.
/// </para>
/// </summary>
public class MissingProjectException : MorphDbException
{
    public MissingProjectException()
        : base("MISSING_PROJECT", "This request must say which project it applies to. Send an X-Project-Id header.")
    {
    }
}

/// <summary>
/// Thrown when a request addresses a project that does not exist. Exists as a type for the same
/// reason as <see cref="MissingProjectException"/>: the alternative was branching on the
/// <c>ErrorCode</c> string, which made the code literal part of every catch site.
/// <para>
/// The message does not include <see cref="ProjectId"/> — a project id is an internal operating
/// detail this API's own docs say a caller must never forward from an end user, so it does not
/// belong in text a caller may show one. Unlike a table or column name, it names nothing a caller
/// chose; the code and status already say what was wrong, and <see cref="ProjectId"/> is there for
/// a caller (or a log) that needs the id itself.
/// </para>
/// </summary>
public class ProjectNotFoundException : MorphDbException
{
    public Guid ProjectId { get; }

    public ProjectNotFoundException(Guid projectId)
        : base("PROJECT_NOT_FOUND", "Project not found.")
    {
        ProjectId = projectId;
    }
}

/// <summary>
/// Thrown when a request names a project, but the value is not a GUID.
/// <para>
/// Distinct from <see cref="MissingProjectException"/> on purpose: that one says the request said
/// nothing about which project it means, this one says it said something and that something cannot
/// be a project id. Collapsing the two into the same "not present" case sends a caller who mistyped
/// their project id looking for a header they had already sent.
/// </para>
/// </summary>
public class MalformedProjectIdException : MorphDbException
{
    public string Value { get; }

    public MalformedProjectIdException(string value)
        : base("INVALID_PROJECT_ID", $"X-Project-Id must be a GUID; received '{value}'.")
    {
        Value = value;
    }
}

/// <summary>
/// Thrown when a project slug is already in use. A sibling of <see cref="DuplicateNameException"/>
/// with the project-specific code the API documents.
/// </summary>
public class DuplicateSlugException : MorphDbException
{
    public string Slug { get; }

    public DuplicateSlugException(string slug)
        : base("DUPLICATE_SLUG", $"Project slug '{slug}' is already in use.")
    {
        Slug = slug;
    }
}

/// <summary>
/// Thrown when a project is created under an id that is already taken.
/// <para>
/// Only a caller that chooses the id can reach this. It is a conflict rather than a failed insert
/// because a deployment that pins its project id will re-run the same creation request, and the
/// answer it needs is "that one already exists", not an internal error.
/// </para>
/// </summary>
public class DuplicateProjectIdException : MorphDbException
{
    public Guid ProjectId { get; }

    public DuplicateProjectIdException(Guid projectId)
        : base("DUPLICATE_PROJECT_ID", $"Project id '{projectId}' is already in use.")
    {
        ProjectId = projectId;
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
/// Thrown when a request presents no usable connection secret while authentication is enforced.
/// <para>
/// Distinct from <see cref="MissingProjectException"/> on purpose: that one says the request is
/// incomplete, this one says the caller is not recognized. Collapsing them would tell a caller
/// with a revoked secret to add a header.
/// </para>
/// </summary>
public class UnauthenticatedException : MorphDbException
{
    public UnauthenticatedException()
        : base("UNAUTHENTICATED", "This request presented no valid secret. Send an Authorization: Bearer <secret> header.")
    {
    }
}

/// <summary>
/// Thrown when a recognized secret is not allowed to do what the request asks.
/// </summary>
public class ForbiddenException : MorphDbException
{
    public ForbiddenException(string message)
        : base("FORBIDDEN", message)
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

