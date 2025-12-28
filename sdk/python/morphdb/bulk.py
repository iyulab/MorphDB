"""MorphDB SDK bulk operations client."""

from uuid import UUID

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


class BulkClient:
    """Client for bulk import/export operations."""

    def __init__(self, http: HttpClient) -> None:
        self._http = http

    # Import operations

    async def import_csv(
        self,
        table_name: str,
        file_content: bytes,
        filename: str = "data.csv",
        options: CsvImportOptions | None = None,
    ) -> ImportJobStatus:
        """Import CSV data into a table."""
        params = {"tableName": table_name}
        if options:
            params.update(options.model_dump(by_alias=True, exclude_none=True))

        data = await self._http.post_file(
            "/api/bulk/import/csv",
            file_content,
            filename,
            "text/csv",
            params,
        )
        return ImportJobStatus.model_validate(data)

    async def import_json(
        self,
        table_name: str,
        file_content: bytes,
        filename: str = "data.json",
        options: JsonImportOptions | None = None,
    ) -> ImportJobStatus:
        """Import JSON data into a table."""
        params = {"tableName": table_name}
        if options:
            params.update(options.model_dump(by_alias=True, exclude_none=True))

        data = await self._http.post_file(
            "/api/bulk/import/json",
            file_content,
            filename,
            "application/json",
            params,
        )
        return ImportJobStatus.model_validate(data)

    async def get_import_status(self, job_id: UUID | str) -> ImportJobStatus:
        """Get import job status."""
        data = await self._http.get(f"/api/bulk/import/{job_id}/status")
        return ImportJobStatus.model_validate(data)

    async def cancel_import(self, job_id: UUID | str) -> ImportJobStatus:
        """Cancel an import job."""
        data = await self._http.post(f"/api/bulk/import/{job_id}/cancel")
        return ImportJobStatus.model_validate(data)

    # Export operations

    async def export_csv(
        self,
        table_name: str,
        options: CsvExportOptions | None = None,
    ) -> ExportJobStatus:
        """Export table data to CSV."""
        body = {"tableName": table_name}
        if options:
            body.update(options.model_dump(by_alias=True, exclude_none=True))

        data = await self._http.post("/api/bulk/export/csv", body)
        return ExportJobStatus.model_validate(data)

    async def export_json(
        self,
        table_name: str,
        options: JsonExportOptions | None = None,
    ) -> ExportJobStatus:
        """Export table data to JSON."""
        body = {"tableName": table_name}
        if options:
            body.update(options.model_dump(by_alias=True, exclude_none=True))

        data = await self._http.post("/api/bulk/export/json", body)
        return ExportJobStatus.model_validate(data)

    async def export_xlsx(
        self,
        table_name: str,
        options: XlsxExportOptions | None = None,
    ) -> ExportJobStatus:
        """Export table data to XLSX."""
        body = {"tableName": table_name}
        if options:
            body.update(options.model_dump(by_alias=True, exclude_none=True))

        data = await self._http.post("/api/bulk/export/xlsx", body)
        return ExportJobStatus.model_validate(data)

    async def get_export_status(self, job_id: UUID | str) -> ExportJobStatus:
        """Get export job status."""
        data = await self._http.get(f"/api/bulk/export/{job_id}/status")
        return ExportJobStatus.model_validate(data)

    async def download_export(self, job_id: UUID | str) -> bytes:
        """Download exported file."""
        return await self._http.get_bytes(f"/api/bulk/export/{job_id}/download")

    async def cancel_export(self, job_id: UUID | str) -> ExportJobStatus:
        """Cancel an export job."""
        data = await self._http.post(f"/api/bulk/export/{job_id}/cancel")
        return ExportJobStatus.model_validate(data)
