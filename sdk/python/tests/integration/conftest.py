"""Pytest configuration for MorphDB SDK integration tests.

These tests require a running MorphDB server.
Set MORPHDB_TEST_URL environment variable or use default http://localhost:5000
"""

import os
from typing import AsyncGenerator
from uuid import uuid4

import pytest
import pytest_asyncio

from morphdb import MorphDBClient


# Test server URL - defaults to docker-compose.test.yml exposed port
MORPHDB_TEST_URL = os.environ.get("MORPHDB_TEST_URL", "http://localhost:5000")

# Skip all tests if server is not available
pytestmark = pytest.mark.integration


@pytest.fixture(scope="session")
def base_url() -> str:
    """Return the test server base URL."""
    return MORPHDB_TEST_URL


@pytest.fixture(scope="function")
def tenant_id() -> str:
    """Return a unique tenant ID for test isolation."""
    return str(uuid4())


@pytest_asyncio.fixture
async def client(base_url: str, tenant_id: str) -> AsyncGenerator[MorphDBClient, None]:
    """Create a MorphDBClient connected to the test server."""
    async with MorphDBClient(
        base_url=base_url,
        tenant_id=tenant_id,
        timeout=30.0,
    ) as client:
        yield client


@pytest.fixture
def unique_table_name() -> str:
    """Generate a unique table name for test isolation."""
    return f"test_{uuid4().hex[:8]}"


def pytest_configure(config: pytest.Config) -> None:
    """Register custom markers."""
    config.addinivalue_line(
        "markers", "integration: marks tests as integration tests (require running server)"
    )
