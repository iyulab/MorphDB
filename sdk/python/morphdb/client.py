"""MorphDB Python SDK client."""

from morphdb.batch import BatchClient
from morphdb.bulk import BulkClient
from morphdb.data import DataClient
from morphdb.http import HttpClient
from morphdb.realtime import RealtimeClient
from morphdb.schema import SchemaClient
from morphdb.webhook import WebhookClient


class MorphDBClient:
    """
    MorphDB Python SDK client.

    Provides access to all MorphDB API functionality through specialized sub-clients:
    - schema: Table and column management
    - data: CRUD and query operations
    - webhooks: Webhook management
    - bulk: Import/export operations
    - realtime: Real-time subscriptions

    Example:
        async with MorphDBClient(
            base_url="http://localhost:5000",
            project_id="your-project-id",
        ) as client:
            # Create a table
            await client.schema.create_table(CreateTableRequest(
                name="users",
                columns=[
                    CreateColumnRequest(name="name", type="text"),
                    CreateColumnRequest(name="email", type="text", unique=True),
                ],
            ))

            # Insert data
            user = await client.data.insert("users", {
                "name": "John Doe",
                "email": "john@example.com",
            })

            # Query data
            result = await client.data.query("users", QueryRequest(
                filters=[Filter(column="name", operator=FilterOperator.CONTAINS, value="John")],
            ))
    """

    def __init__(
        self,
        base_url: str,
        project_id: str,
        timeout: float = 30.0,
        retry_count: int = 3,
        retry_delay: float = 1.0,
    ) -> None:
        """
        Initialize the MorphDB client.

        Args:
            base_url: The base URL of the MorphDB API server.
            project_id: The project ID for multi-project isolation.
            timeout: Request timeout in seconds (default: 30).
            retry_count: Number of retry attempts for failed requests (default: 3).
            retry_delay: Base delay between retries in seconds (default: 1).
        """
        self._http = HttpClient(
            base_url=base_url,
            project_id=project_id,
            timeout=timeout,
            retry_count=retry_count,
            retry_delay=retry_delay,
        )

        self._realtime = RealtimeClient(
            base_url=base_url,
            project_id=project_id,
        )

        # Initialize sub-clients
        self._schema = SchemaClient(self._http)
        self._data = DataClient(self._http)
        self._batch = BatchClient(self._http)
        self._webhooks = WebhookClient(self._http)
        self._bulk = BulkClient(self._http)

    @property
    def schema(self) -> SchemaClient:
        """Get the schema client for table management."""
        return self._schema

    @property
    def data(self) -> DataClient:
        """Get the data client for CRUD operations."""
        return self._data

    @property
    def batch(self) -> BatchClient:
        """Get the batch client for many-writes-in-one-request operations."""
        return self._batch

    @property
    def webhooks(self) -> WebhookClient:
        """Get the webhook client for webhook management."""
        return self._webhooks

    @property
    def bulk(self) -> BulkClient:
        """Get the bulk client for import/export operations."""
        return self._bulk

    @property
    def realtime(self) -> RealtimeClient:
        """Get the realtime client for subscriptions."""
        return self._realtime

    def set_project_id(self, project_id: str) -> None:
        """Update the project ID."""
        self._http.set_project_id(project_id)


    async def disconnect(self) -> None:
        """Disconnect all connections."""
        await self._realtime.disconnect()
        await self._http.close()

    async def close(self) -> None:
        """Close the client and release resources."""
        await self.disconnect()

    async def __aenter__(self) -> "MorphDBClient":
        """Async context manager entry."""
        return self

    async def __aexit__(self, exc_type: type | None, exc_val: Exception | None, exc_tb: object) -> None:
        """Async context manager exit."""
        await self.close()
