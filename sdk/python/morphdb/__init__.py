"""
MorphDB Python SDK

Official Python client for MorphDB - a PostgreSQL-based dynamic schema database service.
"""

from morphdb.client import MorphDBClient
from morphdb.exceptions import (
    MorphDBApiError,
    MorphDBAuthenticationError,
    MorphDBAuthorizationError,
    MorphDBConflictError,
    MorphDBConnectionError,
    MorphDBError,
    MorphDBNotFoundError,
    MorphDBValidationError,
)
from morphdb.models import (
    # Bulk models
    CsvExportOptions,
    CsvImportOptions,
    ExportJobStatus,
    ImportJobStatus,
    JsonExportOptions,
    JsonImportOptions,
    XlsxExportOptions,
    # Data models
    BatchRequest,
    BatchResponse,
    DataRecord,
    Filter,
    FilterOperator,
    OrderBy,
    PagedResponse,
    QueryRequest,
    # Realtime models
    ChangeNotification,
    ChangeOperation,
    SubscriptionOptions,
    # Schema models
    AddColumnRequest,
    AlterColumnRequest,
    ColumnInfo,
    CreateColumnRequest,
    CreateTableRequest,
    TableInfo,
    # Webhook models
    CreateWebhookRequest,
    WebhookDelivery,
    WebhookInfo,
)
from morphdb.realtime import Subscription

__version__ = "0.1.0"
__all__ = [
    # Client
    "MorphDBClient",
    # Exceptions
    "MorphDBError",
    "MorphDBApiError",
    "MorphDBNotFoundError",
    "MorphDBValidationError",
    "MorphDBAuthenticationError",
    "MorphDBAuthorizationError",
    "MorphDBConflictError",
    "MorphDBConnectionError",
    # Schema models
    "CreateTableRequest",
    "CreateColumnRequest",
    "AddColumnRequest",
    "AlterColumnRequest",
    "TableInfo",
    "ColumnInfo",
    # Data models
    "QueryRequest",
    "Filter",
    "FilterOperator",
    "OrderBy",
    "PagedResponse",
    "DataRecord",
    "BatchRequest",
    "BatchResponse",
    # Webhook models
    "CreateWebhookRequest",
    "WebhookInfo",
    "WebhookDelivery",
    # Bulk models
    "CsvImportOptions",
    "JsonImportOptions",
    "CsvExportOptions",
    "JsonExportOptions",
    "XlsxExportOptions",
    "ImportJobStatus",
    "ExportJobStatus",
    # Realtime models
    "ChangeNotification",
    "ChangeOperation",
    "SubscriptionOptions",
    "Subscription",
]
