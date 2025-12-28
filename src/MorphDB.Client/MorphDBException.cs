using System.Net;

namespace MorphDB.Client;

/// <summary>
/// Base exception for MorphDB client errors.
/// </summary>
public class MorphDBException : Exception
{
    /// <summary>
    /// Creates a new MorphDB exception.
    /// </summary>
    public MorphDBException(string message) : base(message) { }

    /// <summary>
    /// Creates a new MorphDB exception with an inner exception.
    /// </summary>
    public MorphDBException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Exception for HTTP API errors.
/// </summary>
public class MorphDBApiException : MorphDBException
{
    /// <summary>
    /// HTTP status code.
    /// </summary>
    public HttpStatusCode StatusCode { get; }

    /// <summary>
    /// Error code from the API.
    /// </summary>
    public string? ErrorCode { get; }

    /// <summary>
    /// Response body.
    /// </summary>
    public string? ResponseBody { get; }

    /// <summary>
    /// Creates a new API exception.
    /// </summary>
    public MorphDBApiException(
        string message,
        HttpStatusCode statusCode,
        string? errorCode = null,
        string? responseBody = null)
        : base(message)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
        ResponseBody = responseBody;
    }
}

/// <summary>
/// Exception when a resource is not found.
/// </summary>
public class MorphDBNotFoundException : MorphDBApiException
{
    /// <summary>
    /// Creates a new not found exception.
    /// </summary>
    public MorphDBNotFoundException(string message, string? responseBody = null)
        : base(message, HttpStatusCode.NotFound, "NOT_FOUND", responseBody) { }
}

/// <summary>
/// Exception for validation errors.
/// </summary>
public class MorphDBValidationException : MorphDBApiException
{
    /// <summary>
    /// Validation errors.
    /// </summary>
    public IReadOnlyDictionary<string, string[]> Errors { get; }

    /// <summary>
    /// Creates a new validation exception.
    /// </summary>
    public MorphDBValidationException(
        string message,
        IReadOnlyDictionary<string, string[]>? errors = null,
        string? responseBody = null)
        : base(message, HttpStatusCode.BadRequest, "VALIDATION_ERROR", responseBody)
    {
        Errors = errors ?? new Dictionary<string, string[]>();
    }
}

/// <summary>
/// Exception for authentication errors.
/// </summary>
public class MorphDBAuthenticationException : MorphDBApiException
{
    /// <summary>
    /// Creates a new authentication exception.
    /// </summary>
    public MorphDBAuthenticationException(string message, string? responseBody = null)
        : base(message, HttpStatusCode.Unauthorized, "UNAUTHORIZED", responseBody) { }
}

/// <summary>
/// Exception for authorization errors.
/// </summary>
public class MorphDBAuthorizationException : MorphDBApiException
{
    /// <summary>
    /// Creates a new authorization exception.
    /// </summary>
    public MorphDBAuthorizationException(string message, string? responseBody = null)
        : base(message, HttpStatusCode.Forbidden, "FORBIDDEN", responseBody) { }
}

/// <summary>
/// Exception for conflict errors.
/// </summary>
public class MorphDBConflictException : MorphDBApiException
{
    /// <summary>
    /// Creates a new conflict exception.
    /// </summary>
    public MorphDBConflictException(string message, string? responseBody = null)
        : base(message, HttpStatusCode.Conflict, "CONFLICT", responseBody) { }
}

/// <summary>
/// Exception for connection errors.
/// </summary>
public class MorphDBConnectionException : MorphDBException
{
    /// <summary>
    /// Creates a new connection exception.
    /// </summary>
    public MorphDBConnectionException(string message)
        : base(message) { }

    /// <summary>
    /// Creates a new connection exception with an inner exception.
    /// </summary>
    public MorphDBConnectionException(string message, Exception innerException)
        : base(message, innerException) { }
}
