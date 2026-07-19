"""Batch data operations — many writes in one request."""

from typing import Any
from urllib.parse import quote

from morphdb.http import HttpClient
from morphdb.models import BatchRequest, BatchResponse


class BatchClient:
    """Client for batch data operations."""

    def __init__(self, http: HttpClient) -> None:
        self._http = http

    async def execute(self, request: BatchRequest) -> BatchResponse:
        """Execute a batch of operations in order.

        Each operation names its own table, so one batch may span tables. Operations are reported
        individually — inspect ``results`` for partial failures, since a batch containing failed
        operations still succeeds as a request.
        """
        body = request.model_dump(by_alias=True, exclude_none=True, mode="json")
        data = await self._http.post("/api/batch/data", body)
        return BatchResponse.model_validate(data)

    async def insert_many(
        self,
        table_name: str,
        records: list[dict[str, Any]],
    ) -> BatchResponse:
        """Insert many records into one table.

        Records without an ``_id`` are assigned one by the server.
        """
        data = await self._http.post(
            f"/api/batch/data/{quote(table_name, safe='')}/insert",
            records,
        )
        return BatchResponse.model_validate(data)
