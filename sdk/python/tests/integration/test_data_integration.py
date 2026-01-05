"""Integration tests for MorphDB Python SDK data operations.

These tests require a running MorphDB server.
Start the test server with: docker compose -f docker-compose.test.yml up -d
"""

import pytest

from morphdb import MorphDBClient
from morphdb.exceptions import MorphDBError
from morphdb.models import (
    CreateTableRequest,
    CreateColumnRequest,
    QueryRequest,
    Filter,
    FilterOperator,
    OrderBy,
    BatchRequest,
)


pytestmark = pytest.mark.integration


class TestDataOperations:
    """Test data operations against a real server."""

    @pytest.fixture
    async def test_table(
        self,
        client: MorphDBClient,
        unique_table_name: str,
    ):
        """Create a test table for data operations."""
        request = CreateTableRequest(
            name=unique_table_name,
            columns=[
                CreateColumnRequest(name="name", type="text", nullable=False),
                CreateColumnRequest(name="email", type="text", unique=True),
                CreateColumnRequest(name="age", type="integer", nullable=True),
                CreateColumnRequest(name="active", type="boolean", nullable=True),
            ],
        )
        await client.schema.create_table(request)
        yield unique_table_name
        # Cleanup
        try:
            await client.schema.drop_table(unique_table_name)
        except MorphDBError:
            pass

    async def test_insert_and_get_by_id(
        self,
        client: MorphDBClient,
        test_table: str,
    ) -> None:
        """Test inserting a record and retrieving it by ID."""
        # Insert
        data = {
            "name": "John Doe",
            "email": "john@example.com",
            "age": 30,
            "active": True,
        }
        record = await client.data.insert(test_table, data)

        assert record.id is not None
        assert record.data["name"] == "John Doe"
        assert record.data["email"] == "john@example.com"
        assert record.data["age"] == 30
        assert record.data["active"] is True

        # Get by ID
        retrieved = await client.data.get_by_id(test_table, record.id)
        assert retrieved.id == record.id
        assert retrieved.data["name"] == "John Doe"

    async def test_query_with_filters(
        self,
        client: MorphDBClient,
        test_table: str,
    ) -> None:
        """Test querying with various filters."""
        # Insert test data
        users = [
            {"name": "Alice", "email": "alice@example.com", "age": 25, "active": True},
            {"name": "Bob", "email": "bob@example.com", "age": 35, "active": True},
            {"name": "Charlie", "email": "charlie@example.com", "age": 30, "active": False},
            {"name": "David", "email": "david@example.com", "age": 28, "active": True},
        ]

        for user in users:
            await client.data.insert(test_table, user)

        # Query with age filter
        query = QueryRequest(
            filters=[Filter(column="age", operator=FilterOperator.GTE, value=30)]
        )
        result = await client.data.query(test_table, query)

        assert result.pagination.total_count == 2
        names = [r.data["name"] for r in result.data]
        assert "Bob" in names
        assert "Charlie" in names

        # Query with boolean filter
        query = QueryRequest(
            filters=[Filter(column="active", operator=FilterOperator.EQ, value=True)]
        )
        result = await client.data.query(test_table, query)

        assert result.pagination.total_count == 3

        # Query with text contains filter
        query = QueryRequest(
            filters=[Filter(column="name", operator=FilterOperator.CONTAINS, value="li")]
        )
        result = await client.data.query(test_table, query)

        assert result.pagination.total_count == 2
        names = [r.data["name"] for r in result.data]
        assert "Alice" in names
        assert "Charlie" in names

    async def test_query_with_ordering(
        self,
        client: MorphDBClient,
        test_table: str,
    ) -> None:
        """Test querying with ordering."""
        # Insert test data
        users = [
            {"name": "Charlie", "email": "c@example.com", "age": 30},
            {"name": "Alice", "email": "a@example.com", "age": 25},
            {"name": "Bob", "email": "b@example.com", "age": 35},
        ]

        for user in users:
            await client.data.insert(test_table, user)

        # Query ordered by name ascending
        query = QueryRequest(
            order_by=[OrderBy(column="name", ascending=True)]
        )
        result = await client.data.query(test_table, query)

        names = [r.data["name"] for r in result.data]
        assert names == ["Alice", "Bob", "Charlie"]

        # Query ordered by age descending
        query = QueryRequest(
            order_by=[OrderBy(column="age", ascending=False)]
        )
        result = await client.data.query(test_table, query)

        ages = [r.data["age"] for r in result.data]
        assert ages == [35, 30, 25]

    async def test_query_with_pagination(
        self,
        client: MorphDBClient,
        test_table: str,
    ) -> None:
        """Test querying with pagination."""
        # Insert test data
        for i in range(15):
            await client.data.insert(test_table, {
                "name": f"User {i}",
                "email": f"user{i}@example.com",
                "age": 20 + i,
            })

        # Get first page
        query = QueryRequest(page=1, page_size=5)
        result = await client.data.query(test_table, query)

        assert len(result.data) == 5
        assert result.pagination.total_count == 15
        assert result.pagination.total_pages == 3
        assert result.pagination.has_next_page is True
        assert result.pagination.has_previous_page is False

        # Get second page
        query = QueryRequest(page=2, page_size=5)
        result = await client.data.query(test_table, query)

        assert len(result.data) == 5
        assert result.pagination.has_next_page is True
        assert result.pagination.has_previous_page is True

        # Get last page
        query = QueryRequest(page=3, page_size=5)
        result = await client.data.query(test_table, query)

        assert len(result.data) == 5
        assert result.pagination.has_next_page is False
        assert result.pagination.has_previous_page is True

    async def test_update_record(
        self,
        client: MorphDBClient,
        test_table: str,
    ) -> None:
        """Test updating a record."""
        # Insert
        record = await client.data.insert(test_table, {
            "name": "Original Name",
            "email": "original@example.com",
            "age": 25,
        })

        # Update
        updated = await client.data.update(test_table, record.id, {
            "name": "Updated Name",
            "age": 26,
        })

        assert updated.data["name"] == "Updated Name"
        assert updated.data["email"] == "original@example.com"  # Unchanged
        assert updated.data["age"] == 26

        # Verify update persisted
        retrieved = await client.data.get_by_id(test_table, record.id)
        assert retrieved.data["name"] == "Updated Name"

    async def test_delete_record(
        self,
        client: MorphDBClient,
        test_table: str,
    ) -> None:
        """Test deleting a record."""
        # Insert
        record = await client.data.insert(test_table, {
            "name": "To Delete",
            "email": "delete@example.com",
        })

        # Delete
        await client.data.delete(test_table, record.id)

        # Verify deletion
        with pytest.raises(MorphDBError):
            await client.data.get_by_id(test_table, record.id)

    async def test_batch_operations(
        self,
        client: MorphDBClient,
        test_table: str,
    ) -> None:
        """Test batch insert, update, and delete operations."""
        # Insert initial records for updating and deleting
        record1 = await client.data.insert(test_table, {
            "name": "Update Me",
            "email": "update@example.com",
        })
        record2 = await client.data.insert(test_table, {
            "name": "Delete Me",
            "email": "delete@example.com",
        })

        # Batch operation
        batch = BatchRequest(
            inserts=[
                {"name": "New User 1", "email": "new1@example.com", "age": 21},
                {"name": "New User 2", "email": "new2@example.com", "age": 22},
            ],
            updates=[
                {"_id": str(record1.id), "name": "Updated User", "age": 30},
            ],
            deletes=[record2.id],
        )

        result = await client.data.batch(test_table, batch)

        assert len(result.inserted) == 2
        assert len(result.updated) == 1
        assert result.deleted == 1

        # Verify inserts
        query = QueryRequest(
            filters=[Filter(column="name", operator=FilterOperator.STARTSWITH, value="New User")]
        )
        query_result = await client.data.query(test_table, query)
        assert query_result.pagination.total_count == 2

        # Verify update
        retrieved = await client.data.get_by_id(test_table, record1.id)
        assert retrieved.data["name"] == "Updated User"

        # Verify delete
        with pytest.raises(MorphDBError):
            await client.data.get_by_id(test_table, record2.id)

    async def test_query_with_select_columns(
        self,
        client: MorphDBClient,
        test_table: str,
    ) -> None:
        """Test querying with specific columns selected."""
        # Insert test data
        await client.data.insert(test_table, {
            "name": "Test User",
            "email": "test@example.com",
            "age": 25,
            "active": True,
        })

        # Query with specific columns
        query = QueryRequest(
            select=["name", "email"]
        )
        result = await client.data.query(test_table, query)

        assert len(result.data) == 1
        record = result.data[0]

        # Selected columns should be present
        assert "name" in record.data
        assert "email" in record.data
        # Non-selected columns may or may not be present depending on server implementation
