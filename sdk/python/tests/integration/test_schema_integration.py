"""Integration tests for MorphDB Python SDK schema operations.

These tests require a running MorphDB server.
Start the test server with: docker compose -f docker-compose.test.yml up -d
"""

import pytest

from morphdb import MorphDBClient
from morphdb.exceptions import MorphDBError
from morphdb.models import CreateTableRequest, CreateColumnRequest, AddColumnRequest


pytestmark = pytest.mark.integration


class TestSchemaOperations:
    """Test schema operations against a real server."""

    async def test_create_and_get_table(
        self,
        client: MorphDBClient,
        unique_table_name: str,
    ) -> None:
        """Test creating a table and retrieving it."""
        # Create table
        request = CreateTableRequest(
            name=unique_table_name,
            columns=[
                CreateColumnRequest(name="name", type="text", nullable=False),
                CreateColumnRequest(name="email", type="text", unique=True),
                CreateColumnRequest(name="age", type="integer", nullable=True),
            ],
            description="Test table for integration tests",
        )

        table = await client.schema.create_table(request)

        # Verify created table
        assert table.name == unique_table_name
        assert len(table.columns) >= 4  # 3 user columns + _id at minimum
        assert table.schema_version == 1

        # Find user-defined columns (exclude system columns)
        user_columns = {col.name: col for col in table.columns if not col.name.startswith("_")}
        assert "name" in user_columns
        assert "email" in user_columns
        assert "age" in user_columns

        # Verify column properties
        assert not user_columns["name"].nullable
        assert user_columns["email"].unique
        assert user_columns["age"].nullable

        # Get table
        retrieved = await client.schema.get_table(unique_table_name)
        assert retrieved.name == unique_table_name
        assert retrieved.table_id == table.table_id

        # Cleanup
        await client.schema.drop_table(unique_table_name)

    async def test_list_tables(
        self,
        client: MorphDBClient,
        unique_table_name: str,
    ) -> None:
        """Test listing all tables."""
        # Create a test table
        request = CreateTableRequest(
            name=unique_table_name,
            columns=[
                CreateColumnRequest(name="value", type="text"),
            ],
        )
        await client.schema.create_table(request)

        # List tables
        tables = await client.schema.get_tables()
        assert len(tables) >= 1

        table_names = [t.name for t in tables]
        assert unique_table_name in table_names

        # Cleanup
        await client.schema.drop_table(unique_table_name)

    async def test_add_column(
        self,
        client: MorphDBClient,
        unique_table_name: str,
    ) -> None:
        """Test adding a column to an existing table."""
        # Create table
        request = CreateTableRequest(
            name=unique_table_name,
            columns=[
                CreateColumnRequest(name="name", type="text"),
            ],
        )
        await client.schema.create_table(request)

        # Add column
        add_request = AddColumnRequest(
            name="status",
            type="text",
            nullable=True,
            default_value="'active'",
        )
        updated = await client.schema.add_column(unique_table_name, add_request)

        # Verify column was added
        column_names = [col.name for col in updated.columns]
        assert "status" in column_names

        # Cleanup
        await client.schema.drop_table(unique_table_name)

    async def test_drop_column(
        self,
        client: MorphDBClient,
        unique_table_name: str,
    ) -> None:
        """Test dropping a column from a table."""
        # Create table with multiple columns
        request = CreateTableRequest(
            name=unique_table_name,
            columns=[
                CreateColumnRequest(name="name", type="text"),
                CreateColumnRequest(name="temp_column", type="text"),
            ],
        )
        await client.schema.create_table(request)

        # Drop the temporary column
        updated = await client.schema.drop_column(unique_table_name, "temp_column")

        # Verify column was dropped
        column_names = [col.name for col in updated.columns]
        assert "temp_column" not in column_names
        assert "name" in column_names

        # Cleanup
        await client.schema.drop_table(unique_table_name)

    async def test_drop_table(
        self,
        client: MorphDBClient,
        unique_table_name: str,
    ) -> None:
        """Test dropping a table."""
        # Create table
        request = CreateTableRequest(
            name=unique_table_name,
            columns=[
                CreateColumnRequest(name="value", type="text"),
            ],
        )
        await client.schema.create_table(request)

        # Drop table
        await client.schema.drop_table(unique_table_name)

        # Verify table was dropped
        with pytest.raises(MorphDBError):
            await client.schema.get_table(unique_table_name)

    async def test_create_table_with_all_column_types(
        self,
        client: MorphDBClient,
        unique_table_name: str,
    ) -> None:
        """Test creating a table with various column types."""
        request = CreateTableRequest(
            name=unique_table_name,
            columns=[
                CreateColumnRequest(name="text_col", type="text"),
                CreateColumnRequest(name="int_col", type="integer"),
                CreateColumnRequest(name="bigint_col", type="bigint"),
                CreateColumnRequest(name="decimal_col", type="decimal"),
                CreateColumnRequest(name="bool_col", type="boolean"),
                CreateColumnRequest(name="date_col", type="date"),
                CreateColumnRequest(name="timestamp_col", type="timestamp"),
                CreateColumnRequest(name="json_col", type="jsonb"),
                CreateColumnRequest(name="uuid_col", type="uuid"),
            ],
        )

        table = await client.schema.create_table(request)

        # Verify all columns exist
        user_columns = {col.name: col for col in table.columns if not col.name.startswith("_")}
        expected_columns = [
            "text_col", "int_col", "bigint_col", "decimal_col",
            "bool_col", "date_col", "timestamp_col", "json_col", "uuid_col"
        ]

        for col_name in expected_columns:
            assert col_name in user_columns, f"Column {col_name} not found"

        # Cleanup
        await client.schema.drop_table(unique_table_name)
