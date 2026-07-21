"""
MorphDB Python SDK

Official Python client for MorphDB - a PostgreSQL-based dynamic schema database service.
"""

from morphdb.batch import BatchClient
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
    # Schema models
    AddColumnRequest,
    AlterColumnRequest,
    # Data models
    BatchOperation,
    BatchOperationResult,
    BatchRequest,
    BatchResponse,
    # Realtime models
    ChangeNotification,
    ChangeOperation,
    ColumnInfo,
    CreateColumnRequest,
    CreateTableRequest,
    # Webhook models
    CreateWebhookRequest,
    # Bulk models
    CsvExportOptions,
    CsvImportOptions,
    DataRecord,
    ExportJobStatus,
    Filter,
    FilterOperator,
    ImportJobStatus,
    JsonExportOptions,
    JsonImportOptions,
    OrderBy,
    PagedResponse,
    QueryRequest,
    SubscriptionOptions,
    TableInfo,
    WebhookDelivery,
    WebhookInfo,
    XlsxExportOptions,
)
from morphdb.realtime import Subscription

__version__ = "0.0.0"
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
    "BatchClient",
    "BatchOperation",
    "BatchOperationResult",
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
