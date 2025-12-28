"""MorphDB SDK data models."""

from datetime import datetime
from enum import Enum
from typing import Any
from uuid import UUID

from pydantic import BaseModel, Field


# Schema Models


class CreateColumnRequest(BaseModel):
    """Request to create a column."""

    name: str
    type: str
    nullable: bool = True
    unique: bool = False
    indexed: bool = False
    default_value: str | None = Field(None, alias="defaultValue")
    check_expression: str | None = Field(None, alias="checkExpression")
    description: str | None = None

    model_config = {"populate_by_name": True}


class CreateTableRequest(BaseModel):
    """Request to create a table."""

    name: str
    columns: list[CreateColumnRequest]
    description: str | None = None


class AddColumnRequest(BaseModel):
    """Request to add a column."""

    name: str
    type: str
    nullable: bool = True
    default_value: str | None = Field(None, alias="defaultValue")

    model_config = {"populate_by_name": True}


class AlterColumnRequest(BaseModel):
    """Request to alter a column."""

    new_name: str | None = Field(None, alias="newName")
    new_type: str | None = Field(None, alias="newType")
    nullable: bool | None = None
    default_value: str | None = Field(None, alias="defaultValue")

    model_config = {"populate_by_name": True}


class ColumnInfo(BaseModel):
    """Column information."""

    column_id: UUID = Field(alias="columnId")
    name: str
    physical_name: str = Field(alias="physicalName")
    data_type: str = Field(alias="dataType")
    native_type: str = Field(alias="nativeType")
    nullable: bool = Field(alias="isNullable")
    unique: bool = Field(alias="isUnique")
    primary_key: bool = Field(alias="isPrimaryKey")
    indexed: bool = Field(alias="isIndexed")
    ordinal_position: int = Field(alias="ordinalPosition")

    model_config = {"populate_by_name": True}


class TableInfo(BaseModel):
    """Table information."""

    table_id: UUID = Field(alias="tableId")
    name: str
    physical_name: str = Field(alias="physicalName")
    schema_version: int = Field(alias="schemaVersion")
    columns: list[ColumnInfo]
    created_at: datetime = Field(alias="createdAt")
    updated_at: datetime = Field(alias="updatedAt")

    model_config = {"populate_by_name": True}


# Data Models


class FilterOperator(str, Enum):
    """Filter operators."""

    EQ = "eq"
    NEQ = "neq"
    GT = "gt"
    GTE = "gte"
    LT = "lt"
    LTE = "lte"
    CONTAINS = "contains"
    STARTSWITH = "startswith"
    ENDSWITH = "endswith"
    ISNULL = "isnull"
    ISNOTNULL = "isnotnull"
    IN = "in"
    NOTIN = "notin"


class Filter(BaseModel):
    """Filter condition."""

    column: str
    operator: FilterOperator
    value: Any = None


class OrderBy(BaseModel):
    """Order by specification."""

    column: str
    ascending: bool = True


class QueryRequest(BaseModel):
    """Query request."""

    select: list[str] | None = None
    filters: list[Filter] | None = None
    order_by: list[OrderBy] | None = Field(None, alias="orderBy")
    page: int = 1
    page_size: int = Field(50, alias="pageSize")

    model_config = {"populate_by_name": True}


class PaginationInfo(BaseModel):
    """Pagination information."""

    page: int
    page_size: int = Field(alias="pageSize")
    total_count: int = Field(alias="totalCount")
    total_pages: int = Field(alias="totalPages")
    has_next_page: bool = Field(alias="hasNextPage")
    has_previous_page: bool = Field(alias="hasPreviousPage")

    model_config = {"populate_by_name": True}


class DataRecord(BaseModel):
    """Data record."""

    id: UUID
    data: dict[str, Any]
    created_at: datetime = Field(alias="createdAt")
    updated_at: datetime = Field(alias="updatedAt")

    model_config = {"populate_by_name": True}


class PagedResponse(BaseModel):
    """Paged response."""

    data: list[DataRecord]
    pagination: PaginationInfo


class BatchRequest(BaseModel):
    """Batch operation request."""

    inserts: list[dict[str, Any]] | None = None
    updates: list[dict[str, Any]] | None = None
    deletes: list[UUID] | None = None


class BatchResponse(BaseModel):
    """Batch operation response."""

    inserted: list[DataRecord]
    updated: list[DataRecord]
    deleted: int


# Webhook Models


class CreateWebhookRequest(BaseModel):
    """Request to create a webhook."""

    name: str
    table_name: str = Field(alias="tableName")
    url: str
    events: list[str] = Field(default_factory=lambda: ["insert", "update", "delete"])
    filter: str | None = None
    headers: dict[str, str] | None = None

    model_config = {"populate_by_name": True}


class WebhookInfo(BaseModel):
    """Webhook information."""

    webhook_id: UUID = Field(alias="webhookId")
    name: str
    table_name: str = Field(alias="tableName")
    url: str
    events: list[str]
    is_active: bool = Field(alias="isActive")
    created_at: datetime = Field(alias="createdAt")
    updated_at: datetime = Field(alias="updatedAt")

    model_config = {"populate_by_name": True}


class WebhookDelivery(BaseModel):
    """Webhook delivery record."""

    delivery_id: UUID = Field(alias="deliveryId")
    webhook_id: UUID = Field(alias="webhookId")
    record_id: UUID | None = Field(None, alias="recordId")
    event: str
    status: str
    attempt_count: int = Field(alias="attemptCount")
    http_status_code: int | None = Field(None, alias="httpStatusCode")
    error_message: str | None = Field(None, alias="errorMessage")
    created_at: datetime = Field(alias="createdAt")
    delivered_at: datetime | None = Field(None, alias="deliveredAt")

    model_config = {"populate_by_name": True}


# Bulk Models


class CsvImportOptions(BaseModel):
    """CSV import options."""

    delimiter: str = ","
    has_header: bool = Field(True, alias="hasHeader")
    date_format: str | None = Field(None, alias="dateFormat")
    trim_whitespace: bool = Field(True, alias="trimWhitespace")
    null_handling: str = Field("empty-as-null", alias="nullHandling")
    duplicate_handling: str = Field("insert", alias="duplicateHandling")
    key_columns: list[str] | None = Field(None, alias="keyColumns")

    model_config = {"populate_by_name": True}


class JsonImportOptions(BaseModel):
    """JSON import options."""

    json_path: str | None = Field(None, alias="jsonPath")
    date_format: str | None = Field(None, alias="dateFormat")
    duplicate_handling: str = Field("insert", alias="duplicateHandling")
    key_columns: list[str] | None = Field(None, alias="keyColumns")

    model_config = {"populate_by_name": True}


class CsvExportOptions(BaseModel):
    """CSV export options."""

    columns: list[str] | None = None
    filter: str | None = None
    order_by: str | None = Field(None, alias="orderBy")
    delimiter: str = ","
    include_header: bool = Field(True, alias="includeHeader")
    date_format: str | None = Field(None, alias="dateFormat")

    model_config = {"populate_by_name": True}


class JsonExportOptions(BaseModel):
    """JSON export options."""

    columns: list[str] | None = None
    filter: str | None = None
    order_by: str | None = Field(None, alias="orderBy")
    pretty: bool = False
    date_format: str | None = Field(None, alias="dateFormat")

    model_config = {"populate_by_name": True}


class XlsxExportOptions(BaseModel):
    """XLSX export options."""

    columns: list[str] | None = None
    filter: str | None = None
    order_by: str | None = Field(None, alias="orderBy")
    sheet_name: str = Field("Sheet1", alias="sheetName")
    date_format: str | None = Field(None, alias="dateFormat")

    model_config = {"populate_by_name": True}


class ImportJobStatus(BaseModel):
    """Import job status."""

    job_id: UUID = Field(alias="jobId")
    table_name: str = Field(alias="tableName")
    format: str
    status: str
    total_rows: int = Field(alias="totalRows")
    processed_rows: int = Field(alias="processedRows")
    success_count: int = Field(alias="successCount")
    error_count: int = Field(alias="errorCount")
    error_message: str | None = Field(None, alias="errorMessage")
    created_at: datetime = Field(alias="createdAt")
    started_at: datetime | None = Field(None, alias="startedAt")
    completed_at: datetime | None = Field(None, alias="completedAt")

    model_config = {"populate_by_name": True}

    @property
    def percent_complete(self) -> float:
        """Get progress percentage."""
        if self.total_rows > 0:
            return (self.processed_rows / self.total_rows) * 100
        return 0


class ExportJobStatus(BaseModel):
    """Export job status."""

    job_id: UUID = Field(alias="jobId")
    table_name: str = Field(alias="tableName")
    format: str
    status: str
    total_rows: int = Field(alias="totalRows")
    processed_rows: int = Field(alias="processedRows")
    download_url: str | None = Field(None, alias="downloadUrl")
    file_size: int | None = Field(None, alias="fileSize")
    error_message: str | None = Field(None, alias="errorMessage")
    created_at: datetime = Field(alias="createdAt")
    started_at: datetime | None = Field(None, alias="startedAt")
    completed_at: datetime | None = Field(None, alias="completedAt")
    expires_at: datetime | None = Field(None, alias="expiresAt")

    model_config = {"populate_by_name": True}

    @property
    def percent_complete(self) -> float:
        """Get progress percentage."""
        if self.total_rows > 0:
            return (self.processed_rows / self.total_rows) * 100
        return 0


# Realtime Models


class ChangeOperation(str, Enum):
    """Change operation types."""

    INSERT = "insert"
    UPDATE = "update"
    DELETE = "delete"


class ChangeNotification(BaseModel):
    """Change notification."""

    table_name: str = Field(alias="tableName")
    operation: ChangeOperation
    record_id: UUID = Field(alias="recordId")
    data: dict[str, Any] | None = None
    old_data: dict[str, Any] | None = Field(None, alias="oldData")
    tenant_id: UUID = Field(alias="tenantId")
    timestamp: datetime

    model_config = {"populate_by_name": True}


class SubscriptionOptions(BaseModel):
    """Subscription options."""

    filter: str | None = None
    columns: list[str] | None = None
    include_old_data: bool = Field(False, alias="includeOldData")

    model_config = {"populate_by_name": True}
