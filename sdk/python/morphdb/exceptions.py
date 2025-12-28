"""MorphDB SDK exceptions."""


class MorphDBError(Exception):
    """Base MorphDB error class."""

    def __init__(self, message: str) -> None:
        self.message = message
        super().__init__(message)


class MorphDBApiError(MorphDBError):
    """API error with HTTP status code."""

    def __init__(
        self,
        message: str,
        status_code: int,
        error_code: str | None = None,
        response_body: str | None = None,
    ) -> None:
        super().__init__(message)
        self.status_code = status_code
        self.error_code = error_code
        self.response_body = response_body


class MorphDBNotFoundError(MorphDBApiError):
    """Resource not found error."""

    def __init__(self, message: str, response_body: str | None = None) -> None:
        super().__init__(message, 404, "NOT_FOUND", response_body)


class MorphDBValidationError(MorphDBApiError):
    """Validation error."""

    def __init__(
        self,
        message: str,
        errors: dict[str, list[str]] | None = None,
        response_body: str | None = None,
    ) -> None:
        super().__init__(message, 400, "VALIDATION_ERROR", response_body)
        self.errors = errors or {}


class MorphDBAuthenticationError(MorphDBApiError):
    """Authentication error."""

    def __init__(self, message: str, response_body: str | None = None) -> None:
        super().__init__(message, 401, "UNAUTHORIZED", response_body)


class MorphDBAuthorizationError(MorphDBApiError):
    """Authorization error."""

    def __init__(self, message: str, response_body: str | None = None) -> None:
        super().__init__(message, 403, "FORBIDDEN", response_body)


class MorphDBConflictError(MorphDBApiError):
    """Conflict error."""

    def __init__(self, message: str, response_body: str | None = None) -> None:
        super().__init__(message, 409, "CONFLICT", response_body)


class MorphDBConnectionError(MorphDBError):
    """Connection error."""

    pass
