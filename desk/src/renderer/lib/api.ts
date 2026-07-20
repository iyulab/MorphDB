import type { TableInfo, ColumnInfo } from '@/types/connection'

// Import raw types from centralized schema
import type {
  RawTableApiResponse,
  RawColumnApiResponse,
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

// Import converters
import {
  toTableApiResponse,
  toColumnApiResponse,
  toProjectApiResponse,
  toProjectStatsResponse,
  toSchemaHealthResponse,
  toViewApiResponse,
  toWebhookApiResponse,
  toDeliveryApiResponse,
  toDlqMessageApiResponse,
  toDlqStatisticsApiResponse,
  toExportJobApiResponse,
  toImportJobApiResponse,
  toAggregationApiResponse,
  toBatchResponse,
  toPagedResponse,
} from './api-converters'

// Import internal types for use within this file
import type {
  TableApiResponse,
  ColumnApiResponse,
  IndexApiResponse,
  RelationApiResponse,
  SystemColumnOptionsResponse,
  ProjectApiResponse,
  ProjectSettingsResponse,
  ProjectStatsResponse,
  SchemaHealthResponse,
  SchemaHealthIssue,
  ViewApiResponse,
  ViewColumnResponse,
  ViewFilterSpec,
  ViewJoinSpec,
  ViewOrderSpec,
  WebhookApiResponse,
  DeliveryApiResponse,
  DlqMessageApiResponse,
  DlqStatisticsApiResponse,
  ExportJobApiResponse,
  ImportJobApiResponse,
  AggregationApiResponse,
  BatchResponse,
  BatchOperationResult,
  PagedResponse,
} from './api-converters'

// Re-export internal types for use throughout the app
export type {
  TableApiResponse,
  ColumnApiResponse,
  IndexApiResponse,
  RelationApiResponse,
  SystemColumnOptionsResponse,
  ProjectApiResponse,
  ProjectSettingsResponse,
  ProjectStatsResponse,
  SchemaHealthResponse,
  SchemaHealthIssue,
  ViewApiResponse,
  ViewColumnResponse,
  ViewFilterSpec,
  ViewJoinSpec,
  ViewOrderSpec,
  WebhookApiResponse,
  DeliveryApiResponse,
  DlqMessageApiResponse,
  DlqStatisticsApiResponse,
  ExportJobApiResponse,
  ImportJobApiResponse,
  AggregationApiResponse,
  BatchResponse,
  BatchOperationResult,
  PagedResponse,
} from './api-converters'

export interface ApiError {
  error: string
  message: string
  code?: string
}

// Project-related type aliases
export type ProjectStatus = 'Active' | 'Suspended' | 'Archived'
export type ProjectSettings = {
  defaultLocale?: string
  timezone?: string
  enableAuditLog?: boolean
  metadata?: Record<string, string>
}

export interface CreateProjectRequest {
  name: string
  slug?: string
  settings?: ProjectSettings
}

export interface UpdateProjectRequest {
  name?: string
  settings?: ProjectSettings
}

// Note: ProjectStatsResponse, SchemaHealthResponse, SchemaHealthIssue
// are now imported from api-converters

// Aggregation types
export type AggregationFunction = 'count' | 'sum' | 'avg' | 'min' | 'max'

export interface AggregationItem {
  function: AggregationFunction
  column?: string // Not required for COUNT(*)
  alias: string
}

export interface FilterConditionItem {
  column: string
  operator: 'eq' | 'ne' | 'gt' | 'gte' | 'lt' | 'lte' | 'contains' | 'startswith' | 'endswith'
  value: unknown
}

export interface HavingCondition {
  alias: string
  operator: 'eq' | 'ne' | 'gt' | 'gte' | 'lt' | 'lte'
  value: number
}

export interface OrderByItem {
  column: string
  direction: 'asc' | 'desc'
}

export interface AggregationRequest {
  aggregations: AggregationItem[]
  groupBy?: string[]
  filter?: FilterConditionItem[]
  having?: HavingCondition[]
  orderBy?: OrderByItem[]
  limit?: number
}

export interface AggregationResponse {
  data: Record<string, unknown>[]
  metadata: {
    executedAt: string
    rowCount: number
  }
}

// Batch operation types
export interface BatchOperation {
  method: 'INSERT' | 'UPDATE' | 'DELETE' | 'UPSERT'
  table: string
  id?: string
  data?: Record<string, unknown>
  keyColumns?: string[]
}

export interface BatchRequest {
  operations: BatchOperation[]
}

// Note: BatchOperationResult and BatchResponse are now imported from api-converters

export interface DataRecordResponse {
  id: string
  data: Record<string, unknown>
}

// Bulk import/export types
export type NullHandling = 'emptyAsNull' | 'preserveEmpty' | 'nullStringAsNull'
export type DuplicateHandling = 'insert' | 'update' | 'upsert' | 'skip' | 'error'

export interface CsvImportOptions {
  delimiter?: string
  hasHeader?: boolean
  skipRows?: number
  encoding?: string
  dateFormat?: string
  trimWhitespace?: boolean
  nullHandling?: NullHandling
  duplicateHandling?: DuplicateHandling
  keyColumns?: string[]
}

export interface JsonImportOptions {
  rootPath?: string
  flattenNested?: boolean
  dateFormat?: string
  duplicateHandling?: DuplicateHandling
  keyColumns?: string[]
}

export interface ImportErrorItem {
  row: number
  column?: string
  message: string
  value?: unknown
}

export interface ImportJobResponse {
  jobId: string
  tableName: string
  format: string
  status: 'pending' | 'processing' | 'completed' | 'failed' | 'cancelled'
  totalRows?: number
  processedRows: number
  successCount: number
  errorCount: number
  errors?: ImportErrorItem[]
  errorMessage?: string
  createdAt: string
  startedAt?: string
  completedAt?: string
}

export interface CsvExportOptions {
  delimiter?: string
  includeHeader?: boolean
  dateFormat?: string
  columns?: string[]
  filter?: string
  orderBy?: string
  limit?: number
}

export interface JsonExportOptions {
  pretty?: boolean
  arrayFormat?: boolean
  dateFormat?: string
  columns?: string[]
  filter?: string
  orderBy?: string
  limit?: number
}

export interface XlsxExportOptions {
  sheetName?: string
  includeHeader?: boolean
  columns?: string[]
  filter?: string
  orderBy?: string
  limit?: number
}

export interface ExportJobResponse {
  jobId: string
  tableName: string
  format: string
  status: 'pending' | 'processing' | 'completed' | 'failed' | 'cancelled'
  totalRows?: number
  rowCount?: number
  processedRows: number
  fileSize?: number
  errorMessage?: string
  createdAt: string
  startedAt?: string
  completedAt?: string
  expiresAt?: string
}

export interface JobProgressResponse {
  jobId: string
  status: string
  totalRows?: number
  processedRows: number
  successCount: number
  errorCount: number
  percentComplete: number
  estimatedTimeRemaining?: string
}

// Note: ViewApiResponse, ViewColumnResponse, ViewFilterSpec, ViewJoinSpec, ViewOrderSpec
// are now imported from api-converters

// Legacy aliases for backward compatibility
export type ViewColumnApiResponse = import('./api-converters').ViewColumnResponse
export type ViewJoinApiSpec = import('./api-converters').ViewJoinSpec
export type ViewFilterApiSpec = import('./api-converters').ViewFilterSpec
export type ViewOrderApiSpec = import('./api-converters').ViewOrderSpec

export interface ViewColumnApiSpec {
  source?: string
  expression?: string
  alias: string
  dataType?: string
  aggregation?: string
}

export interface CreateViewApiRequest {
  name: string
  baseTable: string
  columns: ViewColumnApiSpec[]
  joins?: ViewJoinApiSpec[]
  filters?: ViewFilterApiSpec[]
  groupBy?: string[]
  orderBy?: ViewOrderApiSpec[]
  limit?: number
  distinct?: boolean
  materialized?: boolean
  refreshPolicy?: string
  refreshSchedule?: string
  description?: string
}

export interface UpdateViewApiRequest {
  name?: string
  columns?: ViewColumnApiSpec[]
  joins?: ViewJoinApiSpec[]
  filters?: ViewFilterApiSpec[]
  groupBy?: string[]
  orderBy?: ViewOrderApiSpec[]
  limit?: number
  distinct?: boolean
  refreshPolicy?: string
  refreshSchedule?: string
  description?: string
}

export interface ViewQueryApiResponse {
  data: Record<string, unknown>[]
  totalCount: number
  hasMore: boolean
}

export interface ViewStaleResponse {
  isStale: boolean
  lastRefreshedAt?: string
}

// Note: WebhookApiResponse, DeliveryApiResponse, DlqMessageApiResponse, DlqStatisticsApiResponse
// are now imported from api-converters

// Legacy alias for backward compatibility
export type WebhookDeliveryApiResponse = import('./api-converters').DeliveryApiResponse

// Webhook request types (not in api-converters)
export interface CreateWebhookApiRequest {
  name: string
  table: string
  url: string
  events?: string[]
  filter?: Record<string, unknown>
  headers?: Record<string, string>
}

export interface UpdateWebhookApiRequest {
  url?: string
  events?: string[]
  filter?: Record<string, unknown>
  headers?: Record<string, string>
  isActive?: boolean
}

export interface ResolveDlqApiRequest {
  resolutionNotes: string
  resolvedBy?: string
}

export interface ArchiveDlqApiResponse {
  archivedCount: number
}

// Audit types
export type AuditCategory = 'auth' | 'data' | 'schema' | 'admin' | 'security' | 'system'
export type AuditSeverity = 'debug' | 'info' | 'warning' | 'error' | 'critical'

export interface AuditLogQueryParams {
  category?: number // 0=Auth, 1=Data, 2=Schema, 3=Admin, 4=Security, 5=System
  minSeverity?: number // 0=Debug, 1=Info, 2=Warning, 3=Error, 4=Critical
  actorId?: string
  resourceType?: string
  resourceId?: string
  action?: string
  from?: string
  to?: string
  searchText?: string
  page?: number
  pageSize?: number
  orderBy?: string
  descending?: boolean
}

export interface AuditLogEntryApiResponse {
  id: string
  projectId: string
  category: AuditCategory
  action: string
  severity: AuditSeverity
  actorId?: string
  actorType?: string
  resourceType?: string
  resourceId?: string
  httpMethod?: string
  requestPath?: string
  statusCode?: number
  ipAddress?: string
  userAgent?: string
  durationMs?: number
  metadata?: Record<string, unknown>
  errorMessage?: string
  timestamp: string
}

export interface AuditLogPageApiResponse {
  items: AuditLogEntryApiResponse[]
  totalCount: number
  page: number
  pageSize: number
  totalPages: number
  hasMore: boolean
}

export interface ActorStatsApiResponse {
  actorId: string
  actorType?: string
  eventCount: number
}

export interface ActionStatsApiResponse {
  action: string
  eventCount: number
  avgDurationMs?: number
}

export interface AuditStatsApiResponse {
  totalEvents: number
  byCategory: Record<string, number>
  bySeverity: Record<string, number>
  topActors: ActorStatsApiResponse[]
  topActions: ActionStatsApiResponse[]
  errorRate: number
  from: string
  to: string
}

// ============================================================================
// Security Types - API Keys
// ============================================================================

export type ApiKeyType = 'anon' | 'service'

export interface ApiKeyApiResponse {
  id: string
  name: string
  keyType: ApiKeyType
  keyPrefix: string
  description?: string
  isActive: boolean
  createdAt: string
  expiresAt?: string
  lastUsedAt?: string
}

export interface CreateApiKeyApiRequest {
  name: string
  keyType: ApiKeyType
  description?: string
  expiresAt?: string
}

export interface CreateApiKeyApiResponse {
  key: ApiKeyApiResponse
  rawKey: string
}

// ============================================================================
// Security Types - RLS Policies
// ============================================================================

export type PolicyType = 'select' | 'insert' | 'update' | 'delete' | 'all'

export interface SecurityPolicyApiResponse {
  id: string
  name: string
  tableId: string
  policyType: PolicyType
  expression: string
  description?: string
  isActive: boolean
  ordinalPosition: number
  createdAt: string
  updatedAt: string
}

export interface CreateSecurityPolicyApiRequest {
  name: string
  tableName: string
  policyType: PolicyType
  expression: string
  description?: string
}

export interface UpdateSecurityPolicyApiRequest {
  name?: string
  expression?: string
  isActive?: boolean
  description?: string
}

// ============================================================================
// Security Types - Encryption
// ============================================================================

export interface EncryptionInfoApiResponse {
  enabled: boolean
  currentKeyVersion: number
  availableKeyVersions: number[]
}

export interface KeyRotationResultApiResponse {
  success: boolean
  tableName: string
  previousKeyVersion: number
  newKeyVersion: number
  rowsProcessed: number
  columnsRotated: number
  durationMs: number
  errorMessage?: string
  startedAt: string
  completedAt: string
}

export interface KeyRotationStatusApiResponse {
  state: string
  tableName: string
  currentKeyVersion: number
  targetKeyVersion?: number
  progressPercent: number
  rowsProcessed: number
  totalRows: number
  estimatedTimeRemainingMs?: number
  startedAt?: string
  lastRotatedAt?: string
}

export interface KeyValidationResultApiResponse {
  isValid: boolean
  tableName: string
  expectedKeyVersion: number
  totalEncryptedValues: number
  currentVersionCount: number
  oldVersionCount: number
  unencryptedCount: number
  versionBreakdown: Record<string, number>
}

// Import derived field config types from api-types for backward compatibility
import type {
  RawLookupConfigApiResponse as LookupConfigResponse,
  RawRollupConfigApiResponse as RollupConfigResponse,
  RawFormulaConfigApiResponse as FormulaConfigResponse,
} from './api-types'

export type { LookupConfigResponse, RollupConfigResponse, FormulaConfigResponse }

// Request DTOs
export interface CreateTableRequest {
  name: string
  displayName?: string
  description?: string
  columns: CreateColumnRequest[]
}

export interface CreateColumnRequest {
  name: string
  type: string
  nullable?: boolean
  unique?: boolean
  indexed?: boolean
  default?: string
  lookup?: LookupConfigRequest
  rollup?: RollupConfigRequest
  formula?: FormulaConfigRequest
}

export interface LookupConfigRequest {
  relationId: string
  sourceColumnName: string
}

export interface RollupConfigRequest {
  relationId: string
  sourceColumnName: string
  aggregation: string
  filterExpression?: string
}

export interface FormulaConfigRequest {
  expression: string
  outputType: string
}

export interface UpdateColumnRequest {
  name?: string
  default?: string
  version: number
}

export interface RenameTableRequest {
  newName: string
}

export interface CreateIndexRequest {
  name: string
  columns: string[]
  type?: 'btree' | 'hash' | 'gin' | 'gist'
  unique?: boolean
  where?: string
}

export interface CreateRelationRequest {
  name: string
  sourceTable: string
  sourceColumn: string
  targetTable: string
  targetColumn: string
  type?: 'one-to-one' | 'one-to-many' | 'many-to-one' | 'many-to-many'
  onDelete?: 'no-action' | 'cascade' | 'set-null' | 'restrict'
}

export interface ConnectionConfig {
  url: string
  apiKey: string
  tenantId?: string
}

export class MorphDBClient {
  private baseUrl: string
  private apiKey: string
  private tenantId?: string

  constructor(config: ConnectionConfig) {
    this.baseUrl = config.url.replace(/\/$/, '')
    this.apiKey = config.apiKey
    this.tenantId = config.tenantId
  }

  private async request<T>(
    path: string,
    options: RequestInit = {}
  ): Promise<T> {
    const url = `${this.baseUrl}${path}`
    const headers: Record<string, string> = {
      'Content-Type': 'application/json',
      'X-API-Key': this.apiKey,
      ...(this.tenantId && { 'X-Tenant-Id': this.tenantId }),
      ...(options.headers as Record<string, string>)
    }

    const response = await fetch(url, {
      ...options,
      headers
    })

    if (!response.ok) {
      const error: ApiError = await response.json().catch(() => ({
        error: 'UnknownError',
        message: `Request failed with status ${response.status}`
      }))
      throw new Error(error.message || error.error)
    }

    return response.json()
  }

  // Projects
  async listProjects(status?: ProjectStatus): Promise<ProjectApiResponse[]> {
    const params = new URLSearchParams()
    if (status) params.append('status', status)
    const query = params.toString() ? `?${params.toString()}` : ''
    const response = await this.request<PagedResponse<ProjectApiResponse>>(`/api/projects${query}`)
    return response.data
  }

  async getProject(id: string): Promise<ProjectApiResponse> {
    return this.request<ProjectApiResponse>(`/api/projects/${id}`)
  }

  async getProjectBySlug(slug: string): Promise<ProjectApiResponse> {
    return this.request<ProjectApiResponse>(`/api/projects/slug/${slug}`)
  }

  async createProject(data: CreateProjectRequest): Promise<ProjectApiResponse> {
    return this.request<ProjectApiResponse>('/api/projects', {
      method: 'POST',
      body: JSON.stringify(data)
    })
  }

  async updateProject(id: string, data: UpdateProjectRequest): Promise<ProjectApiResponse> {
    return this.request<ProjectApiResponse>(`/api/projects/${id}`, {
      method: 'PATCH',
      body: JSON.stringify(data)
    })
  }

  async deleteProject(id: string): Promise<void> {
    await this.request<void>(`/api/projects/${id}`, {
      method: 'DELETE'
    })
  }

  async suspendProject(id: string): Promise<void> {
    await this.request<void>(`/api/projects/${id}/suspend`, {
      method: 'POST'
    })
  }

  async reactivateProject(id: string): Promise<void> {
    await this.request<void>(`/api/projects/${id}/reactivate`, {
      method: 'POST'
    })
  }

  async archiveProject(id: string): Promise<void> {
    await this.request<void>(`/api/projects/${id}/archive`, {
      method: 'POST'
    })
  }

  async getProjectStats(id: string): Promise<ProjectStatsResponse> {
    return this.request<ProjectStatsResponse>(`/api/projects/${id}/stats`)
  }

  async validateProjectHealth(id: string): Promise<SchemaHealthResponse> {
    return this.request<SchemaHealthResponse>(`/api/projects/${id}/health`)
  }

  // Tables
  async listTables(tenantId?: string): Promise<TableApiResponse[]> {
    const headers: Record<string, string> = {}
    if (tenantId) {
      headers['X-Tenant-Id'] = tenantId
    }
    const raw = await this.request<RawTableApiResponse[]>('/api/schema/tables', { headers })
    return raw.map(toTableApiResponse)
  }

  async getTable(name: string, tenantId?: string): Promise<TableApiResponse> {
    const headers: Record<string, string> = {}
    if (tenantId) {
      headers['X-Tenant-Id'] = tenantId
    }
    const raw = await this.request<RawTableApiResponse>(`/api/schema/tables/${name}`, { headers })
    return toTableApiResponse(raw)
  }

  async createTable(data: CreateTableRequest, tenantId?: string): Promise<TableApiResponse> {
    const headers: Record<string, string> = {}
    if (tenantId) {
      headers['X-Tenant-Id'] = tenantId
    }
    const raw = await this.request<RawTableApiResponse>('/api/schema/tables', {
      method: 'POST',
      headers,
      body: JSON.stringify(data)
    })
    return toTableApiResponse(raw)
  }

  async renameTable(name: string, newName: string, tenantId?: string): Promise<TableApiResponse> {
    const headers: Record<string, string> = {}
    if (tenantId) {
      headers['X-Tenant-Id'] = tenantId
    }
    const raw = await this.request<RawTableApiResponse>(`/api/schema/tables/${name}/rename`, {
      method: 'POST',
      headers,
      body: JSON.stringify({ newName })
    })
    return toTableApiResponse(raw)
  }

  async deleteTable(name: string, tenantId?: string): Promise<void> {
    const headers: Record<string, string> = {}
    if (tenantId) {
      headers['X-Tenant-Id'] = tenantId
    }
    await this.request<void>(`/api/schema/tables/${name}`, {
      method: 'DELETE',
      headers
    })
  }

  // Columns
  async addColumn(tableName: string, data: CreateColumnRequest, tenantId?: string): Promise<ColumnApiResponse> {
    const headers: Record<string, string> = {}
    if (tenantId) {
      headers['X-Tenant-Id'] = tenantId
    }
    const raw = await this.request<RawColumnApiResponse>(`/api/schema/tables/${tableName}/columns`, {
      method: 'POST',
      headers,
      body: JSON.stringify(data)
    })
    return toColumnApiResponse(raw)
  }

  async updateColumn(
    tableName: string,
    columnName: string,
    data: UpdateColumnRequest,
    tenantId?: string
  ): Promise<ColumnApiResponse> {
    const headers: Record<string, string> = {}
    if (tenantId) {
      headers['X-Tenant-Id'] = tenantId
    }
    const raw = await this.request<RawColumnApiResponse>(`/api/schema/tables/${tableName}/columns/${columnName}`, {
      method: 'PUT',
      headers,
      body: JSON.stringify(data)
    })
    return toColumnApiResponse(raw)
  }

  async deleteColumn(tableName: string, columnName: string, tenantId?: string): Promise<void> {
    const headers: Record<string, string> = {}
    if (tenantId) {
      headers['X-Tenant-Id'] = tenantId
    }
    await this.request<void>(`/api/schema/tables/${tableName}/columns/${columnName}`, {
      method: 'DELETE',
      headers
    })
  }

  // Indexes
  async createIndex(
    tableName: string,
    data: CreateIndexRequest,
    tenantId?: string
  ): Promise<IndexApiResponse> {
    const headers: Record<string, string> = {}
    if (tenantId) {
      headers['X-Tenant-Id'] = tenantId
    }
    return this.request<IndexApiResponse>(`/api/schema/tables/${tableName}/indexes`, {
      method: 'POST',
      headers,
      body: JSON.stringify(data)
    })
  }

  async deleteIndex(indexId: string, tenantId?: string): Promise<void> {
    const headers: Record<string, string> = {}
    if (tenantId) {
      headers['X-Tenant-Id'] = tenantId
    }
    await this.request<void>(`/api/schema/indexes/${indexId}`, {
      method: 'DELETE',
      headers
    })
  }

  // Relations
  async createRelation(data: CreateRelationRequest, tenantId?: string): Promise<RelationApiResponse> {
    const headers: Record<string, string> = {}
    if (tenantId) {
      headers['X-Tenant-Id'] = tenantId
    }
    return this.request<RelationApiResponse>('/api/schema/relations', {
      method: 'POST',
      headers,
      body: JSON.stringify(data)
    })
  }

  async deleteRelation(relationId: string, tenantId?: string): Promise<void> {
    const headers: Record<string, string> = {}
    if (tenantId) {
      headers['X-Tenant-Id'] = tenantId
    }
    await this.request<void>(`/api/schema/relations/${relationId}`, {
      method: 'DELETE',
      headers
    })
  }

  // Data (OData operations)
  async queryData(
    tableName: string,
    options?: {
      $top?: number
      $skip?: number
      $orderby?: string
      $filter?: string
      $select?: string
      $count?: boolean
    }
  ): Promise<{ value: Record<string, unknown>[]; '@odata.count'?: number }> {
    const params = new URLSearchParams()
    if (options?.$top) params.append('$top', options.$top.toString())
    if (options?.$skip) params.append('$skip', options.$skip.toString())
    if (options?.$orderby) params.append('$orderby', options.$orderby)
    if (options?.$filter) params.append('$filter', options.$filter)
    if (options?.$select) params.append('$select', options.$select)
    if (options?.$count) params.append('$count', 'true')

    const query = params.toString() ? `?${params.toString()}` : ''
    return this.request<{ value: Record<string, unknown>[]; '@odata.count'?: number }>(
      `/odata/${tableName}${query}`
    )
  }

  async createRecord(
    tableName: string,
    data: Record<string, unknown>
  ): Promise<Record<string, unknown>> {
    return this.request<Record<string, unknown>>(`/odata/${tableName}`, {
      method: 'POST',
      body: JSON.stringify(data)
    })
  }

  async updateRecord(
    tableName: string,
    key: string,
    data: Record<string, unknown>
  ): Promise<void> {
    await this.request<void>(`/odata/${tableName}('${key}')`, {
      method: 'PATCH',
      body: JSON.stringify(data)
    })
  }

  async deleteRecord(tableName: string, key: string): Promise<void> {
    await this.request<void>(`/odata/${tableName}('${key}')`, {
      method: 'DELETE'
    })
  }

  // Aggregation
  async aggregate(
    tableName: string,
    request: AggregationRequest
  ): Promise<AggregationResponse> {
    return this.request<AggregationResponse>(`/api/data/${tableName}/aggregate`, {
      method: 'POST',
      body: JSON.stringify(request)
    })
  }

  // Batch Operations
  async executeBatch(request: BatchRequest): Promise<BatchResponse> {
    return this.request<BatchResponse>('/api/batch/data', {
      method: 'POST',
      body: JSON.stringify(request)
    })
  }

  async bulkInsert(
    tableName: string,
    records: Record<string, unknown>[]
  ): Promise<BatchResponse> {
    return this.request<BatchResponse>(`/api/batch/data/${tableName}/insert`, {
      method: 'POST',
      body: JSON.stringify(records)
    })
  }

  async bulkUpdate(
    tableName: string,
    data: Record<string, unknown>,
    filter?: string
  ): Promise<BatchResponse> {
    return this.request<BatchResponse>(`/api/batch/data/${tableName}`, {
      method: 'PATCH',
      body: JSON.stringify({ data, filter })
    })
  }

  async bulkDelete(tableName: string, filter: string): Promise<BatchResponse> {
    const params = new URLSearchParams({ filter })
    return this.request<BatchResponse>(`/api/batch/data/${tableName}?${params}`, {
      method: 'DELETE'
    })
  }

  async upsert(
    tableName: string,
    data: Record<string, unknown>,
    keyColumns: string[]
  ): Promise<DataRecordResponse> {
    return this.request<DataRecordResponse>(`/api/batch/data/${tableName}`, {
      method: 'PUT',
      body: JSON.stringify({ data, keyColumns })
    })
  }

  // Bulk Import Operations
  async importCsv(
    tableName: string,
    file: File,
    options?: CsvImportOptions
  ): Promise<ImportJobResponse> {
    const params = new URLSearchParams()
    if (options?.delimiter) params.append('delimiter', options.delimiter)
    if (options?.hasHeader !== undefined) params.append('hasHeader', String(options.hasHeader))
    if (options?.dateFormat) params.append('dateFormat', options.dateFormat)
    if (options?.trimWhitespace !== undefined) params.append('trimWhitespace', String(options.trimWhitespace))
    if (options?.nullHandling) params.append('nullHandling', options.nullHandling)
    if (options?.duplicateHandling) params.append('duplicateHandling', options.duplicateHandling)
    if (options?.keyColumns) params.append('keyColumns', options.keyColumns.join(','))

    const query = params.toString() ? `?${params}` : ''
    const url = `${this.baseUrl}/api/bulk/${tableName}/import/csv${query}`

    const response = await fetch(url, {
      method: 'POST',
      headers: {
        'Content-Type': 'text/csv',
        'X-API-Key': this.apiKey,
        ...(this.tenantId && { 'X-Tenant-Id': this.tenantId })
      },
      body: file
    })

    if (!response.ok) {
      const error = await response.json().catch(() => ({ message: `Import failed: ${response.status}` }))
      throw new Error(error.message)
    }

    return response.json()
  }

  async importJson(
    tableName: string,
    file: File,
    options?: JsonImportOptions
  ): Promise<ImportJobResponse> {
    const params = new URLSearchParams()
    if (options?.dateFormat) params.append('dateFormat', options.dateFormat)
    if (options?.duplicateHandling) params.append('duplicateHandling', options.duplicateHandling)
    if (options?.keyColumns) params.append('keyColumns', options.keyColumns.join(','))

    const query = params.toString() ? `?${params}` : ''
    const url = `${this.baseUrl}/api/bulk/${tableName}/import/json${query}`

    const response = await fetch(url, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'X-API-Key': this.apiKey,
        ...(this.tenantId && { 'X-Tenant-Id': this.tenantId })
      },
      body: file
    })

    if (!response.ok) {
      const error = await response.json().catch(() => ({ message: `Import failed: ${response.status}` }))
      throw new Error(error.message)
    }

    return response.json()
  }

  async getImportJob(jobId: string): Promise<ImportJobResponse> {
    return this.request<ImportJobResponse>(`/api/bulk/import/${jobId}`)
  }

  async listImportJobs(limit = 50, offset = 0): Promise<ImportJobResponse[]> {
    return this.request<ImportJobResponse[]>(`/api/bulk/import?limit=${limit}&offset=${offset}`)
  }

  // Bulk Export Operations
  async exportCsv(tableName: string, options?: CsvExportOptions): Promise<ExportJobResponse> {
    return this.request<ExportJobResponse>(`/api/bulk/${tableName}/export/csv`, {
      method: 'POST',
      body: JSON.stringify(options || {})
    })
  }

  async exportJson(tableName: string, options?: JsonExportOptions): Promise<ExportJobResponse> {
    return this.request<ExportJobResponse>(`/api/bulk/${tableName}/export/json`, {
      method: 'POST',
      body: JSON.stringify(options || {})
    })
  }

  async exportXlsx(tableName: string, options?: XlsxExportOptions): Promise<ExportJobResponse> {
    return this.request<ExportJobResponse>(`/api/bulk/${tableName}/export/xlsx`, {
      method: 'POST',
      body: JSON.stringify(options || {})
    })
  }

  async getExportJob(jobId: string): Promise<ExportJobResponse> {
    return this.request<ExportJobResponse>(`/api/bulk/export/${jobId}`)
  }

  async listExportJobs(limit = 50, offset = 0): Promise<ExportJobResponse[]> {
    return this.request<ExportJobResponse[]>(`/api/bulk/export?limit=${limit}&offset=${offset}`)
  }

  async downloadExport(jobId: string): Promise<Blob> {
    const url = `${this.baseUrl}/api/bulk/export/${jobId}/download`
    const response = await fetch(url, {
      headers: {
        'X-API-Key': this.apiKey,
        ...(this.tenantId && { 'X-Tenant-Id': this.tenantId })
      }
    })

    if (!response.ok) {
      throw new Error(`Download failed: ${response.status}`)
    }

    return response.blob()
  }

  async getJobProgress(jobId: string): Promise<JobProgressResponse> {
    return this.request<JobProgressResponse>(`/api/bulk/jobs/${jobId}/progress`)
  }

  async cancelJob(jobId: string): Promise<void> {
    await this.request<void>(`/api/bulk/jobs/${jobId}/cancel`, { method: 'POST' })
  }

  // Health check
  async healthCheck(): Promise<{ status: string }> {
    return this.request<{ status: string }>('/health')
  }

  // Views
  async listViews(): Promise<ViewApiResponse[]> {
    return this.request<ViewApiResponse[]>('/api/views')
  }

  async getView(name: string): Promise<ViewApiResponse> {
    return this.request<ViewApiResponse>(`/api/views/${name}`)
  }

  async createView(data: CreateViewApiRequest): Promise<ViewApiResponse> {
    return this.request<ViewApiResponse>('/api/views', {
      method: 'POST',
      body: JSON.stringify(data)
    })
  }

  async updateView(name: string, data: UpdateViewApiRequest): Promise<ViewApiResponse> {
    return this.request<ViewApiResponse>(`/api/views/${name}`, {
      method: 'PATCH',
      body: JSON.stringify(data)
    })
  }

  async deleteView(name: string): Promise<void> {
    await this.request<void>(`/api/views/${name}`, {
      method: 'DELETE'
    })
  }

  async refreshMaterializedView(name: string, concurrent = false): Promise<void> {
    const params = concurrent ? '?concurrent=true' : ''
    await this.request<void>(`/api/views/${name}/refresh${params}`, {
      method: 'POST'
    })
  }

  async checkViewStale(name: string): Promise<ViewStaleResponse> {
    return this.request<ViewStaleResponse>(`/api/views/${name}/stale`)
  }

  async queryViewData(
    name: string,
    options?: {
      select?: string
      filter?: string
      orderBy?: string
      skip?: number
      take?: number
    }
  ): Promise<ViewQueryApiResponse> {
    const params = new URLSearchParams()
    if (options?.select) params.append('select', options.select)
    if (options?.filter) params.append('filter', options.filter)
    if (options?.orderBy) params.append('orderBy', options.orderBy)
    if (options?.skip) params.append('skip', options.skip.toString())
    if (options?.take) params.append('take', options.take.toString())

    const query = params.toString() ? `?${params.toString()}` : ''
    return this.request<ViewQueryApiResponse>(`/api/views/${name}/data${query}`)
  }

  // Webhooks
  async listWebhooks(): Promise<WebhookApiResponse[]> {
    return this.request<WebhookApiResponse[]>('/api/webhooks')
  }

  async getWebhook(id: string): Promise<WebhookApiResponse> {
    return this.request<WebhookApiResponse>(`/api/webhooks/${id}`)
  }

  async createWebhook(data: CreateWebhookApiRequest): Promise<WebhookApiResponse> {
    return this.request<WebhookApiResponse>('/api/webhooks', {
      method: 'POST',
      body: JSON.stringify(data)
    })
  }

  async updateWebhook(id: string, data: UpdateWebhookApiRequest): Promise<WebhookApiResponse> {
    return this.request<WebhookApiResponse>(`/api/webhooks/${id}`, {
      method: 'PATCH',
      body: JSON.stringify(data)
    })
  }

  async deleteWebhook(id: string): Promise<void> {
    await this.request<void>(`/api/webhooks/${id}`, {
      method: 'DELETE'
    })
  }

  async listWebhookDeliveries(
    webhookId: string,
    options?: { limit?: number; offset?: number }
  ): Promise<WebhookDeliveryApiResponse[]> {
    const params = new URLSearchParams()
    if (options?.limit) params.append('limit', options.limit.toString())
    if (options?.offset) params.append('offset', options.offset.toString())

    const query = params.toString() ? `?${params.toString()}` : ''
    return this.request<WebhookDeliveryApiResponse[]>(`/api/webhooks/${webhookId}/deliveries${query}`)
  }

  // Dead Letter Queue
  async listDlqMessages(
    options?: { webhookId?: string; status?: string; limit?: number; offset?: number }
  ): Promise<DlqMessageApiResponse[]> {
    const params = new URLSearchParams()
    if (options?.webhookId) params.append('webhookId', options.webhookId)
    if (options?.status) params.append('status', options.status)
    if (options?.limit) params.append('limit', options.limit.toString())
    if (options?.offset) params.append('offset', options.offset.toString())

    const query = params.toString() ? `?${params.toString()}` : ''
    return this.request<DlqMessageApiResponse[]>(`/api/webhooks/dlq${query}`)
  }

  async getDlqStatistics(): Promise<DlqStatisticsApiResponse> {
    return this.request<DlqStatisticsApiResponse>('/api/webhooks/dlq/statistics')
  }

  async resolveDlqMessage(dlqId: string, data: ResolveDlqApiRequest): Promise<DlqMessageApiResponse> {
    return this.request<DlqMessageApiResponse>(`/api/webhooks/dlq/${dlqId}/resolve`, {
      method: 'POST',
      body: JSON.stringify(data)
    })
  }

  async replayDlqMessage(dlqId: string): Promise<void> {
    await this.request<void>(`/api/webhooks/dlq/${dlqId}/replay`, {
      method: 'POST'
    })
  }

  async archiveDlqMessages(
    options?: { webhookId?: string; olderThanDays?: number }
  ): Promise<ArchiveDlqApiResponse> {
    const params = new URLSearchParams()
    if (options?.webhookId) params.append('webhookId', options.webhookId)
    if (options?.olderThanDays) params.append('olderThanDays', options.olderThanDays.toString())

    const query = params.toString() ? `?${params.toString()}` : ''
    return this.request<ArchiveDlqApiResponse>(`/api/webhooks/dlq/archive${query}`, {
      method: 'POST'
    })
  }

  // Audit API
  async queryAuditLogs(
    projectId: string,
    params?: AuditLogQueryParams
  ): Promise<AuditLogPageApiResponse> {
    const searchParams = new URLSearchParams()
    if (params) {
      if (params.category !== undefined) searchParams.set('category', params.category.toString())
      if (params.minSeverity !== undefined)
        searchParams.set('minSeverity', params.minSeverity.toString())
      if (params.actorId) searchParams.set('actorId', params.actorId)
      if (params.resourceType) searchParams.set('resourceType', params.resourceType)
      if (params.resourceId) searchParams.set('resourceId', params.resourceId)
      if (params.action) searchParams.set('action', params.action)
      if (params.from) searchParams.set('from', params.from)
      if (params.to) searchParams.set('to', params.to)
      if (params.searchText) searchParams.set('searchText', params.searchText)
      if (params.page !== undefined) searchParams.set('page', params.page.toString())
      if (params.pageSize !== undefined) searchParams.set('pageSize', params.pageSize.toString())
      if (params.orderBy) searchParams.set('orderBy', params.orderBy)
      if (params.descending !== undefined)
        searchParams.set('descending', params.descending.toString())
    }
    const queryString = searchParams.toString()
    const url = `/api/projects/${projectId}/audit/logs${queryString ? `?${queryString}` : ''}`
    return this.request<AuditLogPageApiResponse>(url)
  }

  async getAuditLog(projectId: string, logId: string): Promise<AuditLogEntryApiResponse> {
    return this.request<AuditLogEntryApiResponse>(
      `/api/projects/${projectId}/audit/logs/${logId}`
    )
  }

  async getAuditStats(
    projectId: string,
    from?: string,
    to?: string
  ): Promise<AuditStatsApiResponse> {
    const searchParams = new URLSearchParams()
    if (from) searchParams.set('from', from)
    if (to) searchParams.set('to', to)
    const queryString = searchParams.toString()
    const url = `/api/projects/${projectId}/audit/stats${queryString ? `?${queryString}` : ''}`
    return this.request<AuditStatsApiResponse>(url)
  }

  // ============================================================================
  // Security API - API Keys
  // ============================================================================

  async getApiKeys(): Promise<ApiKeyApiResponse[]> {
    return this.request<ApiKeyApiResponse[]>('/api/security/keys')
  }

  async createApiKey(request: CreateApiKeyApiRequest): Promise<CreateApiKeyApiResponse> {
    return this.request<CreateApiKeyApiResponse>('/api/security/keys', {
      method: 'POST',
      body: JSON.stringify(request)
    })
  }

  async revokeApiKey(keyId: string): Promise<void> {
    await this.request<void>(`/api/security/keys/${keyId}`, {
      method: 'DELETE'
    })
  }

  async rotateApiKey(keyId: string, revokeOld: boolean = true): Promise<CreateApiKeyApiResponse> {
    return this.request<CreateApiKeyApiResponse>(
      `/api/security/keys/${keyId}/rotate?revokeOld=${revokeOld}`,
      { method: 'POST' }
    )
  }

  // ============================================================================
  // Security API - RLS Policies
  // ============================================================================

  async getSecurityPolicies(tableName: string): Promise<SecurityPolicyApiResponse[]> {
    return this.request<SecurityPolicyApiResponse[]>(`/api/security/policies/${tableName}`)
  }

  async createSecurityPolicy(
    request: CreateSecurityPolicyApiRequest
  ): Promise<SecurityPolicyApiResponse> {
    return this.request<SecurityPolicyApiResponse>('/api/security/policies', {
      method: 'POST',
      body: JSON.stringify(request)
    })
  }

  async updateSecurityPolicy(
    policyId: string,
    request: UpdateSecurityPolicyApiRequest
  ): Promise<SecurityPolicyApiResponse> {
    return this.request<SecurityPolicyApiResponse>(`/api/security/policies/${policyId}`, {
      method: 'PATCH',
      body: JSON.stringify(request)
    })
  }

  async deleteSecurityPolicy(policyId: string): Promise<void> {
    await this.request<void>(`/api/security/policies/${policyId}`, {
      method: 'DELETE'
    })
  }

  // ============================================================================
  // Security API - Encryption
  // ============================================================================

  async getEncryptionInfo(): Promise<EncryptionInfoApiResponse> {
    return this.request<EncryptionInfoApiResponse>('/api/security/encryption/info')
  }

  async rotateTableKey(tableName: string): Promise<KeyRotationResultApiResponse> {
    return this.request<KeyRotationResultApiResponse>(
      `/api/security/encryption/rotate/${tableName}`,
      { method: 'POST' }
    )
  }

  async rotateTenantKeys(): Promise<KeyRotationResultApiResponse> {
    return this.request<KeyRotationResultApiResponse>('/api/security/encryption/rotate', {
      method: 'POST'
    })
  }

  async getRotationStatus(tableName: string): Promise<KeyRotationStatusApiResponse> {
    return this.request<KeyRotationStatusApiResponse>(
      `/api/security/encryption/status/${tableName}`
    )
  }

  async validateEncryption(tableName: string): Promise<KeyValidationResultApiResponse> {
    return this.request<KeyValidationResultApiResponse>(
      `/api/security/encryption/validate/${tableName}`
    )
  }
}

// Helper to convert API response to internal types
export function mapTableResponse(response: TableApiResponse): TableInfo {
  return {
    name: response.name,
    displayName: response.displayName || response.name,
    description: response.description,
    columnCount: response.columns.length,
    createdAt: response.createdAt,
    updatedAt: response.updatedAt
  }
}

export function mapColumnResponse(response: ColumnApiResponse): ColumnInfo {
  return {
    name: response.name,
    displayName: response.displayName,
    dataType: response.dataType,
    isNullable: response.isNullable,
    isPrimaryKey: response.isPrimaryKey,
    defaultValue: response.defaultValue
  }
}
