"""Tests for WebhookClient."""

from typing import Any
from unittest.mock import AsyncMock
from uuid import uuid4

import pytest

from morphdb.http import HttpClient
from morphdb.models import CreateWebhookRequest, WebhookDelivery, WebhookInfo
from morphdb.webhook import WebhookClient


class TestWebhookClient:
    """Test cases for WebhookClient."""

    @pytest.fixture
    def webhook_client(self, mock_http_client: HttpClient) -> WebhookClient:
        """Create a WebhookClient with mocked HTTP client."""
        return WebhookClient(mock_http_client)

    @pytest.fixture
    def sample_delivery_response(self) -> dict[str, Any]:
        """Return a sample delivery response."""
        from datetime import datetime, timezone

        now = datetime.now(timezone.utc).isoformat()
        return {
            "deliveryId": str(uuid4()),
            "webhookId": str(uuid4()),
            "recordId": str(uuid4()),
            "event": "insert",
            "status": "delivered",
            "attemptCount": 1,
            "httpStatusCode": 200,
            "errorMessage": None,
            "createdAt": now,
            "deliveredAt": now,
        }

    @pytest.mark.asyncio
    async def test_get_webhooks(
        self,
        webhook_client: WebhookClient,
        mock_http_client: HttpClient,
        sample_webhook_response: dict[str, Any],
    ) -> None:
        """Test getting all webhooks."""
        mock_http_client.get = AsyncMock(return_value=[sample_webhook_response])

        webhooks = await webhook_client.get_webhooks()

        assert len(webhooks) == 1
        assert isinstance(webhooks[0], WebhookInfo)
        assert webhooks[0].name == "order-notifications"
        mock_http_client.get.assert_called_once_with("/api/webhooks")

    @pytest.mark.asyncio
    async def test_get_webhook(
        self,
        webhook_client: WebhookClient,
        mock_http_client: HttpClient,
        sample_webhook_response: dict[str, Any],
    ) -> None:
        """Test getting a single webhook."""
        mock_http_client.get = AsyncMock(return_value=sample_webhook_response)
        webhook_id = sample_webhook_response["webhookId"]

        webhook = await webhook_client.get_webhook(webhook_id)

        assert isinstance(webhook, WebhookInfo)
        assert webhook.table_name == "orders"
        mock_http_client.get.assert_called_once_with(f"/api/webhooks/{webhook_id}")

    @pytest.mark.asyncio
    async def test_create_webhook(
        self,
        webhook_client: WebhookClient,
        mock_http_client: HttpClient,
        sample_webhook_response: dict[str, Any],
    ) -> None:
        """Test creating a webhook."""
        mock_http_client.post = AsyncMock(return_value=sample_webhook_response)

        request = CreateWebhookRequest(
            name="order-notifications",
            table_name="orders",
            url="https://example.com/webhook",
            events=["insert", "update", "delete"],
        )
        webhook = await webhook_client.create(request)

        assert isinstance(webhook, WebhookInfo)
        assert webhook.is_active is True
        mock_http_client.post.assert_called_once()

    @pytest.mark.asyncio
    async def test_create_webhook_with_headers(
        self,
        webhook_client: WebhookClient,
        mock_http_client: HttpClient,
        sample_webhook_response: dict[str, Any],
    ) -> None:
        """Test creating a webhook with custom headers."""
        mock_http_client.post = AsyncMock(return_value=sample_webhook_response)

        request = CreateWebhookRequest(
            name="order-notifications",
            table_name="orders",
            url="https://example.com/webhook",
            headers={"Authorization": "Bearer token123"},
        )
        await webhook_client.create(request)

        call_args = mock_http_client.post.call_args
        body = call_args[0][1]
        assert body["headers"] == {"Authorization": "Bearer token123"}

    @pytest.mark.asyncio
    async def test_update_webhook(
        self,
        webhook_client: WebhookClient,
        mock_http_client: HttpClient,
        sample_webhook_response: dict[str, Any],
    ) -> None:
        """Test updating a webhook."""
        mock_http_client.put = AsyncMock(return_value=sample_webhook_response)
        webhook_id = sample_webhook_response["webhookId"]

        request = CreateWebhookRequest(
            name="updated-webhook",
            table_name="orders",
            url="https://new-url.com/webhook",
        )
        webhook = await webhook_client.update(webhook_id, request)

        assert isinstance(webhook, WebhookInfo)
        mock_http_client.put.assert_called_once()
        call_args = mock_http_client.put.call_args
        assert call_args[0][0] == f"/api/webhooks/{webhook_id}"

    @pytest.mark.asyncio
    async def test_delete_webhook(
        self,
        webhook_client: WebhookClient,
        mock_http_client: HttpClient,
    ) -> None:
        """Test deleting a webhook."""
        mock_http_client.delete = AsyncMock(return_value=None)
        webhook_id = str(uuid4())

        await webhook_client.delete(webhook_id)

        mock_http_client.delete.assert_called_once_with(f"/api/webhooks/{webhook_id}")

    @pytest.mark.asyncio
    async def test_activate_webhook(
        self,
        webhook_client: WebhookClient,
        mock_http_client: HttpClient,
        sample_webhook_response: dict[str, Any],
    ) -> None:
        """Test activating a webhook."""
        mock_http_client.post = AsyncMock(return_value=sample_webhook_response)
        webhook_id = sample_webhook_response["webhookId"]

        webhook = await webhook_client.activate(webhook_id)

        assert isinstance(webhook, WebhookInfo)
        mock_http_client.post.assert_called_once_with(
            f"/api/webhooks/{webhook_id}/activate"
        )

    @pytest.mark.asyncio
    async def test_deactivate_webhook(
        self,
        webhook_client: WebhookClient,
        mock_http_client: HttpClient,
        sample_webhook_response: dict[str, Any],
    ) -> None:
        """Test deactivating a webhook."""
        sample_webhook_response["isActive"] = False
        mock_http_client.post = AsyncMock(return_value=sample_webhook_response)
        webhook_id = sample_webhook_response["webhookId"]

        webhook = await webhook_client.deactivate(webhook_id)

        assert isinstance(webhook, WebhookInfo)
        assert webhook.is_active is False
        mock_http_client.post.assert_called_once_with(
            f"/api/webhooks/{webhook_id}/deactivate"
        )

    @pytest.mark.asyncio
    async def test_get_deliveries(
        self,
        webhook_client: WebhookClient,
        mock_http_client: HttpClient,
        sample_delivery_response: dict[str, Any],
    ) -> None:
        """Test getting webhook deliveries."""
        mock_http_client.get = AsyncMock(return_value=[sample_delivery_response])
        webhook_id = sample_delivery_response["webhookId"]

        deliveries = await webhook_client.get_deliveries(webhook_id)

        assert len(deliveries) == 1
        assert isinstance(deliveries[0], WebhookDelivery)
        assert deliveries[0].status == "delivered"

    @pytest.mark.asyncio
    async def test_get_deliveries_paged_response(
        self,
        webhook_client: WebhookClient,
        mock_http_client: HttpClient,
        sample_delivery_response: dict[str, Any],
    ) -> None:
        """Test getting webhook deliveries with paged response."""
        mock_http_client.get = AsyncMock(
            return_value={"data": [sample_delivery_response]}
        )
        webhook_id = sample_delivery_response["webhookId"]

        deliveries = await webhook_client.get_deliveries(webhook_id)

        assert len(deliveries) == 1
        assert isinstance(deliveries[0], WebhookDelivery)

    @pytest.mark.asyncio
    async def test_retry_delivery(
        self,
        webhook_client: WebhookClient,
        mock_http_client: HttpClient,
        sample_delivery_response: dict[str, Any],
    ) -> None:
        """Test retrying a failed delivery."""
        mock_http_client.post = AsyncMock(return_value=sample_delivery_response)
        delivery_id = sample_delivery_response["deliveryId"]

        delivery = await webhook_client.retry_delivery(delivery_id)

        assert isinstance(delivery, WebhookDelivery)
        mock_http_client.post.assert_called_once_with(
            f"/api/webhooks/deliveries/{delivery_id}/retry"
        )
