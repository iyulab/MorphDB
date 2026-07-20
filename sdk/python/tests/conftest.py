"""Pytest configuration and fixtures for MorphDB SDK tests."""

from datetime import datetime, timezone
from typing import Any
from unittest.mock import AsyncMock
from uuid import uuid4

import pytest

from morphdb.http import HttpClient


@pytest.fixture
def project_id() -> str:
    """Return a test project ID."""
    return str(uuid4())


@pytest.fixture
def api_key() -> str:
    """Return a test API key."""
    return "test-api-key-12345"


@pytest.fixture
def base_url() -> str:
    """Return a test base URL."""
    return "http://localhost:5000"


@pytest.fixture
def mock_http_client(base_url: str, project_id: str, api_key: str) -> HttpClient:
    """Create a mock HTTP client for testing."""
    client = HttpClient(
        base_url=base_url,
        project_id=project_id,
        api_key=api_key,
        timeout=30.0,
        retry_count=1,
        retry_delay=0.1,
    )
    # Mock all HTTP methods
    client.get = AsyncMock()
    client.post = AsyncMock()
    client.put = AsyncMock()
    client.patch = AsyncMock()
    client.delete = AsyncMock()
    client.post_file = AsyncMock()
    client.get_bytes = AsyncMock()
    client.close = AsyncMock()
    return client


@pytest.fixture
def sample_table_response() -> dict[str, Any]:
    """Return a sample table response."""
    now = datetime.now(timezone.utc).isoformat()
    return {
        "tableId": str(uuid4()),
        "name": "users",
        "physicalName": "tbl_abc123",
        "schemaVersion": 1,
        "columns": [
            {
                "columnId": str(uuid4()),
                "name": "_id",
                "physicalName": "col_id123",
                "dataType": "uuid",
                "nativeType": "uuid",
                "isNullable": False,
                "isUnique": True,
                "isPrimaryKey": True,
                "isIndexed": True,
                "ordinalPosition": 0,
            },
            {
                "columnId": str(uuid4()),
                "name": "name",
                "physicalName": "col_name123",
                "dataType": "text",
                "nativeType": "text",
                "isNullable": False,
                "isUnique": False,
                "isPrimaryKey": False,
                "isIndexed": False,
                "ordinalPosition": 1,
            },
            {
                "columnId": str(uuid4()),
                "name": "email",
                "physicalName": "col_email123",
                "dataType": "text",
                "nativeType": "text",
                "isNullable": True,
                "isUnique": True,
                "isPrimaryKey": False,
                "isIndexed": True,
                "ordinalPosition": 2,
            },
        ],
        "createdAt": now,
        "updatedAt": now,
    }


@pytest.fixture
def sample_data_record() -> dict[str, Any]:
    """Return a sample data record response."""
    now = datetime.now(timezone.utc).isoformat()
    return {
        "id": str(uuid4()),
        "data": {
            "name": "John Doe",
            "email": "john@example.com",
        },
        "createdAt": now,
        "updatedAt": now,
    }


@pytest.fixture
def sample_paged_response(sample_data_record: dict[str, Any]) -> dict[str, Any]:
    """Return a sample paged response."""
    return {
        "data": [sample_data_record],
        "pagination": {
            "page": 1,
            "pageSize": 50,
            "totalCount": 1,
            "totalPages": 1,
            "hasNextPage": False,
            "hasPreviousPage": False,
        },
    }


@pytest.fixture
def sample_webhook_response() -> dict[str, Any]:
    """Return a sample webhook response."""
    now = datetime.now(timezone.utc).isoformat()
    return {
        "webhookId": str(uuid4()),
        "name": "order-notifications",
        "tableName": "orders",
        "url": "https://example.com/webhook",
        "events": ["insert", "update", "delete"],
        "isActive": True,
        "createdAt": now,
        "updatedAt": now,
    }


@pytest.fixture
def sample_import_job_response() -> dict[str, Any]:
    """Return a sample import job response."""
    now = datetime.now(timezone.utc).isoformat()
    return {
        "jobId": str(uuid4()),
        "tableName": "users",
        "format": "csv",
        "status": "processing",
        "totalRows": 100,
        "processedRows": 50,
        "successCount": 48,
        "errorCount": 2,
        "errorMessage": None,
        "createdAt": now,
        "startedAt": now,
        "completedAt": None,
    }


@pytest.fixture
def sample_export_job_response() -> dict[str, Any]:
    """Return a sample export job response."""
    now = datetime.now(timezone.utc).isoformat()
    return {
        "jobId": str(uuid4()),
        "tableName": "users",
        "format": "csv",
        "status": "completed",
        "totalRows": 100,
        "processedRows": 100,
        "downloadUrl": "https://storage.example.com/exports/abc123.csv",
        "fileSize": 12345,
        "errorMessage": None,
        "createdAt": now,
        "startedAt": now,
        "completedAt": now,
        "expiresAt": now,
    }


@pytest.fixture
def sample_batch_response() -> dict[str, Any]:
    """Return a batch response exactly as BatchController serializes it."""
    return {
        "results": [
            {"index": 0, "success": True, "data": {"_id": str(uuid4())}, "affectedRows": 1},
            {"index": 1, "success": True, "data": {"_id": str(uuid4())}, "affectedRows": 1},
        ],
        "successCount": 2,
        "failureCount": 0,
    }
