"""MorphDB SDK data client."""

from typing import Any
from uuid import UUID

from morphdb.http import HttpClient
from morphdb.models import (
    BatchRequest,
    BatchResponse,
    DataRecord,
    PagedResponse,
    QueryRequest,
)


class DataClient:
    """Client for data operations."""

    def __init__(self, http: HttpClient) -> None:
        self._http = http

    async def query(
        self,
        table_name: str,
        request: QueryRequest | None = None,
    ) -> PagedResponse:
        """Query records from a table."""
        body = {}
        if request:
            body = request.model_dump(by_alias=True, exclude_none=True)

        data = await self._http.post(f"/api/data/{table_name}/query", body)
        return PagedResponse.model_validate(data)

    async def get_by_id(self, table_name: str, record_id: UUID | str) -> DataRecord:
        """Get a record by ID."""
        data = await self._http.get(f"/api/data/{table_name}/{record_id}")
        return DataRecord.model_validate(data)

    async def insert(
        self,
        table_name: str,
        record: dict[str, Any],
    ) -> DataRecord:
        """Insert a new record."""
        data = await self._http.post(f"/api/data/{table_name}", record)
        return DataRecord.model_validate(data)

    async def update(
        self,
        table_name: str,
        record_id: UUID | str,
        record: dict[str, Any],
    ) -> DataRecord:
        """Update an existing record."""
        data = await self._http.put(f"/api/data/{table_name}/{record_id}", record)
        return DataRecord.model_validate(data)

    async def delete(self, table_name: str, record_id: UUID | str) -> None:
        """Delete a record."""
        await self._http.delete(f"/api/data/{table_name}/{record_id}")

    async def batch(
        self,
        table_name: str,
        request: BatchRequest,
    ) -> BatchResponse:
        """Execute batch operations."""
        # Convert UUIDs to strings in delete list
        body = request.model_dump(by_alias=True, exclude_none=True)
        if "deletes" in body and body["deletes"]:
            body["deletes"] = [str(uid) for uid in body["deletes"]]

        data = await self._http.post(f"/api/data/{table_name}/batch", body)
        return BatchResponse.model_validate(data)

    async def insert_many(
        self,
        table_name: str,
        records: list[dict[str, Any]],
    ) -> list[DataRecord]:
        """Insert multiple records."""
        request = BatchRequest(inserts=records)
        response = await self.batch(table_name, request)
        return response.inserted

    async def delete_many(
        self,
        table_name: str,
        record_ids: list[UUID | str],
    ) -> int:
        """Delete multiple records."""
        uuids = [UUID(str(rid)) if not isinstance(rid, UUID) else rid for rid in record_ids]
        request = BatchRequest(deletes=uuids)
        response = await self.batch(table_name, request)
        return response.deleted
