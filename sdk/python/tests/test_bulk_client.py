"""Tests for BulkClient."""

from typing import Any
from unittest.mock import AsyncMock
from uuid import uuid4

import pytest

from morphdb.bulk import BulkClient
from morphdb.http import HttpClient
from morphdb.models import (
    CsvExportOptions,
    CsvImportOptions,
    ExportJobStatus,
    ImportJobStatus,
    JsonExportOptions,
    JsonImportOptions,
    XlsxExportOptions,
)


class TestBulkClient:
    """Test cases for BulkClient."""

    @pytest.fixture
    def bulk_client(self, mock_http_client: HttpClient) -> BulkClient:
        """Create a BulkClient with mocked HTTP client."""
        return BulkClient(mock_http_client)

    @pytest.mark.asyncio
    async def test_import_csv(
        self,
        bulk_client: BulkClient,
        mock_http_client: HttpClient,
        sample_import_job_response: dict[str, Any],
    ) -> None:
        """Test CSV import."""
        mock_http_client.post_file = AsyncMock(return_value=sample_import_job_response)

        csv_data = b"name,email\nJohn,john@example.com"
        options = CsvImportOptions(delimiter=",", has_header=True)

        job = await bulk_client.import_csv("users", csv_data, "users.csv", options)

        assert isinstance(job, ImportJobStatus)
        assert job.format == "csv"
        mock_http_client.post_file.assert_called_once()
        call_args = mock_http_client.post_file.call_args
        assert call_args[0][0] == "/api/bulk/import/csv"
        assert call_args[0][1] == csv_data
        assert call_args[0][2] == "users.csv"
        assert call_args[0][3] == "text/csv"

    @pytest.mark.asyncio
    async def test_import_csv_default_filename(
        self,
        bulk_client: BulkClient,
        mock_http_client: HttpClient,
        sample_import_job_response: dict[str, Any],
    ) -> None:
        """Test CSV import with default filename."""
        mock_http_client.post_file = AsyncMock(return_value=sample_import_job_response)

        csv_data = b"name,email\nJohn,john@example.com"

        job = await bulk_client.import_csv("users", csv_data)

        assert isinstance(job, ImportJobStatus)
        call_args = mock_http_client.post_file.call_args
        assert call_args[0][2] == "data.csv"  # default filename

    @pytest.mark.asyncio
    async def test_import_json(
        self,
        bulk_client: BulkClient,
        mock_http_client: HttpClient,
        sample_import_job_response: dict[str, Any],
    ) -> None:
        """Test JSON import."""
        sample_import_job_response["format"] = "json"
        mock_http_client.post_file = AsyncMock(return_value=sample_import_job_response)

        json_data = b'[{"name": "John", "email": "john@example.com"}]'
        options = JsonImportOptions()

        job = await bulk_client.import_json("users", json_data, "users.json", options)

        assert isinstance(job, ImportJobStatus)
        assert job.format == "json"
        call_args = mock_http_client.post_file.call_args
        assert call_args[0][0] == "/api/bulk/import/json"
        assert call_args[0][3] == "application/json"

    @pytest.mark.asyncio
    async def test_get_import_status(
        self,
        bulk_client: BulkClient,
        mock_http_client: HttpClient,
        sample_import_job_response: dict[str, Any],
    ) -> None:
        """Test getting import job status."""
        mock_http_client.get = AsyncMock(return_value=sample_import_job_response)
        job_id = sample_import_job_response["jobId"]

        job = await bulk_client.get_import_status(job_id)

        assert isinstance(job, ImportJobStatus)
        assert job.status == "processing"
        assert job.percent_complete == 50.0
        mock_http_client.get.assert_called_once_with(f"/api/bulk/import/{job_id}/status")

    @pytest.mark.asyncio
    async def test_cancel_import(
        self,
        bulk_client: BulkClient,
        mock_http_client: HttpClient,
        sample_import_job_response: dict[str, Any],
    ) -> None:
        """Test canceling an import job."""
        sample_import_job_response["status"] = "cancelled"
        mock_http_client.post = AsyncMock(return_value=sample_import_job_response)
        job_id = sample_import_job_response["jobId"]

        job = await bulk_client.cancel_import(job_id)

        assert isinstance(job, ImportJobStatus)
        assert job.status == "cancelled"
        mock_http_client.post.assert_called_once_with(
            f"/api/bulk/import/{job_id}/cancel"
        )

    @pytest.mark.asyncio
    async def test_export_csv(
        self,
        bulk_client: BulkClient,
        mock_http_client: HttpClient,
        sample_export_job_response: dict[str, Any],
    ) -> None:
        """Test CSV export."""
        mock_http_client.post = AsyncMock(return_value=sample_export_job_response)

        options = CsvExportOptions(columns=["name", "email"], delimiter=",")

        job = await bulk_client.export_csv("users", options)

        assert isinstance(job, ExportJobStatus)
        assert job.format == "csv"
        mock_http_client.post.assert_called_once()
        call_args = mock_http_client.post.call_args
        assert call_args[0][0] == "/api/bulk/export/csv"
        body = call_args[0][1]
        assert body["tableName"] == "users"
        assert body["columns"] == ["name", "email"]

    @pytest.mark.asyncio
    async def test_export_csv_no_options(
        self,
        bulk_client: BulkClient,
        mock_http_client: HttpClient,
        sample_export_job_response: dict[str, Any],
    ) -> None:
        """Test CSV export without options."""
        mock_http_client.post = AsyncMock(return_value=sample_export_job_response)

        job = await bulk_client.export_csv("users")

        assert isinstance(job, ExportJobStatus)
        call_args = mock_http_client.post.call_args
        body = call_args[0][1]
        assert body == {"tableName": "users"}

    @pytest.mark.asyncio
    async def test_export_json(
        self,
        bulk_client: BulkClient,
        mock_http_client: HttpClient,
        sample_export_job_response: dict[str, Any],
    ) -> None:
        """Test JSON export."""
        sample_export_job_response["format"] = "json"
        mock_http_client.post = AsyncMock(return_value=sample_export_job_response)

        options = JsonExportOptions(pretty=True)

        job = await bulk_client.export_json("users", options)

        assert isinstance(job, ExportJobStatus)
        assert job.format == "json"
        call_args = mock_http_client.post.call_args
        assert call_args[0][0] == "/api/bulk/export/json"

    @pytest.mark.asyncio
    async def test_export_xlsx(
        self,
        bulk_client: BulkClient,
        mock_http_client: HttpClient,
        sample_export_job_response: dict[str, Any],
    ) -> None:
        """Test XLSX export."""
        sample_export_job_response["format"] = "xlsx"
        mock_http_client.post = AsyncMock(return_value=sample_export_job_response)

        options = XlsxExportOptions(sheet_name="Users")

        job = await bulk_client.export_xlsx("users", options)

        assert isinstance(job, ExportJobStatus)
        call_args = mock_http_client.post.call_args
        assert call_args[0][0] == "/api/bulk/export/xlsx"
        body = call_args[0][1]
        assert body["sheetName"] == "Users"

    @pytest.mark.asyncio
    async def test_get_export_status(
        self,
        bulk_client: BulkClient,
        mock_http_client: HttpClient,
        sample_export_job_response: dict[str, Any],
    ) -> None:
        """Test getting export job status."""
        mock_http_client.get = AsyncMock(return_value=sample_export_job_response)
        job_id = sample_export_job_response["jobId"]

        job = await bulk_client.get_export_status(job_id)

        assert isinstance(job, ExportJobStatus)
        assert job.status == "completed"
        assert job.download_url is not None
        assert job.percent_complete == 100.0
        mock_http_client.get.assert_called_once_with(f"/api/bulk/export/{job_id}/status")

    @pytest.mark.asyncio
    async def test_download_export(
        self,
        bulk_client: BulkClient,
        mock_http_client: HttpClient,
    ) -> None:
        """Test downloading export file."""
        expected_content = b"name,email\nJohn,john@example.com"
        mock_http_client.get_bytes = AsyncMock(return_value=expected_content)
        job_id = "test-job-id"

        content = await bulk_client.download_export(job_id)

        assert content == expected_content
        mock_http_client.get_bytes.assert_called_once_with(
            f"/api/bulk/export/{job_id}/download"
        )

    @pytest.mark.asyncio
    async def test_cancel_export(
        self,
        bulk_client: BulkClient,
        mock_http_client: HttpClient,
        sample_export_job_response: dict[str, Any],
    ) -> None:
        """Test canceling an export job."""
        sample_export_job_response["status"] = "cancelled"
        mock_http_client.post = AsyncMock(return_value=sample_export_job_response)
        job_id = sample_export_job_response["jobId"]

        job = await bulk_client.cancel_export(job_id)

        assert isinstance(job, ExportJobStatus)
        assert job.status == "cancelled"
        mock_http_client.post.assert_called_once_with(
            f"/api/bulk/export/{job_id}/cancel"
        )


class TestImportJobStatus:
    """Test ImportJobStatus model."""

    def test_percent_complete_calculation(
        self,
        sample_import_job_response: dict[str, Any],
    ) -> None:
        """Test percent complete calculation."""
        job = ImportJobStatus.model_validate(sample_import_job_response)
        assert job.percent_complete == 50.0

    def test_percent_complete_zero_rows(self) -> None:
        """Test percent complete with zero total rows."""
        from datetime import datetime, timezone

        now = datetime.now(timezone.utc).isoformat()
        job = ImportJobStatus.model_validate(
            {
                # ImportJobStatus.job_id is a UUID; a placeholder string never parsed.
                "jobId": str(uuid4()),
                "tableName": "users",
                "format": "csv",
                "status": "pending",
                "totalRows": 0,
                "processedRows": 0,
                "successCount": 0,
                "errorCount": 0,
                "createdAt": now,
            }
        )
        assert job.percent_complete == 0.0


class TestExportJobStatus:
    """Test ExportJobStatus model."""

    def test_percent_complete_calculation(
        self,
        sample_export_job_response: dict[str, Any],
    ) -> None:
        """Test percent complete calculation."""
        job = ExportJobStatus.model_validate(sample_export_job_response)
        assert job.percent_complete == 100.0
