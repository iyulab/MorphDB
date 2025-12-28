/**
 * Base MorphDB error class
 */
export class MorphDBError extends Error {
  constructor(message: string) {
    super(message);
    this.name = 'MorphDBError';
  }
}

/**
 * API error with HTTP status code
 */
export class MorphDBApiError extends MorphDBError {
  public readonly statusCode: number;
  public readonly errorCode?: string;
  public readonly responseBody?: string;

  constructor(
    message: string,
    statusCode: number,
    errorCode?: string,
    responseBody?: string
  ) {
    super(message);
    this.name = 'MorphDBApiError';
    this.statusCode = statusCode;
    this.errorCode = errorCode;
    this.responseBody = responseBody;
  }
}

/**
 * Resource not found error
 */
export class MorphDBNotFoundError extends MorphDBApiError {
  constructor(message: string, responseBody?: string) {
    super(message, 404, 'NOT_FOUND', responseBody);
    this.name = 'MorphDBNotFoundError';
  }
}

/**
 * Validation error
 */
export class MorphDBValidationError extends MorphDBApiError {
  public readonly errors: Record<string, string[]>;

  constructor(
    message: string,
    errors?: Record<string, string[]>,
    responseBody?: string
  ) {
    super(message, 400, 'VALIDATION_ERROR', responseBody);
    this.name = 'MorphDBValidationError';
    this.errors = errors ?? {};
  }
}

/**
 * Authentication error
 */
export class MorphDBAuthenticationError extends MorphDBApiError {
  constructor(message: string, responseBody?: string) {
    super(message, 401, 'UNAUTHORIZED', responseBody);
    this.name = 'MorphDBAuthenticationError';
  }
}

/**
 * Authorization error
 */
export class MorphDBAuthorizationError extends MorphDBApiError {
  constructor(message: string, responseBody?: string) {
    super(message, 403, 'FORBIDDEN', responseBody);
    this.name = 'MorphDBAuthorizationError';
  }
}

/**
 * Conflict error
 */
export class MorphDBConflictError extends MorphDBApiError {
  constructor(message: string, responseBody?: string) {
    super(message, 409, 'CONFLICT', responseBody);
    this.name = 'MorphDBConflictError';
  }
}

/**
 * Connection error
 */
export class MorphDBConnectionError extends MorphDBError {
  constructor(message: string) {
    super(message);
    this.name = 'MorphDBConnectionError';
  }
}
