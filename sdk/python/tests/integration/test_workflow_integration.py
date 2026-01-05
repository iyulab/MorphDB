"""Integration tests for complete MorphDB SDK workflows.

These tests verify end-to-end scenarios combining multiple operations.
Requires a running MorphDB server.
"""

import pytest
from uuid import uuid4

from morphdb import MorphDBClient
from morphdb.models import (
    CreateTableRequest,
    CreateColumnRequest,
    QueryRequest,
    Filter,
    FilterOperator,
    OrderBy,
    AddColumnRequest,
)


pytestmark = pytest.mark.integration


class TestFullWorkflows:
    """Test complete end-to-end workflows."""

    async def test_complete_table_lifecycle(
        self,
        client: MorphDBClient,
        unique_table_name: str,
    ) -> None:
        """Test complete table lifecycle: create -> use -> modify -> drop."""
        # 1. Create table
        create_request = CreateTableRequest(
            name=unique_table_name,
            columns=[
                CreateColumnRequest(name="product_name", type="text", nullable=False),
                CreateColumnRequest(name="price", type="decimal", nullable=False),
                CreateColumnRequest(name="in_stock", type="boolean", nullable=True),
            ],
            description="Product catalog table",
        )

        table = await client.schema.create_table(create_request)
        assert table.name == unique_table_name
        initial_column_count = len(table.columns)

        # 2. Insert initial data
        products = [
            {"product_name": "Widget A", "price": 19.99, "in_stock": True},
            {"product_name": "Widget B", "price": 29.99, "in_stock": True},
            {"product_name": "Widget C", "price": 39.99, "in_stock": False},
        ]

        inserted_ids = []
        for product in products:
            record = await client.data.insert(unique_table_name, product)
            inserted_ids.append(record.id)
            assert record.data["product_name"] == product["product_name"]

        # 3. Query data
        query = QueryRequest(
            filters=[Filter(column="in_stock", operator=FilterOperator.EQ, value=True)],
            order_by=[OrderBy(column="price", ascending=True)],
        )
        result = await client.data.query(unique_table_name, query)

        assert result.pagination.total_count == 2
        assert result.data[0].data["product_name"] == "Widget A"
        assert result.data[1].data["product_name"] == "Widget B"

        # 4. Update data
        await client.data.update(unique_table_name, inserted_ids[2], {
            "in_stock": True,
            "price": 34.99,
        })

        # Verify update
        updated = await client.data.get_by_id(unique_table_name, inserted_ids[2])
        assert updated.data["in_stock"] is True
        assert float(updated.data["price"]) == 34.99

        # 5. Add a new column
        add_column = AddColumnRequest(
            name="category",
            type="text",
            nullable=True,
            default_value="'general'",
        )
        modified_table = await client.schema.add_column(unique_table_name, add_column)

        assert len(modified_table.columns) == initial_column_count + 1
        column_names = [col.name for col in modified_table.columns]
        assert "category" in column_names

        # 6. Insert with new column
        new_product = await client.data.insert(unique_table_name, {
            "product_name": "Widget D",
            "price": 49.99,
            "in_stock": True,
            "category": "premium",
        })
        assert new_product.data["category"] == "premium"

        # 7. Delete a record
        await client.data.delete(unique_table_name, inserted_ids[0])

        # Verify count
        all_query = QueryRequest()
        all_result = await client.data.query(unique_table_name, all_query)
        assert all_result.pagination.total_count == 3

        # 8. Drop table
        await client.schema.drop_table(unique_table_name)

        # Verify table is gone
        tables = await client.schema.get_tables()
        table_names = [t.name for t in tables]
        assert unique_table_name not in table_names

    async def test_multi_table_workflow(
        self,
        client: MorphDBClient,
    ) -> None:
        """Test workflow involving multiple related tables."""
        # Generate unique names
        categories_table = f"categories_{uuid4().hex[:8]}"
        products_table = f"products_{uuid4().hex[:8]}"

        try:
            # Create categories table
            await client.schema.create_table(CreateTableRequest(
                name=categories_table,
                columns=[
                    CreateColumnRequest(name="name", type="text", nullable=False, unique=True),
                    CreateColumnRequest(name="description", type="text", nullable=True),
                ],
            ))

            # Create products table
            await client.schema.create_table(CreateTableRequest(
                name=products_table,
                columns=[
                    CreateColumnRequest(name="name", type="text", nullable=False),
                    CreateColumnRequest(name="category_name", type="text", nullable=True),
                    CreateColumnRequest(name="price", type="decimal", nullable=False),
                ],
            ))

            # Insert categories
            electronics = await client.data.insert(categories_table, {
                "name": "Electronics",
                "description": "Electronic devices and accessories",
            })
            clothing = await client.data.insert(categories_table, {
                "name": "Clothing",
                "description": "Apparel and fashion items",
            })

            # Insert products
            products = [
                {"name": "Laptop", "category_name": "Electronics", "price": 999.99},
                {"name": "Phone", "category_name": "Electronics", "price": 699.99},
                {"name": "T-Shirt", "category_name": "Clothing", "price": 29.99},
                {"name": "Jeans", "category_name": "Clothing", "price": 59.99},
            ]

            for product in products:
                await client.data.insert(products_table, product)

            # Query products by category
            electronics_query = QueryRequest(
                filters=[Filter(column="category_name", operator=FilterOperator.EQ, value="Electronics")],
            )
            electronics_products = await client.data.query(products_table, electronics_query)
            assert electronics_products.pagination.total_count == 2

            # Query high-value products
            expensive_query = QueryRequest(
                filters=[Filter(column="price", operator=FilterOperator.GTE, value=100)],
            )
            expensive_products = await client.data.query(products_table, expensive_query)
            assert expensive_products.pagination.total_count == 2

            # Get all categories
            categories = await client.data.query(categories_table, QueryRequest())
            assert categories.pagination.total_count == 2

        finally:
            # Cleanup
            try:
                await client.schema.drop_table(products_table)
            except Exception:
                pass
            try:
                await client.schema.drop_table(categories_table)
            except Exception:
                pass

    async def test_large_dataset_operations(
        self,
        client: MorphDBClient,
        unique_table_name: str,
    ) -> None:
        """Test operations with a larger dataset."""
        # Create table
        await client.schema.create_table(CreateTableRequest(
            name=unique_table_name,
            columns=[
                CreateColumnRequest(name="index", type="integer", nullable=False),
                CreateColumnRequest(name="value", type="text", nullable=False),
                CreateColumnRequest(name="category", type="text", nullable=True),
            ],
        ))

        try:
            # Insert 100 records
            categories = ["A", "B", "C", "D", "E"]
            for i in range(100):
                await client.data.insert(unique_table_name, {
                    "index": i,
                    "value": f"Value {i}",
                    "category": categories[i % 5],
                })

            # Verify count
            query = QueryRequest()
            result = await client.data.query(unique_table_name, query)
            assert result.pagination.total_count == 100

            # Query by category
            for cat in categories:
                cat_query = QueryRequest(
                    filters=[Filter(column="category", operator=FilterOperator.EQ, value=cat)],
                )
                cat_result = await client.data.query(unique_table_name, cat_query)
                assert cat_result.pagination.total_count == 20

            # Query with range
            range_query = QueryRequest(
                filters=[
                    Filter(column="index", operator=FilterOperator.GTE, value=40),
                    Filter(column="index", operator=FilterOperator.LT, value=60),
                ],
            )
            range_result = await client.data.query(unique_table_name, range_query)
            assert range_result.pagination.total_count == 20

            # Pagination through all records
            all_records = []
            page = 1
            while True:
                page_query = QueryRequest(page=page, page_size=25)
                page_result = await client.data.query(unique_table_name, page_query)
                all_records.extend(page_result.data)
                if not page_result.pagination.has_next_page:
                    break
                page += 1

            assert len(all_records) == 100

        finally:
            await client.schema.drop_table(unique_table_name)
