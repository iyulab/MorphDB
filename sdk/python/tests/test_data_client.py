"""Tests for DataClient."""

from typing import Any
from unittest.mock import AsyncMock
from uuid import uuid4

import pytest

from morphdb.data import DataClient
from morphdb.http import HttpClient
from morphdb.models import (
    BatchRequest,
    BatchResponse,
    DataRecord,
    Filter,
    FilterOperator,
    OrderBy,
    PagedResponse,
    QueryRequest,
)


class TestDataClient:
    """Test cases for DataClient."""

    @pytest.fixture
    def data_client(self, mock_http_client: HttpClient) -> DataClient:
        """Create a DataClient with mocked HTTP client."""
        return DataClient(mock_http_client)

    @pytest.mark.asyncio
    async def test_query_default(
        self,
        data_client: DataClient,
        mock_http_client: HttpClient,
        sample_paged_response: dict[str, Any],
    ) -> None:
        """Test querying with default parameters."""
        mock_http_client.post = AsyncMock(return_value=sample_paged_response)

        result = await data_client.query("users")

        assert isinstance(result, PagedResponse)
        assert len(result.data) == 1
        assert result.pagination.page == 1
        mock_http_client.post.assert_called_once_with("/api/data/users/query", {})

    @pytest.mark.asyncio
    async def test_query_with_filters(
        self,
        data_client: DataClient,
        mock_http_client: HttpClient,
        sample_paged_response: dict[str, Any],
    ) -> None:
        """Test querying with filters."""
        mock_http_client.post = AsyncMock(return_value=sample_paged_response)

        request = QueryRequest(
            filters=[
                Filter(column="name", operator=FilterOperator.CONTAINS, value="John"),
            ]
        )
        await data_client.query("users", request)

        call_args = mock_http_client.post.call_args
        body = call_args[0][1]
        assert "filters" in body
        assert len(body["filters"]) == 1

    @pytest.mark.asyncio
    async def test_query_with_pagination(
        self,
        data_client: DataClient,
        mock_http_client: HttpClient,
        sample_paged_response: dict[str, Any],
    ) -> None:
        """Test querying with pagination."""
        mock_http_client.post = AsyncMock(return_value=sample_paged_response)

        request = QueryRequest(page=2, page_size=25)
        await data_client.query("users", request)

        call_args = mock_http_client.post.call_args
        body = call_args[0][1]
        assert body["page"] == 2
        assert body["pageSize"] == 25

    @pytest.mark.asyncio
    async def test_query_with_order_by(
        self,
        data_client: DataClient,
        mock_http_client: HttpClient,
        sample_paged_response: dict[str, Any],
    ) -> None:
        """Test querying with order by."""
        mock_http_client.post = AsyncMock(return_value=sample_paged_response)

        request = QueryRequest(
            order_by=[
                OrderBy(column="name", ascending=True),
                OrderBy(column="email", ascending=False),
            ]
        )
        await data_client.query("users", request)

        call_args = mock_http_client.post.call_args
        body = call_args[0][1]
        assert "orderBy" in body
        assert len(body["orderBy"]) == 2

    @pytest.mark.asyncio
    async def test_query_with_select(
        self,
        data_client: DataClient,
        mock_http_client: HttpClient,
        sample_paged_response: dict[str, Any],
    ) -> None:
        """Test querying with column selection."""
        mock_http_client.post = AsyncMock(return_value=sample_paged_response)

        request = QueryRequest(select=["name", "email"])
        await data_client.query("users", request)

        call_args = mock_http_client.post.call_args
        body = call_args[0][1]
        assert body["select"] == ["name", "email"]

    @pytest.mark.asyncio
    async def test_get_by_id(
        self,
        data_client: DataClient,
        mock_http_client: HttpClient,
        sample_data_record: dict[str, Any],
    ) -> None:
        """Test getting a record by ID."""
        mock_http_client.get = AsyncMock(return_value=sample_data_record)
        record_id = sample_data_record["id"]

        result = await data_client.get_by_id("users", record_id)

        assert isinstance(result, DataRecord)
        assert str(result.id) == record_id
        mock_http_client.get.assert_called_once_with(f"/api/data/users/{record_id}")

    @pytest.mark.asyncio
    async def test_get_by_id_uuid_type(
        self,
        data_client: DataClient,
        mock_http_client: HttpClient,
        sample_data_record: dict[str, Any],
    ) -> None:
        """Test getting a record by UUID type."""
        mock_http_client.get = AsyncMock(return_value=sample_data_record)
        record_id = uuid4()

        await data_client.get_by_id("users", record_id)

        mock_http_client.get.assert_called_once_with(f"/api/data/users/{record_id}")

    @pytest.mark.asyncio
    async def test_insert(
        self,
        data_client: DataClient,
        mock_http_client: HttpClient,
        sample_data_record: dict[str, Any],
    ) -> None:
        """Test inserting a new record."""
        mock_http_client.post = AsyncMock(return_value=sample_data_record)

        data = {"name": "John Doe", "email": "john@example.com"}
        result = await data_client.insert("users", data)

        assert isinstance(result, DataRecord)
        assert result.data["name"] == "John Doe"
        mock_http_client.post.assert_called_once_with("/api/data/users", data)

    @pytest.mark.asyncio
    async def test_update(
        self,
        data_client: DataClient,
        mock_http_client: HttpClient,
        sample_data_record: dict[str, Any],
    ) -> None:
        """Test updating a record."""
        mock_http_client.put = AsyncMock(return_value=sample_data_record)
        record_id = sample_data_record["id"]

        data = {"name": "Jane Doe"}
        result = await data_client.update("users", record_id, data)

        assert isinstance(result, DataRecord)
        mock_http_client.put.assert_called_once_with(
            f"/api/data/users/{record_id}", data
        )

    @pytest.mark.asyncio
    async def test_delete(
        self,
        data_client: DataClient,
        mock_http_client: HttpClient,
    ) -> None:
        """Test deleting a record."""
        mock_http_client.delete = AsyncMock(return_value=None)
        record_id = str(uuid4())

        await data_client.delete("users", record_id)

        mock_http_client.delete.assert_called_once_with(f"/api/data/users/{record_id}")

    @pytest.mark.asyncio
    async def test_batch(
        self,
        data_client: DataClient,
        mock_http_client: HttpClient,
        sample_batch_response: dict[str, Any],
    ) -> None:
        """Test batch operations."""
        mock_http_client.post = AsyncMock(return_value=sample_batch_response)

        request = BatchRequest(
            inserts=[{"name": "User 1", "email": "user1@example.com"}],
            updates=[{"_id": str(uuid4()), "name": "Updated User"}],
            deletes=[uuid4(), uuid4()],
        )
        result = await data_client.batch("users", request)

        assert isinstance(result, BatchResponse)
        assert len(result.inserted) == 1
        assert len(result.updated) == 1
        assert result.deleted == 2

    @pytest.mark.asyncio
    async def test_insert_many(
        self,
        data_client: DataClient,
        mock_http_client: HttpClient,
        sample_batch_response: dict[str, Any],
    ) -> None:
        """Test inserting multiple records."""
        mock_http_client.post = AsyncMock(return_value=sample_batch_response)

        records = [
            {"name": "User 1", "email": "user1@example.com"},
            {"name": "User 2", "email": "user2@example.com"},
        ]
        result = await data_client.insert_many("users", records)

        assert isinstance(result, list)
        assert all(isinstance(r, DataRecord) for r in result)

    @pytest.mark.asyncio
    async def test_delete_many(
        self,
        data_client: DataClient,
        mock_http_client: HttpClient,
        sample_batch_response: dict[str, Any],
    ) -> None:
        """Test deleting multiple records."""
        mock_http_client.post = AsyncMock(return_value=sample_batch_response)

        record_ids = [uuid4(), uuid4()]
        result = await data_client.delete_many("users", record_ids)

        assert result == 2


class TestFilterOperator:
    """Test FilterOperator enum values."""

    def test_operators(self) -> None:
        """Test all filter operators are defined."""
        assert FilterOperator.EQ.value == "eq"
        assert FilterOperator.NEQ.value == "neq"
        assert FilterOperator.GT.value == "gt"
        assert FilterOperator.GTE.value == "gte"
        assert FilterOperator.LT.value == "lt"
        assert FilterOperator.LTE.value == "lte"
        assert FilterOperator.CONTAINS.value == "contains"
        assert FilterOperator.STARTSWITH.value == "startswith"
        assert FilterOperator.ENDSWITH.value == "endswith"
        assert FilterOperator.ISNULL.value == "isnull"
        assert FilterOperator.ISNOTNULL.value == "isnotnull"
        assert FilterOperator.IN.value == "in"
        assert FilterOperator.NOTIN.value == "notin"
