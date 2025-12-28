"""MorphDB SDK schema client."""

from morphdb.http import HttpClient
from morphdb.models import (
    AddColumnRequest,
    AlterColumnRequest,
    CreateTableRequest,
    TableInfo,
)


class SchemaClient:
    """Client for schema management operations."""

    def __init__(self, http: HttpClient) -> None:
        self._http = http

    async def get_tables(self) -> list[TableInfo]:
        """Get all tables."""
        data = await self._http.get("/api/schema/tables")
        return [TableInfo.model_validate(item) for item in data]

    async def get_table(self, table_name: str) -> TableInfo:
        """Get a table by name."""
        data = await self._http.get(f"/api/schema/tables/{table_name}")
        return TableInfo.model_validate(data)

    async def create_table(self, request: CreateTableRequest) -> TableInfo:
        """Create a new table."""
        data = await self._http.post(
            "/api/schema/tables",
            request.model_dump(by_alias=True, exclude_none=True),
        )
        return TableInfo.model_validate(data)

    async def drop_table(self, table_name: str) -> None:
        """Drop a table."""
        await self._http.delete(f"/api/schema/tables/{table_name}")

    async def add_column(self, table_name: str, request: AddColumnRequest) -> TableInfo:
        """Add a column to a table."""
        data = await self._http.post(
            f"/api/schema/tables/{table_name}/columns",
            request.model_dump(by_alias=True, exclude_none=True),
        )
        return TableInfo.model_validate(data)

    async def alter_column(
        self,
        table_name: str,
        column_name: str,
        request: AlterColumnRequest,
    ) -> TableInfo:
        """Alter a column in a table."""
        data = await self._http.patch(
            f"/api/schema/tables/{table_name}/columns/{column_name}",
            request.model_dump(by_alias=True, exclude_none=True),
        )
        return TableInfo.model_validate(data)

    async def drop_column(self, table_name: str, column_name: str) -> TableInfo:
        """Drop a column from a table."""
        data = await self._http.delete(
            f"/api/schema/tables/{table_name}/columns/{column_name}"
        )
        return TableInfo.model_validate(data)
