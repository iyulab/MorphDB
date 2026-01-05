"""Tests for SchemaClient."""

from typing import Any
from unittest.mock import AsyncMock

import pytest

from morphdb.http import HttpClient
from morphdb.models import (
    AddColumnRequest,
    AlterColumnRequest,
    CreateColumnRequest,
    CreateTableRequest,
    TableInfo,
)
from morphdb.schema import SchemaClient


class TestSchemaClient:
    """Test cases for SchemaClient."""

    @pytest.fixture
    def schema_client(self, mock_http_client: HttpClient) -> SchemaClient:
        """Create a SchemaClient with mocked HTTP client."""
        return SchemaClient(mock_http_client)

    @pytest.mark.asyncio
    async def test_get_tables(
        self,
        schema_client: SchemaClient,
        mock_http_client: HttpClient,
        sample_table_response: dict[str, Any],
    ) -> None:
        """Test getting all tables."""
        mock_http_client.get = AsyncMock(return_value=[sample_table_response])

        tables = await schema_client.get_tables()

        assert len(tables) == 1
        assert isinstance(tables[0], TableInfo)
        assert tables[0].name == "users"
        mock_http_client.get.assert_called_once_with("/api/schema/tables")

    @pytest.mark.asyncio
    async def test_get_tables_empty(
        self,
        schema_client: SchemaClient,
        mock_http_client: HttpClient,
    ) -> None:
        """Test getting tables when none exist."""
        mock_http_client.get = AsyncMock(return_value=[])

        tables = await schema_client.get_tables()

        assert len(tables) == 0
        mock_http_client.get.assert_called_once_with("/api/schema/tables")

    @pytest.mark.asyncio
    async def test_get_table(
        self,
        schema_client: SchemaClient,
        mock_http_client: HttpClient,
        sample_table_response: dict[str, Any],
    ) -> None:
        """Test getting a single table by name."""
        mock_http_client.get = AsyncMock(return_value=sample_table_response)

        table = await schema_client.get_table("users")

        assert isinstance(table, TableInfo)
        assert table.name == "users"
        assert len(table.columns) == 3
        mock_http_client.get.assert_called_once_with("/api/schema/tables/users")

    @pytest.mark.asyncio
    async def test_get_table_columns_parsed(
        self,
        schema_client: SchemaClient,
        mock_http_client: HttpClient,
        sample_table_response: dict[str, Any],
    ) -> None:
        """Test that table columns are correctly parsed."""
        mock_http_client.get = AsyncMock(return_value=sample_table_response)

        table = await schema_client.get_table("users")

        id_col = table.columns[0]
        assert id_col.name == "_id"
        assert id_col.data_type == "uuid"
        assert id_col.primary_key is True
        assert id_col.nullable is False

        name_col = table.columns[1]
        assert name_col.name == "name"
        assert name_col.data_type == "text"
        assert name_col.nullable is False

        email_col = table.columns[2]
        assert email_col.name == "email"
        assert email_col.unique is True

    @pytest.mark.asyncio
    async def test_create_table(
        self,
        schema_client: SchemaClient,
        mock_http_client: HttpClient,
        sample_table_response: dict[str, Any],
    ) -> None:
        """Test creating a new table."""
        mock_http_client.post = AsyncMock(return_value=sample_table_response)

        request = CreateTableRequest(
            name="users",
            columns=[
                CreateColumnRequest(name="name", type="text", nullable=False),
                CreateColumnRequest(name="email", type="text", unique=True),
            ],
        )

        table = await schema_client.create_table(request)

        assert isinstance(table, TableInfo)
        assert table.name == "users"
        mock_http_client.post.assert_called_once()
        call_args = mock_http_client.post.call_args
        assert call_args[0][0] == "/api/schema/tables"

    @pytest.mark.asyncio
    async def test_create_table_with_description(
        self,
        schema_client: SchemaClient,
        mock_http_client: HttpClient,
        sample_table_response: dict[str, Any],
    ) -> None:
        """Test creating a table with description."""
        mock_http_client.post = AsyncMock(return_value=sample_table_response)

        request = CreateTableRequest(
            name="users",
            columns=[CreateColumnRequest(name="name", type="text")],
            description="User accounts table",
        )

        await schema_client.create_table(request)

        call_args = mock_http_client.post.call_args
        body = call_args[0][1]
        assert body["description"] == "User accounts table"

    @pytest.mark.asyncio
    async def test_drop_table(
        self,
        schema_client: SchemaClient,
        mock_http_client: HttpClient,
    ) -> None:
        """Test dropping a table."""
        mock_http_client.delete = AsyncMock(return_value=None)

        await schema_client.drop_table("users")

        mock_http_client.delete.assert_called_once_with("/api/schema/tables/users")

    @pytest.mark.asyncio
    async def test_add_column(
        self,
        schema_client: SchemaClient,
        mock_http_client: HttpClient,
        sample_table_response: dict[str, Any],
    ) -> None:
        """Test adding a column to a table."""
        mock_http_client.post = AsyncMock(return_value=sample_table_response)

        request = AddColumnRequest(
            name="age",
            type="integer",
            nullable=True,
            default_value="0",
        )

        table = await schema_client.add_column("users", request)

        assert isinstance(table, TableInfo)
        mock_http_client.post.assert_called_once()
        call_args = mock_http_client.post.call_args
        assert call_args[0][0] == "/api/schema/tables/users/columns"

    @pytest.mark.asyncio
    async def test_alter_column_rename(
        self,
        schema_client: SchemaClient,
        mock_http_client: HttpClient,
        sample_table_response: dict[str, Any],
    ) -> None:
        """Test renaming a column."""
        mock_http_client.patch = AsyncMock(return_value=sample_table_response)

        request = AlterColumnRequest(new_name="full_name")

        await schema_client.alter_column("users", "name", request)

        mock_http_client.patch.assert_called_once()
        call_args = mock_http_client.patch.call_args
        assert call_args[0][0] == "/api/schema/tables/users/columns/name"
        body = call_args[0][1]
        assert body["newName"] == "full_name"

    @pytest.mark.asyncio
    async def test_alter_column_change_type(
        self,
        schema_client: SchemaClient,
        mock_http_client: HttpClient,
        sample_table_response: dict[str, Any],
    ) -> None:
        """Test changing column type."""
        mock_http_client.patch = AsyncMock(return_value=sample_table_response)

        request = AlterColumnRequest(new_type="varchar(100)")

        await schema_client.alter_column("users", "name", request)

        call_args = mock_http_client.patch.call_args
        body = call_args[0][1]
        assert body["newType"] == "varchar(100)"

    @pytest.mark.asyncio
    async def test_alter_column_nullable(
        self,
        schema_client: SchemaClient,
        mock_http_client: HttpClient,
        sample_table_response: dict[str, Any],
    ) -> None:
        """Test changing column nullability."""
        mock_http_client.patch = AsyncMock(return_value=sample_table_response)

        request = AlterColumnRequest(nullable=True)

        await schema_client.alter_column("users", "email", request)

        call_args = mock_http_client.patch.call_args
        body = call_args[0][1]
        assert body["nullable"] is True

    @pytest.mark.asyncio
    async def test_drop_column(
        self,
        schema_client: SchemaClient,
        mock_http_client: HttpClient,
        sample_table_response: dict[str, Any],
    ) -> None:
        """Test dropping a column."""
        mock_http_client.delete = AsyncMock(return_value=sample_table_response)

        await schema_client.drop_column("users", "email")

        mock_http_client.delete.assert_called_once_with(
            "/api/schema/tables/users/columns/email"
        )
