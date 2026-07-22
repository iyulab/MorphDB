"""MorphDB SDK real-time subscriptions client."""

import asyncio
from collections.abc import Awaitable, Callable
from typing import Any
from uuid import uuid4

from signalrcore.hub_connection_builder import HubConnectionBuilder

from morphdb.models import ChangeNotification, SubscriptionOptions


class Subscription:
    """Represents an active subscription."""

    def __init__(
        self,
        subscription_id: str,
        table_name: str,
        unsubscribe_fn: Callable[[], Awaitable[None]],
    ) -> None:
        self.subscription_id = subscription_id
        self.table_name = table_name
        self._unsubscribe_fn = unsubscribe_fn
        self._is_active = True

    @property
    def is_active(self) -> bool:
        """Check if subscription is active."""
        return self._is_active

    async def unsubscribe(self) -> None:
        """Unsubscribe from changes."""
        if self._is_active:
            await self._unsubscribe_fn()
            self._is_active = False


ChangeCallback = Callable[[ChangeNotification], None]


class RealtimeClient:
    """Client for real-time data subscriptions."""

    def __init__(
        self,
        base_url: str,
        project_id: str,
    ) -> None:
        self._base_url = base_url.rstrip("/")
        self._project_id = project_id
        self._connection: Any | None = None
        self._subscriptions: dict[str, ChangeCallback] = {}
        self._is_connected = False

    def _build_hub_url(self) -> str:
        """Build the SignalR hub URL."""
        return f"{self._base_url}/hubs/data?projectId={self._project_id}"

    def _get_headers(self) -> dict[str, str]:
        """Get connection headers."""
        return {}

    async def connect(self) -> None:
        """Connect to the real-time hub."""
        if self._is_connected:
            return

        hub_url = self._build_hub_url()
        headers = self._get_headers()

        self._connection = (
            HubConnectionBuilder()
            .with_url(hub_url, options={"headers": headers})
            .with_automatic_reconnect(
                {
                    "type": "interval",
                    "keep_alive_interval": 10,
                    "intervals": [0, 2, 5, 10, 30],
                }
            )
            .build()
        )

        # Set up event handlers
        self._connection.on("OnChange", self._handle_change)
        self._connection.on_open(self._on_connected)
        self._connection.on_close(self._on_disconnected)
        self._connection.on_error(self._on_error)

        # Start the connection
        self._connection.start()
        self._is_connected = True

        # Wait for connection to be established
        await asyncio.sleep(0.5)

    def _on_connected(self) -> None:
        """Handle connection established."""
        self._is_connected = True

    def _on_disconnected(self) -> None:
        """Handle disconnection."""
        self._is_connected = False

    def _on_error(self, error: Exception) -> None:
        """Handle connection error."""
        # Log or handle error as needed
        pass

    def _handle_change(self, data: list[Any]) -> None:
        """Handle incoming change notification."""
        if not data:
            return

        try:
            # SignalR sends arguments as a list
            notification_data = data[0] if isinstance(data, list) else data
            notification = ChangeNotification.model_validate(notification_data)

            # Dispatch to all subscriptions for this table
            for sub_id, callback in self._subscriptions.items():
                if sub_id.startswith(f"{notification.table_name}:"):
                    callback(notification)
        except Exception:
            # Silently handle parsing errors
            pass

    async def subscribe(
        self,
        table_name: str,
        callback: ChangeCallback,
        options: SubscriptionOptions | None = None,
    ) -> Subscription:
        """Subscribe to table changes."""
        await self.connect()

        subscription_id = f"{table_name}:{uuid4()}"
        self._subscriptions[subscription_id] = callback

        # Subscribe on the server
        if self._connection:
            subscribe_args = [table_name]
            if options:
                subscribe_args.append(options.model_dump(by_alias=True, exclude_none=True))
            else:
                subscribe_args.append({})

            self._connection.send("Subscribe", subscribe_args)

        async def unsubscribe() -> None:
            if subscription_id in self._subscriptions:
                del self._subscriptions[subscription_id]
                if self._connection:
                    self._connection.send("Unsubscribe", [table_name])

        return Subscription(subscription_id, table_name, unsubscribe)

    async def unsubscribe_all(self) -> None:
        """Unsubscribe from all subscriptions."""
        self._subscriptions.clear()

    async def disconnect(self) -> None:
        """Disconnect from the real-time hub."""
        if self._connection:
            self._connection.stop()
            self._connection = None
        self._is_connected = False
        self._subscriptions.clear()

    @property
    def is_connected(self) -> bool:
        """Check if connected to the hub."""
        return self._is_connected
