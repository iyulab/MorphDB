/**
 * MorphDB API Types - Server API Schema
 *
 * This file contains types that EXACTLY match the server API responses.
 * All field names use camelCase as they come from the ASP.NET Core serializer.
 *
 * Internal types used throughout the app should import from this file
 * and use the provided converter functions.
 */

// =============================================================================
// COMMON TYPES
// =============================================================================

export interface RawPagedResponse<T> {
  data: T[]
  pagination: RawPaginationInfo
}

export interface RawPaginationInfo {
  page: number
  pageSize: number
  totalCount: number
  totalPages: number
  hasNext: boolean
  hasPrevious: boolean
}

export interface RawErrorResponse {
  error: string
  message: string
  code?: string
  details?: Record<string, string[]>
}

// =============================================================================
// PROJECT TYPES
// =============================================================================

export interface RawProjectApiResponse {
  id: string
  name: string
  slug: string
  systemSchema: string
  dataSchema: string
  status: string
  settings?: RawProjectSettingsApiModel
  createdAt: string
  updatedAt: string
}

export interface RawProjectSettingsApiModel {
  defaultLocale?: string
  timezone?: string
  enableAuditLog?: boolean
  metadata?: Record<string, string>
}

export interface RawCreateProjectApiRequest {
  name: string
  slug?: string
  settings?: RawProjectSettingsApiModel
}

export interface RawUpdateProjectApiRequest {
  name?: string
  settings?: RawProjectSettingsApiModel
}

export interface RawProjectStatsApiResponse {
  projectId: string
  totalTableCount: number
  totalSizeBytes: number
  systemSchemaStats: RawSchemaStatsApiResponse
  dataSchemaStats: RawSchemaStatsApiResponse
}

export interface RawSchemaStatsApiResponse {
  schemaName: string
  tableCount: number
  indexCount: number
  dataSizeBytes: number
  indexSizeBytes: number
  totalSizeBytes: number
  lastModified?: string
}

export interface RawSchemaHealthApiResponse {
  projectId: string
  isHealthy: boolean
  issues: RawSchemaHealthIssueApiResponse[]
  checkedAt: string
}

export interface RawSchemaHealthIssueApiResponse {
  severity: string
  code: string
  message: string
  affectedObject: string
}

// =============================================================================
// TABLE TYPES
// =============================================================================

export interface RawTableApiResponse {
  id: string
  name: string
  version: number
  createdAt: string
  updatedAt: string
  columns: RawColumnApiResponse[]
  indexes: RawIndexApiResponse[]
  relations: RawRelationApiResponse[]
  systemColumns?: RawSystemColumnOptionsApiResponse
}

export interface RawColumnApiResponse {
  id: string
  name: string
  type: string
  nullable: boolean
  unique: boolean
  indexed: boolean
  primaryKey: boolean
  default?: string
  position: number
  isDerived: boolean
  lookup?: RawLookupConfigApiResponse
  rollup?: RawRollupConfigApiResponse
  formula?: RawFormulaConfigApiResponse
}

export interface RawIndexApiResponse {
  id: string
  name: string
  columns: string[]
  type: string
  unique: boolean
}

export interface RawRelationApiResponse {
  id: string
  name: string
  sourceTableId: string
  sourceColumnId: string
  targetTableId: string
  targetColumnId: string
  type: string
  onDelete: string
}

export interface RawSystemColumnOptionsApiResponse {
  timestamps: boolean
  versioning: boolean
  softDelete: boolean
  auditFields: boolean
  hierarchy: boolean
  ownership: boolean
  sourceTracking: boolean
}

// =============================================================================
// COLUMN CONFIG TYPES
// =============================================================================

export interface RawLookupConfigApiResponse {
  targetTable: string
  targetColumn: string
  relationColumn: string
  allowMultiple: boolean
  onDelete: string
}

export interface RawRollupConfigApiResponse {
  targetTable: string
  sourceColumn: string
  foreignKeyColumn: string
  relation: string
  aggregation: string
  filter?: RawRollupFilterApiResponse
  orderBy?: string
  delimiter?: string
}

export interface RawRollupFilterApiResponse {
  field: string
  operator: string
  value: unknown
}

export interface RawFormulaConfigApiResponse {
  formula: string
  returnType: string
  outputFormat?: string
  dependencies: string[]
  isVolatile: boolean
}

// =============================================================================
// REQUEST TYPES
// =============================================================================

export interface RawCreateTableApiRequest {
  name: string
  columns?: RawCreateColumnApiRequest[]
  systemColumns?: RawSystemColumnOptionsApiRequest
}

export interface RawCreateColumnApiRequest {
  name: string
  type: string
  nullable?: boolean
  unique?: boolean
  indexed?: boolean
  default?: string
  lookup?: RawLookupConfigApiRequest
  rollup?: RawRollupConfigApiRequest
  formula?: RawFormulaConfigApiRequest
}

export interface RawAddColumnApiRequest {
  name: string
  type: string
  nullable?: boolean
  unique?: boolean
  indexed?: boolean
  default?: string
  lookup?: RawLookupConfigApiRequest
  rollup?: RawRollupConfigApiRequest
  formula?: RawFormulaConfigApiRequest
}

export interface RawUpdateColumnApiRequest {
  name?: string
  default?: string
  version: number
}

export interface RawUpdateTableApiRequest {
  name?: string
  version: number
}

export interface RawSystemColumnOptionsApiRequest {
  versioning?: boolean
  softDelete?: boolean
  auditFields?: boolean
  hierarchy?: boolean
  ownership?: boolean
  sourceTracking?: boolean
}

export interface RawLookupConfigApiRequest {
  targetTable: string
  targetColumn: string
  relationColumn: string
  allowMultiple?: boolean
  onDelete?: string
}

export interface RawRollupConfigApiRequest {
  targetTable: string
  sourceColumn: string
  foreignKeyColumn: string
  relation: string
  aggregation: string
  filter?: RawRollupFilterApiRequest
  orderBy?: string
  delimiter?: string
}

export interface RawRollupFilterApiRequest {
  field: string
  operator: string
  value: unknown
}

export interface RawFormulaConfigApiRequest {
  formula: string
  returnType: string
  outputFormat?: string
}

// =============================================================================
// INDEX TYPES
// =============================================================================

export interface RawCreateIndexApiRequest {
  name: string
  columns: string[]
  type?: string
  unique?: boolean
  where?: string
}

// =============================================================================
// RELATION TYPES
// =============================================================================

export interface RawCreateRelationApiRequest {
  name: string
  sourceTable: string
  sourceColumn: string
  targetTable: string
  targetColumn: string
  type: string
  onDelete?: string
}

// =============================================================================
// VIEW TYPES
// =============================================================================

export interface RawViewApiResponse {
  id: string
  name: string
  baseTable: string
  columns: RawViewColumnApiResponse[]
  filters: RawViewFilterApiSpec[]
  joins: RawViewJoinApiSpec[]
  groupBy: string[]
  orderBy: RawViewOrderApiSpec[]
  limit?: number
  distinct: boolean
  isMaterialized: boolean
  isStale: boolean
  refreshPolicy?: string
  refreshSchedule?: string
  lastRefreshedAt?: string
  createdAt: string
  updatedAt: string
}

export interface RawViewColumnApiResponse {
  name: string
  dataType: string
  expression?: string
  isComputed: boolean
}

export interface RawViewFilterApiSpec {
  field: string
  operator: string
  value: unknown
  logicalOp?: string
}

export interface RawViewJoinApiSpec {
  table: string
  alias?: string
  joinType: string
  condition: string
}

export interface RawViewOrderApiSpec {
  column: string
  descending: boolean
  nullOrdering?: string
}

export interface RawCreateViewApiRequest {
  name: string
  baseTable: string
  columns: RawViewColumnApiSpec[]
  filters?: RawViewFilterApiSpec[]
  joins?: RawViewJoinApiSpec[]
  groupBy?: string[]
  orderBy?: RawViewOrderApiSpec[]
  limit?: number
  distinct?: boolean
  materialized?: boolean
  refreshPolicy?: string
  refreshSchedule?: string
  description?: string
}

export interface RawViewColumnApiSpec {
  source: string
  alias?: string
  expression?: string
  dataType?: string
  aggregation?: string
}

export interface RawUpdateViewApiRequest {
  name?: string
  columns?: RawViewColumnApiSpec[]
  filters?: RawViewFilterApiSpec[]
  joins?: RawViewJoinApiSpec[]
  groupBy?: string[]
  orderBy?: RawViewOrderApiSpec[]
  limit?: number
  distinct?: boolean
  refreshPolicy?: string
  refreshSchedule?: string
  description?: string
}

export interface RawViewQueryApiResponse {
  data: Record<string, unknown>[]
  totalCount: number
  hasMore: boolean
}

// =============================================================================
// WEBHOOK TYPES
// =============================================================================

export interface RawWebhookApiResponse {
  id: string
  name: string
  table: string
  url: string
  events: string[]
  headers: Record<string, string>
  filter?: string
  isActive: boolean
  secret: string
  createdAt: string
  updatedAt: string
}

export interface RawCreateWebhookApiRequest {
  name: string
  table: string
  url: string
  events: string[]
  headers?: Record<string, string>
  filter?: string
}

export interface RawUpdateWebhookApiRequest {
  url?: string
  events?: string[]
  headers?: Record<string, string>
  filter?: string
  isActive?: boolean
}

export interface RawDeliveryApiResponse {
  id: string
  event: string
  recordId: string
  status: string
  attemptCount: number
  httpStatusCode?: number
  errorMessage?: string
  createdAt: string
  deliveredAt?: string
}

export interface RawDlqMessageApiResponse {
  dlqId: string
  webhookId: string
  deliveryId: string
  event: string
  recordId: string
  status: string
  reason: string
  attemptCount: number
  lastHttpStatusCode?: number
  lastErrorMessage?: string
  dlqAt: string
  resolvedAt?: string
  resolutionNotes?: string
}

export interface RawDlqStatisticsApiResponse {
  totalMessages: number
  pendingReviewCount: number
  resolvedCount: number
  archivedCount: number
  oldestPendingAt?: string
  byReason: Record<string, number>
  byWebhook: Record<string, number>
}

// =============================================================================
// DATA TYPES
// =============================================================================

export interface RawDataRecordResponse {
  id: string
  data: Record<string, unknown>
}

export interface RawDataQueryParameters {
  select?: string
  filter?: string
  orderBy?: string
  page?: number
  pageSize?: number
}

// =============================================================================
// BATCH TYPES
// =============================================================================

export interface RawBatchRequest {
  operations: RawBatchOperation[]
}

export interface RawBatchOperation {
  method: string
  table: string
  id?: string
  data?: Record<string, unknown>
  filter?: string
  keyColumns?: string[]
}

export interface RawBatchResponse {
  successCount: number
  failureCount: number
  results: RawBatchOperationResult[]
}

export interface RawBatchOperationResult {
  index: number
  success: boolean
  data?: Record<string, unknown>
  affectedRows?: number
  error?: string
}

// =============================================================================
// AGGREGATION TYPES
// =============================================================================

export interface RawAggregationApiRequest {
  aggregations: RawAggregationColumnApiRequest[]
  groupBy?: string[]
  filter?: string
  having?: RawHavingConditionApiRequest[]
  orderBy?: RawAggregationOrderByApiRequest[]
  limit?: number
  offset?: number
}

export interface RawAggregationColumnApiRequest {
  function: string
  column?: string
  alias: string
  distinct?: boolean
}

export interface RawHavingConditionApiRequest {
  alias: string
  operator: string
  value: number
}

export interface RawAggregationOrderByApiRequest {
  column: string
  direction?: string
}

export interface RawAggregationApiResponse {
  data: Record<string, unknown>[]
  totalGroups: number
  metadata: RawAggregationMetadataApiResponse
}

export interface RawAggregationMetadataApiResponse {
  rowsScanned: number
  executionTimeMs: number
}

// =============================================================================
// IMPORT/EXPORT TYPES
// =============================================================================

export interface RawExportJobApiResponse {
  jobId: string
  tableName: string
  format: string
  status: string
  createdAt: string
  startedAt?: string
  completedAt?: string
  expiresAt?: string
  totalRows: number
  processedRows: number
  percentComplete: number
  fileSize?: number
  errorMessage?: string
}

export interface RawImportJobApiResponse {
  jobId: string
  tableName: string
  format: string
  status: string
  createdAt: string
  startedAt?: string
  completedAt?: string
  totalRows: number
  processedRows: number
  successCount: number
  errorCount: number
  percentComplete: number
  errorMessage?: string
}

export interface RawCsvExportApiRequest {
  columns?: string[]
  filter?: string
  orderBy?: string
  delimiter?: string
  includeHeader?: boolean
  dateFormat?: string
}

export interface RawCsvImportApiRequest {
  delimiter?: string
  hasHeader?: boolean
  trimWhitespace?: boolean
  dateFormat?: string
  duplicateHandling?: string
  keyColumns?: string[]
  nullHandling?: string
}

export interface RawJsonExportApiRequest {
  columns?: string[]
  filter?: string
  orderBy?: string
  pretty?: boolean
  dateFormat?: string
}

export interface RawJsonImportApiRequest {
  dateFormat?: string
  duplicateHandling?: string
  keyColumns?: string[]
}

export interface RawXlsxExportApiRequest {
  columns?: string[]
  filter?: string
  orderBy?: string
  sheetName?: string
  includeHeader?: boolean
}
