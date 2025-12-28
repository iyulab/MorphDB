"""MorphDB SDK webhook client."""

from uuid import UUID

from morphdb.http import HttpClient
from morphdb.models import CreateWebhookRequest, WebhookDelivery, WebhookInfo


class WebhookClient:
    """Client for webhook management operations."""

    def __init__(self, http: HttpClient) -> None:
        self._http = http

    async def get_webhooks(self) -> list[WebhookInfo]:
        """Get all webhooks."""
        data = await self._http.get("/api/webhooks")
        return [WebhookInfo.model_validate(item) for item in data]

    async def get_webhook(self, webhook_id: UUID | str) -> WebhookInfo:
        """Get a webhook by ID."""
        data = await self._http.get(f"/api/webhooks/{webhook_id}")
        return WebhookInfo.model_validate(data)

    async def create(self, request: CreateWebhookRequest) -> WebhookInfo:
        """Create a new webhook."""
        data = await self._http.post(
            "/api/webhooks",
            request.model_dump(by_alias=True, exclude_none=True),
        )
        return WebhookInfo.model_validate(data)

    async def update(
        self,
        webhook_id: UUID | str,
        request: CreateWebhookRequest,
    ) -> WebhookInfo:
        """Update a webhook."""
        data = await self._http.put(
            f"/api/webhooks/{webhook_id}",
            request.model_dump(by_alias=True, exclude_none=True),
        )
        return WebhookInfo.model_validate(data)

    async def delete(self, webhook_id: UUID | str) -> None:
        """Delete a webhook."""
        await self._http.delete(f"/api/webhooks/{webhook_id}")

    async def activate(self, webhook_id: UUID | str) -> WebhookInfo:
        """Activate a webhook."""
        data = await self._http.post(f"/api/webhooks/{webhook_id}/activate")
        return WebhookInfo.model_validate(data)

    async def deactivate(self, webhook_id: UUID | str) -> WebhookInfo:
        """Deactivate a webhook."""
        data = await self._http.post(f"/api/webhooks/{webhook_id}/deactivate")
        return WebhookInfo.model_validate(data)

    async def get_deliveries(
        self,
        webhook_id: UUID | str,
        page: int = 1,
        page_size: int = 50,
    ) -> list[WebhookDelivery]:
        """Get webhook deliveries."""
        data = await self._http.get(
            f"/api/webhooks/{webhook_id}/deliveries",
            params={"page": page, "pageSize": page_size},
        )
        # Handle paged response
        if isinstance(data, dict) and "data" in data:
            return [WebhookDelivery.model_validate(item) for item in data["data"]]
        return [WebhookDelivery.model_validate(item) for item in data]

    async def retry_delivery(self, delivery_id: UUID | str) -> WebhookDelivery:
        """Retry a failed delivery."""
        data = await self._http.post(f"/api/webhooks/deliveries/{delivery_id}/retry")
        return WebhookDelivery.model_validate(data)
