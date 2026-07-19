"""Tests for BatchClient.

The routes asserted here are the ones BatchController serves: ``[Route("api/batch")]`` with
``[HttpPost("data")]`` and ``[HttpPost("data/{table}/insert")]``.

The previous batch method targeted ``/api/data/{table}/batch``, which no controller serves — the
server answered 405 — and its request and response shapes matched no endpoint. Its tests asserted
only the mocked return value and never the request, so they passed while nothing worked.
"""

from typing import Any
from unittest.mock import AsyncMock
from uuid import uuid4

import pytest

from morphdb.batch import BatchClient
from morphdb.http import HttpClient
from morphdb.models import BatchOperation, BatchRequest, BatchResponse


class TestBatchClient:
    """Test cases for BatchClient."""

    @pytest.fixture
    def batch_client(self, mock_http_client: HttpClient) -> BatchClient:
        """Create a BatchClient with a mocked HTTP client."""
        return BatchClient(mock_http_client)

    @pytest.mark.asyncio
    async def test_insert_many_posts_to_the_route_the_server_serves(
        self,
        batch_client: BatchClient,
        mock_http_client: HttpClient,
        sample_batch_response: dict[str, Any],
    ) -> None:
        mock_http_client.post = AsyncMock(return_value=sample_batch_response)
        records = [{"name": "User 1"}, {"name": "User 2"}]

        result = await batch_client.insert_many("users", records)

        mock_http_client.post.assert_called_once_with("/api/batch/data/users/insert", records)
        assert isinstance(result, BatchResponse)
        assert result.success_count == 2
        assert result.failure_count == 0
        assert len(result.results) == 2

    @pytest.mark.asyncio
    async def test_insert_many_encodes_the_table_name(
        self,
        batch_client: BatchClient,
        mock_http_client: HttpClient,
        sample_batch_response: dict[str, Any],
    ) -> None:
        mock_http_client.post = AsyncMock(return_value=sample_batch_response)

        await batch_client.insert_many("my table", [])

        mock_http_client.post.assert_called_once_with("/api/batch/data/my%20table/insert", [])

    @pytest.mark.asyncio
    async def test_execute_posts_the_operations_to_the_route_the_server_serves(
        self,
        batch_client: BatchClient,
        mock_http_client: HttpClient,
        sample_batch_response: dict[str, Any],
    ) -> None:
        mock_http_client.post = AsyncMock(return_value=sample_batch_response)
        record_id = uuid4()
        request = BatchRequest(
            operations=[
                BatchOperation(method="INSERT", table="users", data={"name": "User 1"}),
                BatchOperation(method="DELETE", table="users", id=record_id),
            ]
        )

        result = await batch_client.execute(request)

        path, body = mock_http_client.post.call_args.args
        assert path == "/api/batch/data"
        assert body["operations"][0] == {
            "method": "INSERT",
            "table": "users",
            "data": {"name": "User 1"},
        }
        # Serialized for the wire — a raw UUID would not survive JSON encoding.
        assert body["operations"][1]["id"] == str(record_id)
        assert result.success_count == 2

    @pytest.mark.asyncio
    async def test_partial_failure_is_visible_per_operation(
        self,
        batch_client: BatchClient,
        mock_http_client: HttpClient,
    ) -> None:
        """A batch with failed operations still succeeds as a request, so the results carry it."""
        mock_http_client.post = AsyncMock(
            return_value={
                "results": [
                    {"index": 0, "success": True, "affectedRows": 1},
                    {"index": 1, "success": False, "error": "null value in column 'name'"},
                ],
                "successCount": 1,
                "failureCount": 1,
            }
        )

        result = await batch_client.insert_many("users", [{}, {}])

        assert result.failure_count == 1
        assert result.results[1].error == "null value in column 'name'"
