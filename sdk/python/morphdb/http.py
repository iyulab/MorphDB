"""MorphDB SDK HTTP client utilities."""

import asyncio
from typing import Any

import httpx

from morphdb.exceptions import (
    MorphDBApiError,
    MorphDBAuthenticationError,
    MorphDBAuthorizationError,
    MorphDBConflictError,
    MorphDBConnectionError,
    MorphDBNotFoundError,
    MorphDBValidationError,
)


class HttpClient:
    """HTTP client with retry logic and error handling."""

    def __init__(
        self,
        base_url: str,
        project_id: str,
        api_key: str | None = None,
        jwt_token: str | None = None,
        timeout: float = 30.0,
        retry_count: int = 3,
        retry_delay: float = 1.0,
    ) -> None:
        self.base_url = base_url.rstrip("/")
        self.project_id = project_id
        self.api_key = api_key
        self.jwt_token = jwt_token
        self.timeout = timeout
        self.retry_count = retry_count
        self.retry_delay = retry_delay
        self._client: httpx.AsyncClient | None = None

    def _get_headers(self) -> dict[str, str]:
        """Get request headers."""
        headers = {
            "Content-Type": "application/json",
            "X-Project-Id": self.project_id,
        }
        if self.api_key:
            headers["X-API-Key"] = self.api_key
        if self.jwt_token:
            headers["Authorization"] = f"Bearer {self.jwt_token}"
        return headers

    async def _get_client(self) -> httpx.AsyncClient:
        """Get or create the HTTP client."""
        if self._client is None or self._client.is_closed:
            self._client = httpx.AsyncClient(
                base_url=self.base_url,
                headers=self._get_headers(),
                timeout=self.timeout,
            )
        return self._client

    async def close(self) -> None:
        """Close the HTTP client."""
        if self._client is not None and not self._client.is_closed:
            await self._client.aclose()
            self._client = None

    def set_project_id(self, project_id: str) -> None:
        """Update project ID."""
        self.project_id = project_id

    def set_api_key(self, api_key: str) -> None:
        """Update API key."""
        self.api_key = api_key

    def set_jwt_token(self, jwt_token: str) -> None:
        """Update JWT token."""
        self.jwt_token = jwt_token

    async def _handle_response(self, response: httpx.Response) -> Any:
        """Handle HTTP response and raise appropriate exceptions."""
        if response.status_code >= 200 and response.status_code < 300:
            if response.status_code == 204:
                return None
            try:
                return response.json()
            except Exception:
                return response.text

        response_body = response.text
        error_message = f"HTTP {response.status_code}"

        try:
            error_data = response.json()
            if isinstance(error_data, dict):
                error_message = error_data.get("message", error_message)
                error_code = error_data.get("code")
                errors = error_data.get("errors")
        except Exception:
            error_code = None
            errors = None

        if response.status_code == 400:
            raise MorphDBValidationError(error_message, errors, response_body)
        elif response.status_code == 401:
            raise MorphDBAuthenticationError(error_message, response_body)
        elif response.status_code == 403:
            raise MorphDBAuthorizationError(error_message, response_body)
        elif response.status_code == 404:
            raise MorphDBNotFoundError(error_message, response_body)
        elif response.status_code == 409:
            raise MorphDBConflictError(error_message, response_body)
        else:
            raise MorphDBApiError(error_message, response.status_code, error_code, response_body)

    async def _request_with_retry(
        self,
        method: str,
        path: str,
        **kwargs: Any,
    ) -> Any:
        """Execute request with retry logic."""
        client = await self._get_client()
        last_exception: Exception | None = None

        for attempt in range(self.retry_count):
            try:
                # Update headers for each request in case credentials changed
                headers = kwargs.pop("headers", {})
                headers.update(self._get_headers())
                kwargs["headers"] = headers

                response = await client.request(method, path, **kwargs)
                return await self._handle_response(response)
            except (httpx.ConnectError, httpx.TimeoutException) as e:
                last_exception = MorphDBConnectionError(str(e))
                if attempt < self.retry_count - 1:
                    await asyncio.sleep(self.retry_delay * (attempt + 1))
            except MorphDBApiError:
                raise
            except Exception as e:
                raise MorphDBConnectionError(str(e)) from e

        if last_exception:
            raise last_exception
        raise MorphDBConnectionError("Request failed after retries")

    async def get(self, path: str, params: dict[str, Any] | None = None) -> Any:
        """Execute GET request."""
        return await self._request_with_retry("GET", path, params=params)

    async def post(
        self,
        path: str,
        data: dict[str, Any] | None = None,
        params: dict[str, Any] | None = None,
    ) -> Any:
        """Execute POST request."""
        return await self._request_with_retry("POST", path, json=data, params=params)

    async def put(
        self,
        path: str,
        data: dict[str, Any] | None = None,
        params: dict[str, Any] | None = None,
    ) -> Any:
        """Execute PUT request."""
        return await self._request_with_retry("PUT", path, json=data, params=params)

    async def patch(
        self,
        path: str,
        data: dict[str, Any] | None = None,
        params: dict[str, Any] | None = None,
    ) -> Any:
        """Execute PATCH request."""
        return await self._request_with_retry("PATCH", path, json=data, params=params)

    async def delete(self, path: str, params: dict[str, Any] | None = None) -> Any:
        """Execute DELETE request."""
        return await self._request_with_retry("DELETE", path, params=params)

    async def post_file(
        self,
        path: str,
        file_content: bytes,
        filename: str,
        content_type: str = "application/octet-stream",
        params: dict[str, Any] | None = None,
    ) -> Any:
        """Execute POST request with file upload."""
        files = {"file": (filename, file_content, content_type)}
        client = await self._get_client()

        headers = self._get_headers()
        # Remove Content-Type for multipart form data
        headers.pop("Content-Type", None)

        response = await client.post(path, files=files, params=params, headers=headers)
        return await self._handle_response(response)

    async def get_bytes(self, path: str, params: dict[str, Any] | None = None) -> bytes:
        """Execute GET request and return raw bytes."""
        client = await self._get_client()
        response = await client.get(path, params=params, headers=self._get_headers())

        if response.status_code >= 200 and response.status_code < 300:
            return response.content

        await self._handle_response(response)
        return b""  # Never reached, but satisfies type checker
