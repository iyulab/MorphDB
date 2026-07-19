"""MorphDB SDK data client."""

from typing import Any
from uuid import UUID

from morphdb.batch import BatchClient
from morphdb.http import HttpClient
from morphdb.models import (
    BatchOperation,
    BatchRequest,
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

    async def insert_many(
        self,
        table_name: str,
        records: list[dict[str, Any]],
    ) -> int:
        """Insert multiple records, returning how many landed.

        Delegates to the batch endpoint; use ``client.batch`` directly for per-operation results.
        """
        response = await BatchClient(self._http).insert_many(table_name, records)
        return response.success_count

    async def delete_many(
        self,
        table_name: str,
        record_ids: list[UUID | str],
    ) -> int:
        """Delete multiple records by id, returning how many were deleted."""
        request = BatchRequest(
            operations=[
                BatchOperation(method="DELETE", table=table_name, id=UUID(str(rid)))
                for rid in record_ids
            ]
        )
        response = await BatchClient(self._http).execute(request)
        return response.success_count
