# morphdb

Official Python client SDK for MorphDB - a PostgreSQL-based dynamic schema database service.

## Installation

```bash
pip install morphdb
# or
poetry add morphdb
# or
uv add morphdb
```

## Quick Start

```python
import asyncio
from morphdb import (
    MorphDBClient,
    CreateTableRequest,
    CreateColumnRequest,
    QueryRequest,
    Filter,
    FilterOperator,
)

async def main():
    # Create client
    async with MorphDBClient(
        base_url="http://localhost:5000",
        tenant_id="your-tenant-id",
        api_key="your-api-key",
    ) as client:
        # Schema Management
        await client.schema.create_table(CreateTableRequest(
            name="users",
            columns=[
                CreateColumnRequest(name="name", type="text"),
                CreateColumnRequest(name="email", type="text", unique=True),
                CreateColumnRequest(name="age", type="integer"),
            ],
        ))

        # Data Operations
        user = await client.data.insert("users", {
            "name": "John Doe",
            "email": "john@example.com",
            "age": 30,
        })

        # Query with filters
        result = await client.data.query("users", QueryRequest(
            filters=[Filter(column="age", operator=FilterOperator.GTE, value=18)],
            page_size=10,
        ))

        for record in result.data:
            print(f"User: {record.data['name']}")

asyncio.run(main())
```

## Features

- **Schema Management**: Create, alter, and drop tables dynamically
- **Data Operations**: CRUD operations with filtering, pagination, and ordering
- **Real-time Subscriptions**: SignalR-based change notifications
- **Bulk Operations**: Import/export data in CSV, JSON, and XLSX formats
- **Webhooks**: Manage webhook subscriptions for event notifications
- **Async/Await**: Full async support with httpx and signalrcore
- **Type-safe API**: Full type hints with Pydantic models

## API Reference

### Schema Client

```python
# Get all tables
tables = await client.schema.get_tables()

# Get a table by name
table = await client.schema.get_table("users")

# Create a table
new_table = await client.schema.create_table(CreateTableRequest(
    name="products",
    columns=[
        CreateColumnRequest(name="name", type="text", nullable=False),
        CreateColumnRequest(name="price", type="decimal"),
    ],
))

# Add a column
await client.schema.add_column("products", AddColumnRequest(
    name="description",
    type="text",
))

# Alter a column
await client.schema.alter_column("products", "description", AlterColumnRequest(
    nullable=False,
))

# Drop a column
await client.schema.drop_column("products", "description")

# Drop a table
await client.schema.drop_table("products")
```

### Data Client

```python
# Query records
result = await client.data.query("users", QueryRequest(
    select=["name", "email"],
    filters=[
        Filter(column="age", operator=FilterOperator.GTE, value=18),
        Filter(column="is_active", operator=FilterOperator.EQ, value=True),
    ],
    order_by=[OrderBy(column="created_at", ascending=False)],
    page=1,
    page_size=20,
))

# Get by ID
user = await client.data.get_by_id("users", "uuid-here")

# Insert
new_user = await client.data.insert("users", {
    "name": "Jane Doe",
    "email": "jane@example.com",
})

# Update
updated = await client.data.update("users", "uuid-here", {
    "name": "Jane Smith",
})

# Delete
await client.data.delete("users", "uuid-here")

# Batch operations
result = await client.data.batch("users", BatchRequest(
    inserts=[{"name": "User 1"}, {"name": "User 2"}],
    updates=[{"id": "uuid-1", "name": "Updated User"}],
    deletes=["uuid-to-delete"],
))

# Convenience methods
users = await client.data.insert_many("users", [
    {"name": "User 1"},
    {"name": "User 2"},
])

deleted_count = await client.data.delete_many("users", ["uuid-1", "uuid-2"])
```

### Bulk Client

```python
# Import CSV
with open("data.csv", "rb") as f:
    import_job = await client.bulk.import_csv(
        "users",
        f.read(),
        options=CsvImportOptions(
            delimiter=",",
            has_header=True,
        ),
    )

# Check import status
status = await client.bulk.get_import_status(import_job.job_id)
print(f"Progress: {status.percent_complete}%")

# Export to CSV
export_job = await client.bulk.export_csv("users", CsvExportOptions(
    columns=["name", "email"],
    filter="is_active:eq:true",
))

# Download export
file_bytes = await client.bulk.download_export(export_job.job_id)
with open("export.csv", "wb") as f:
    f.write(file_bytes)
```

### Webhook Client

```python
# Create webhook
webhook = await client.webhooks.create(CreateWebhookRequest(
    name="user-changes",
    table_name="users",
    url="https://your-server.com/webhook",
    events=["insert", "update", "delete"],
))

# Get deliveries
deliveries = await client.webhooks.get_deliveries(webhook.webhook_id)

# Retry failed delivery
await client.webhooks.retry_delivery(delivery_id)

# Activate/Deactivate
await client.webhooks.deactivate(webhook.webhook_id)
await client.webhooks.activate(webhook.webhook_id)
```

### Realtime Client

```python
# Subscribe to table changes
def on_change(notification):
    print(f"{notification.operation}: {notification.record_id}")
    print(f"Data: {notification.data}")

subscription = await client.realtime.subscribe(
    "users",
    on_change,
    SubscriptionOptions(filter="is_active:eq:true"),
)

# Keep connection alive...
await asyncio.sleep(60)

# Unsubscribe
await subscription.unsubscribe()

# Disconnect all subscriptions
await client.disconnect()
```

## Error Handling

```python
from morphdb import (
    MorphDBError,
    MorphDBNotFoundError,
    MorphDBValidationError,
    MorphDBAuthenticationError,
)

try:
    await client.data.get_by_id("users", "non-existent")
except MorphDBNotFoundError as e:
    print(f"User not found: {e.message}")
except MorphDBValidationError as e:
    print(f"Validation errors: {e.errors}")
except MorphDBAuthenticationError as e:
    print("Authentication required")
except MorphDBError as e:
    print(f"MorphDB error: {e.message}")
```

## Configuration

```python
client = MorphDBClient(
    base_url="http://localhost:5000",
    tenant_id="your-tenant-id",
    api_key="your-api-key",
    jwt_token="optional-jwt-token",
    timeout=30.0,  # 30 seconds
    retry_count=3,
    retry_delay=1.0,  # 1 second
)

# Update credentials at runtime
client.set_tenant_id("new-tenant-id")
client.set_api_key("new-api-key")
client.set_jwt_token("new-jwt-token")
```

## Requirements

- Python 3.10+
- httpx
- pydantic
- signalrcore

## License

MIT License
