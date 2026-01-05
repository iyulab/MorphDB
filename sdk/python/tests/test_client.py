"""Tests for MorphDBClient."""

from unittest.mock import AsyncMock, patch

import pytest

from morphdb.client import MorphDBClient
from morphdb.data import DataClient
from morphdb.schema import SchemaClient
from morphdb.bulk import BulkClient
from morphdb.webhook import WebhookClient
from morphdb.realtime import RealtimeClient


class TestMorphDBClient:
    """Test cases for MorphDBClient."""

    @pytest.fixture
    def client(self, base_url: str, tenant_id: str, api_key: str) -> MorphDBClient:
        """Create a MorphDBClient."""
        return MorphDBClient(
            base_url=base_url,
            tenant_id=tenant_id,
            api_key=api_key,
        )

    def test_client_initialization(
        self,
        base_url: str,
        tenant_id: str,
        api_key: str,
    ) -> None:
        """Test client initialization."""
        client = MorphDBClient(
            base_url=base_url,
            tenant_id=tenant_id,
            api_key=api_key,
        )

        assert client is not None
        assert isinstance(client.schema, SchemaClient)
        assert isinstance(client.data, DataClient)
        assert isinstance(client.webhooks, WebhookClient)
        assert isinstance(client.bulk, BulkClient)
        assert isinstance(client.realtime, RealtimeClient)

    def test_client_initialization_with_jwt(
        self,
        base_url: str,
        tenant_id: str,
    ) -> None:
        """Test client initialization with JWT token."""
        jwt_token = "eyJ0eXAiOiJKV1QiLCJhbGciOiJIUzI1NiJ9..."

        client = MorphDBClient(
            base_url=base_url,
            tenant_id=tenant_id,
            jwt_token=jwt_token,
        )

        assert client is not None
        assert client._http.jwt_token == jwt_token

    def test_client_initialization_with_custom_options(
        self,
        base_url: str,
        tenant_id: str,
        api_key: str,
    ) -> None:
        """Test client initialization with custom options."""
        client = MorphDBClient(
            base_url=base_url,
            tenant_id=tenant_id,
            api_key=api_key,
            timeout=60.0,
            retry_count=5,
            retry_delay=2.0,
        )

        assert client._http.timeout == 60.0
        assert client._http.retry_count == 5
        assert client._http.retry_delay == 2.0

    def test_set_tenant_id(self, client: MorphDBClient) -> None:
        """Test setting tenant ID."""
        new_tenant_id = "new-tenant-id"

        client.set_tenant_id(new_tenant_id)

        assert client._http.tenant_id == new_tenant_id

    def test_set_api_key(self, client: MorphDBClient) -> None:
        """Test setting API key."""
        new_api_key = "new-api-key"

        client.set_api_key(new_api_key)

        assert client._http.api_key == new_api_key

    def test_set_jwt_token(self, client: MorphDBClient) -> None:
        """Test setting JWT token."""
        jwt_token = "new-jwt-token"

        client.set_jwt_token(jwt_token)

        assert client._http.jwt_token == jwt_token

    @pytest.mark.asyncio
    async def test_disconnect(self, client: MorphDBClient) -> None:
        """Test disconnecting client."""
        client._realtime.disconnect = AsyncMock()
        client._http.close = AsyncMock()

        await client.disconnect()

        client._realtime.disconnect.assert_called_once()
        client._http.close.assert_called_once()

    @pytest.mark.asyncio
    async def test_close(self, client: MorphDBClient) -> None:
        """Test closing client."""
        client._realtime.disconnect = AsyncMock()
        client._http.close = AsyncMock()

        await client.close()

        client._realtime.disconnect.assert_called_once()
        client._http.close.assert_called_once()

    @pytest.mark.asyncio
    async def test_async_context_manager(
        self,
        base_url: str,
        tenant_id: str,
        api_key: str,
    ) -> None:
        """Test async context manager."""
        with patch.object(MorphDBClient, "close", new_callable=AsyncMock) as mock_close:
            async with MorphDBClient(
                base_url=base_url,
                tenant_id=tenant_id,
                api_key=api_key,
            ) as client:
                assert client is not None
                assert isinstance(client, MorphDBClient)

            mock_close.assert_called_once()

    def test_base_url_trailing_slash_removed(
        self,
        tenant_id: str,
        api_key: str,
    ) -> None:
        """Test that trailing slash is removed from base URL."""
        client = MorphDBClient(
            base_url="http://localhost:5000/",
            tenant_id=tenant_id,
            api_key=api_key,
        )

        assert client._http.base_url == "http://localhost:5000"


class TestMorphDBClientProperties:
    """Test MorphDBClient property accessors."""

    @pytest.fixture
    def client(self, base_url: str, tenant_id: str, api_key: str) -> MorphDBClient:
        """Create a MorphDBClient."""
        return MorphDBClient(
            base_url=base_url,
            tenant_id=tenant_id,
            api_key=api_key,
        )

    def test_schema_property(self, client: MorphDBClient) -> None:
        """Test schema property returns SchemaClient."""
        schema = client.schema
        assert isinstance(schema, SchemaClient)
        # Same instance on multiple accesses
        assert client.schema is schema

    def test_data_property(self, client: MorphDBClient) -> None:
        """Test data property returns DataClient."""
        data = client.data
        assert isinstance(data, DataClient)
        assert client.data is data

    def test_webhooks_property(self, client: MorphDBClient) -> None:
        """Test webhooks property returns WebhookClient."""
        webhooks = client.webhooks
        assert isinstance(webhooks, WebhookClient)
        assert client.webhooks is webhooks

    def test_bulk_property(self, client: MorphDBClient) -> None:
        """Test bulk property returns BulkClient."""
        bulk = client.bulk
        assert isinstance(bulk, BulkClient)
        assert client.bulk is bulk

    def test_realtime_property(self, client: MorphDBClient) -> None:
        """Test realtime property returns RealtimeClient."""
        realtime = client.realtime
        assert isinstance(realtime, RealtimeClient)
        assert client.realtime is realtime
