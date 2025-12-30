/**
 * MorphDB API Converters
 *
 * Converts between Raw API types (matching server exactly) and
 * internal app types (used throughout the desk application).
 */

import type {
  RawTableApiResponse,
  RawColumnApiResponse,
  RawIndexApiResponse,
  RawRelationApiResponse,
  RawSystemColumnOptionsApiResponse,
  RawProjectApiResponse,
  RawProjectStatsApiResponse,
  RawSchemaHealthApiResponse,
  RawViewApiResponse,
  RawWebhookApiResponse,
  RawDeliveryApiResponse,
  RawDlqMessageApiResponse,
  RawDlqStatisticsApiResponse,
  RawExportJobApiResponse,
  RawImportJobApiResponse,
  RawAggregationApiResponse,
  RawBatchResponse,
  RawPagedResponse,
} from './api-types'

// =============================================================================
// INTERNAL TYPES (used throughout the app)
// =============================================================================

// Keep internal types compatible with existing code
export interface TableApiResponse {
  id: string
  name: string
  displayName?: string
  description?: string
  version: number
  createdAt: string
  updatedAt: string
  columns: ColumnApiResponse[]
  indexes: IndexApiResponse[]
  relations: RelationApiResponse[]
  systemColumns?: SystemColumnOptionsResponse
}

export interface ColumnApiResponse {
  id: string
  name: string
  displayName: string
  dataType: string
  isNullable: boolean
  isUnique: boolean
  isIndexed: boolean
  isPrimaryKey: boolean
  defaultValue?: string
  position: number
  isDerived: boolean
}

export interface IndexApiResponse {
  id: string
  name: string
  columns: string[]
  type: 'btree' | 'hash' | 'gin' | 'gist'
  unique: boolean
}

export interface RelationApiResponse {
  id: string
  name: string
  sourceTableId: string
  sourceColumnId: string
  targetTableId: string
  targetColumnId: string
  type: 'one-to-one' | 'one-to-many' | 'many-to-one' | 'many-to-many'
  onDelete: 'no-action' | 'cascade' | 'set-null' | 'restrict'
}

export interface SystemColumnOptionsResponse {
  enableId: boolean
  enableCreatedAt: boolean
  enableUpdatedAt: boolean
  enableVersion: boolean
  enableSoftDelete: boolean
  enableAuditFields: boolean
  enableHierarchy: boolean
  enableOwnership: boolean
  enableSourceTracking: boolean
}

export interface ProjectApiResponse {
  id: string
  organizationId?: string
  name: string
  slug: string
  systemSchema: string
  dataSchema: string
  status: string
  settings?: ProjectSettingsResponse
  createdAt: string
  updatedAt: string
}

export interface ProjectSettingsResponse {
  defaultLocale?: string
  timezone?: string
  maxTables?: number
  maxStorageBytes?: number
  enableAuditLog?: boolean
  rateLimits?: RateLimitSettingsResponse
  metadata?: Record<string, string>
}

export interface RateLimitSettingsResponse {
  requestsPerMinute?: number
  requestsPerHour?: number
  maxConcurrentConnections?: number
}

export interface ProjectStatsResponse {
  projectId: string
  totalTableCount: number
  totalSizeBytes: number
  tableCount: number
  totalRows: number
  schemaSizeBytes: number
  dataSizeBytes: number
}

export interface SchemaHealthResponse {
  projectId: string
  isHealthy: boolean
  issues: SchemaHealthIssue[]
  checkedAt: string
}

export interface SchemaHealthIssue {
  severity: 'Warning' | 'Error' | 'Critical'
  code: string
  message: string
  affectedObject: string
}

export interface ViewApiResponse {
  id: string
  name: string
  baseTable: string
  columns: ViewColumnResponse[]
  filters: ViewFilterSpec[]
  joins: ViewJoinSpec[]
  groupBy: string[]
  orderBy: ViewOrderSpec[]
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

export interface ViewColumnResponse {
  name: string
  dataType: string
  expression?: string
  isComputed: boolean
}

export interface ViewFilterSpec {
  field: string
  operator: string
  value: unknown
  logicalOp?: string
}

export interface ViewJoinSpec {
  table: string
  alias?: string
  joinType: string
  condition: string
}

export interface ViewOrderSpec {
  column: string
  descending: boolean
  nullOrdering?: string
}

export interface WebhookApiResponse {
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

export interface DeliveryApiResponse {
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

export interface DlqMessageApiResponse {
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

export interface DlqStatisticsApiResponse {
  totalMessages: number
  pendingReviewCount: number
  resolvedCount: number
  archivedCount: number
  oldestPendingAt?: string
  byReason: Record<string, number>
  byWebhook: Record<string, number>
}

export interface ExportJobApiResponse {
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

export interface ImportJobApiResponse {
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

export interface AggregationApiResponse {
  data: Record<string, unknown>[]
  totalGroups: number
  metadata: {
    rowsScanned: number
    executionTimeMs: number
  }
}

export interface BatchResponse {
  successCount: number
  failureCount: number
  results: BatchOperationResult[]
}

export interface BatchOperationResult {
  index: number
  success: boolean
  data?: Record<string, unknown>
  affectedRows?: number
  error?: string
}

export interface PagedResponse<T> {
  data: T[]
  pagination: {
    page: number
    pageSize: number
    totalCount: number
    totalPages: number
    hasNext: boolean
    hasPrevious: boolean
  }
}

// =============================================================================
// CONVERTERS
// =============================================================================

export function toColumnApiResponse(raw: RawColumnApiResponse): ColumnApiResponse {
  return {
    id: raw.id,
    name: raw.name,
    displayName: raw.name,
    dataType: raw.type,
    isNullable: raw.nullable,
    isUnique: raw.unique,
    isIndexed: raw.indexed,
    isPrimaryKey: raw.primaryKey,
    defaultValue: raw.default,
    position: raw.position,
    isDerived: raw.isDerived
  }
}

export function toIndexApiResponse(raw: RawIndexApiResponse): IndexApiResponse {
  return {
    id: raw.id,
    name: raw.name,
    columns: raw.columns,
    type: raw.type as IndexApiResponse['type'],
    unique: raw.unique
  }
}

export function toRelationApiResponse(raw: RawRelationApiResponse): RelationApiResponse {
  return {
    id: raw.id,
    name: raw.name,
    sourceTableId: raw.sourceTableId,
    sourceColumnId: raw.sourceColumnId,
    targetTableId: raw.targetTableId,
    targetColumnId: raw.targetColumnId,
    type: raw.type as RelationApiResponse['type'],
    onDelete: raw.onDelete as RelationApiResponse['onDelete']
  }
}

export function toSystemColumnOptionsResponse(raw: RawSystemColumnOptionsApiResponse): SystemColumnOptionsResponse {
  return {
    enableId: true, // Always enabled
    enableCreatedAt: raw.timestamps,
    enableUpdatedAt: raw.timestamps,
    enableVersion: raw.versioning,
    enableSoftDelete: raw.softDelete,
    enableAuditFields: raw.auditFields,
    enableHierarchy: raw.hierarchy,
    enableOwnership: raw.ownership,
    enableSourceTracking: raw.sourceTracking
  }
}

export function toTableApiResponse(raw: RawTableApiResponse): TableApiResponse {
  return {
    id: raw.id,
    name: raw.name,
    version: raw.version,
    createdAt: raw.createdAt,
    updatedAt: raw.updatedAt,
    columns: raw.columns.map(toColumnApiResponse),
    indexes: raw.indexes.map(toIndexApiResponse),
    relations: raw.relations.map(toRelationApiResponse),
    systemColumns: raw.systemColumns ? toSystemColumnOptionsResponse(raw.systemColumns) : undefined
  }
}

export function toProjectApiResponse(raw: RawProjectApiResponse): ProjectApiResponse {
  return {
    id: raw.id,
    organizationId: raw.organizationId,
    name: raw.name,
    slug: raw.slug,
    systemSchema: raw.systemSchema,
    dataSchema: raw.dataSchema,
    status: raw.status,
    settings: raw.settings ? {
      defaultLocale: raw.settings.defaultLocale,
      timezone: raw.settings.timezone,
      maxTables: raw.settings.maxTables,
      maxStorageBytes: raw.settings.maxStorageBytes,
      enableAuditLog: raw.settings.enableAuditLog,
      rateLimits: raw.settings.rateLimits,
      metadata: raw.settings.metadata
    } : undefined,
    createdAt: raw.createdAt,
    updatedAt: raw.updatedAt
  }
}

export function toProjectStatsResponse(raw: RawProjectStatsApiResponse): ProjectStatsResponse {
  return {
    projectId: raw.projectId,
    totalTableCount: raw.totalTableCount,
    totalSizeBytes: raw.totalSizeBytes,
    tableCount: raw.dataSchemaStats.tableCount,
    totalRows: 0, // Not in raw response
    schemaSizeBytes: raw.systemSchemaStats.totalSizeBytes,
    dataSizeBytes: raw.dataSchemaStats.totalSizeBytes
  }
}

export function toSchemaHealthResponse(raw: RawSchemaHealthApiResponse): SchemaHealthResponse {
  return {
    projectId: raw.projectId,
    isHealthy: raw.isHealthy,
    issues: raw.issues.map(issue => ({
      severity: issue.severity as SchemaHealthIssue['severity'],
      code: issue.code,
      message: issue.message,
      affectedObject: issue.affectedObject
    })),
    checkedAt: raw.checkedAt
  }
}

export function toViewApiResponse(raw: RawViewApiResponse): ViewApiResponse {
  return {
    id: raw.id,
    name: raw.name,
    baseTable: raw.baseTable,
    columns: raw.columns.map(col => ({
      name: col.name,
      dataType: col.dataType,
      expression: col.expression,
      isComputed: col.isComputed
    })),
    filters: raw.filters,
    joins: raw.joins,
    groupBy: raw.groupBy,
    orderBy: raw.orderBy,
    limit: raw.limit,
    distinct: raw.distinct,
    isMaterialized: raw.isMaterialized,
    isStale: raw.isStale,
    refreshPolicy: raw.refreshPolicy,
    refreshSchedule: raw.refreshSchedule,
    lastRefreshedAt: raw.lastRefreshedAt,
    createdAt: raw.createdAt,
    updatedAt: raw.updatedAt
  }
}

export function toWebhookApiResponse(raw: RawWebhookApiResponse): WebhookApiResponse {
  return { ...raw }
}

export function toDeliveryApiResponse(raw: RawDeliveryApiResponse): DeliveryApiResponse {
  return { ...raw }
}

export function toDlqMessageApiResponse(raw: RawDlqMessageApiResponse): DlqMessageApiResponse {
  return { ...raw }
}

export function toDlqStatisticsApiResponse(raw: RawDlqStatisticsApiResponse): DlqStatisticsApiResponse {
  return { ...raw }
}

export function toExportJobApiResponse(raw: RawExportJobApiResponse): ExportJobApiResponse {
  return { ...raw }
}

export function toImportJobApiResponse(raw: RawImportJobApiResponse): ImportJobApiResponse {
  return { ...raw }
}

export function toAggregationApiResponse(raw: RawAggregationApiResponse): AggregationApiResponse {
  return {
    data: raw.data,
    totalGroups: raw.totalGroups,
    metadata: {
      rowsScanned: raw.metadata.rowsScanned,
      executionTimeMs: raw.metadata.executionTimeMs
    }
  }
}

export function toBatchResponse(raw: RawBatchResponse): BatchResponse {
  return {
    successCount: raw.successCount,
    failureCount: raw.failureCount,
    results: raw.results
  }
}

export function toPagedResponse<TRaw, TInternal>(
  raw: RawPagedResponse<TRaw>,
  converter: (item: TRaw) => TInternal
): PagedResponse<TInternal> {
  return {
    data: raw.data.map(converter),
    pagination: {
      page: raw.pagination.page,
      pageSize: raw.pagination.pageSize,
      totalCount: raw.pagination.totalCount,
      totalPages: raw.pagination.totalPages,
      hasNext: raw.pagination.hasNext,
      hasPrevious: raw.pagination.hasPrevious
    }
  }
}
