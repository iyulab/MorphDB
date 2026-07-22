/**
 * MorphDB Client Options
 */
export interface MorphDBClientOptions {
  /** Project ID for multi-project operations */
  projectId?: string;
  /** Request timeout in milliseconds (default: 30000) */
  timeout?: number;
  /** Number of retry attempts (default: 3) */
  retryCount?: number;
  /** Delay between retries in milliseconds (default: 1000) */
  retryDelay?: number;
}

// Schema Types

export interface CreateTableRequest {
  name: string;
  columns: CreateColumnRequest[];
  description?: string;
}

export interface CreateColumnRequest {
  name: string;
  type: ColumnType;
  nullable?: boolean;
  unique?: boolean;
  indexed?: boolean;
  defaultValue?: string;
  checkExpression?: string;
  description?: string;
}

export interface AddColumnRequest {
  name: string;
  type: ColumnType;
  nullable?: boolean;
  defaultValue?: string;
}

export interface AlterColumnRequest {
  newName?: string;
  newType?: ColumnType;
  nullable?: boolean;
  defaultValue?: string;
}

export type ColumnType =
  | 'text'
  | 'integer'
  | 'bigint'
  | 'decimal'
  | 'boolean'
  | 'timestamp'
  | 'date'
  | 'uuid'
  | 'jsonb'
  | 'array';

export interface TableInfo {
  tableId: string;
  name: string;
  physicalName: string;
  schemaVersion: number;
  columns: ColumnInfo[];
  createdAt: string;
  updatedAt: string;
}

export interface ColumnInfo {
  columnId: string;
  name: string;
  physicalName: string;
  dataType: string;
  nativeType: string;
  nullable: boolean;
  unique: boolean;
  primaryKey: boolean;
  indexed: boolean;
  ordinalPosition: number;
}

// Data Types

export interface QueryRequest {
  select?: string[];
  filters?: Filter[];
  orderBy?: OrderBy[];
  page?: number;
  pageSize?: number;
}

export interface Filter {
  column: string;
  operator: FilterOperator;
  value?: unknown;
}

export type FilterOperator =
  | 'eq'
  | 'neq'
  | 'gt'
  | 'gte'
  | 'lt'
  | 'lte'
  | 'contains'
  | 'startswith'
  | 'endswith'
  | 'isnull'
  | 'isnotnull'
  | 'in'
  | 'notin';

export interface OrderBy {
  column: string;
  ascending?: boolean;
}

export interface PagedResponse<T> {
  data: T[];
  pagination: PaginationInfo;
}

export interface PaginationInfo {
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
  hasNextPage: boolean;
  hasPreviousPage: boolean;
}

export interface DataRecord {
  id: string;
  data: Record<string, unknown>;
  createdAt: string;
  updatedAt: string;
}

export type BatchMethod = 'INSERT' | 'UPDATE' | 'DELETE' | 'UPSERT';

export interface BatchOperation {
  method: BatchMethod;
  table: string;
  /** Target record, for UPDATE and DELETE */
  id?: string;
  /** Record payload, for INSERT, UPDATE and UPSERT */
  data?: Record<string, unknown>;
  /** Columns identifying an existing row, for UPSERT */
  keyColumns?: string[];
}

export interface BatchRequest {
  operations: BatchOperation[];
}

export interface BatchOperationResult {
  index: number;
  success: boolean;
  /** Identifying data for the affected record — for inserts, the generated `_id` */
  data?: Record<string, unknown>;
  error?: string;
  affectedRows?: number;
}

export interface BatchResponse {
  results: BatchOperationResult[];
  successCount: number;
  failureCount: number;
}

// Webhook Types

export interface CreateWebhookRequest {
  name: string;
  tableName: string;
  url: string;
  events?: WebhookEvent[];
  filter?: string;
  headers?: Record<string, string>;
}

export type WebhookEvent = 'insert' | 'update' | 'delete';

export interface WebhookInfo {
  webhookId: string;
  name: string;
  tableName: string;
  url: string;
  events: WebhookEvent[];
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface WebhookDelivery {
  deliveryId: string;
  webhookId: string;
  recordId?: string;
  event: string;
  status: string;
  attemptCount: number;
  httpStatusCode?: number;
  errorMessage?: string;
  createdAt: string;
  deliveredAt?: string;
}

// Bulk Types

export interface CsvImportOptions {
  delimiter?: string;
  hasHeader?: boolean;
  dateFormat?: string;
  trimWhitespace?: boolean;
  nullHandling?: 'empty-as-null' | 'keep-empty';
  duplicateHandling?: 'insert' | 'update' | 'skip';
  keyColumns?: string[];
}

export interface JsonImportOptions {
  jsonPath?: string;
  dateFormat?: string;
  duplicateHandling?: 'insert' | 'update' | 'skip';
  keyColumns?: string[];
}

export interface CsvExportOptions {
  columns?: string[];
  filter?: string;
  orderBy?: string;
  delimiter?: string;
  includeHeader?: boolean;
  dateFormat?: string;
}

export interface JsonExportOptions {
  columns?: string[];
  filter?: string;
  orderBy?: string;
  pretty?: boolean;
  dateFormat?: string;
}

export interface XlsxExportOptions {
  columns?: string[];
  filter?: string;
  orderBy?: string;
  sheetName?: string;
  dateFormat?: string;
}

export interface ImportJobStatus {
  jobId: string;
  tableName: string;
  format: string;
  status: JobStatus;
  totalRows: number;
  processedRows: number;
  successCount: number;
  errorCount: number;
  errorMessage?: string;
  createdAt: string;
  startedAt?: string;
  completedAt?: string;
  percentComplete: number;
}

export interface ExportJobStatus {
  jobId: string;
  tableName: string;
  format: string;
  status: JobStatus;
  totalRows: number;
  processedRows: number;
  downloadUrl?: string;
  fileSize?: number;
  errorMessage?: string;
  createdAt: string;
  startedAt?: string;
  completedAt?: string;
  expiresAt?: string;
  percentComplete: number;
}

export type JobStatus = 'pending' | 'processing' | 'completed' | 'failed' | 'cancelled';

// Realtime Types

export interface ChangeNotification {
  tableName: string;
  operation: ChangeOperation;
  recordId: string;
  data?: Record<string, unknown>;
  oldData?: Record<string, unknown>;
  projectId: string;
  timestamp: string;
}

export type ChangeOperation = 'insert' | 'update' | 'delete';

export interface SubscriptionOptions {
  filter?: string;
  columns?: string[];
  includeOldData?: boolean;
}

export interface Subscription {
  subscriptionId: string;
  tableName: string;
  isActive: boolean;
  unsubscribe(): Promise<void>;
}
