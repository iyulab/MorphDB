// Main client
export { MorphDBClient } from './client.js';

// Sub-clients
export { SchemaClient } from './schema.js';
export { DataClient } from './data.js';
export { BatchClient } from './batch.js';
export { WebhookClient } from './webhook.js';
export { BulkClient } from './bulk.js';
export { RealtimeClient } from './realtime.js';

// Errors
export {
  MorphDBError,
  MorphDBApiError,
  MorphDBNotFoundError,
  MorphDBValidationError,
  MorphDBAuthenticationError,
  MorphDBAuthorizationError,
  MorphDBConflictError,
  MorphDBConnectionError,
} from './errors.js';

// Types
export type {
  // Client options
  MorphDBClientOptions,

  // Schema types
  CreateTableRequest,
  CreateColumnRequest,
  AddColumnRequest,
  AlterColumnRequest,
  ColumnType,
  TableInfo,
  ColumnInfo,

  // Data types
  QueryRequest,
  Filter,
  FilterOperator,
  OrderBy,
  PagedResponse,
  PaginationInfo,
  DataRecord,
  BatchMethod,
  BatchOperation,
  BatchOperationResult,
  BatchRequest,
  BatchResponse,

  // Webhook types
  CreateWebhookRequest,
  WebhookEvent,
  WebhookInfo,
  WebhookDelivery,

  // Bulk types
  CsvImportOptions,
  JsonImportOptions,
  CsvExportOptions,
  JsonExportOptions,
  XlsxExportOptions,
  ImportJobStatus,
  ExportJobStatus,
  JobStatus,

  // Realtime types
  ChangeNotification,
  ChangeOperation,
  SubscriptionOptions,
  Subscription,
} from './types.js';
