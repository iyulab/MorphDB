import type { TableInfo, ColumnInfo } from '@/types/connection'

export interface ApiError {
  error: string
  message: string
  code?: string
}

export interface PagedResponse<T> {
  data: T[]
  pagination: {
    page: number
    pageSize: number
    totalCount: number
  }
}

// Project types
export type ProjectStatus = 'Active' | 'Suspended' | 'Archived'

export interface ProjectApiResponse {
  projectId: string
  organizationId: string
  name: string
  slug: string
  description?: string
  environment: string
  status: ProjectStatus
  settings?: ProjectSettings
  createdAt: string
  updatedAt?: string
}

export interface ProjectSettings {
  defaultLocale?: string
  enableAuditLog?: boolean
  retentionDays?: number
  maxTableCount?: number
  maxRowsPerTable?: number
}

export interface CreateProjectRequest {
  name: string
  slug?: string
  organizationId?: string
  settings?: ProjectSettings
}

export interface UpdateProjectRequest {
  name?: string
  settings?: ProjectSettings
}

export interface ProjectStatsResponse {
  projectId: string
  tableCount: number
  totalRows: number
  schemaSizeBytes: number
  dataSizeBytes: number
  lastActivityAt?: string
}

export interface SchemaHealthResponse {
  projectId: string
  isHealthy: boolean
  issues: SchemaHealthIssue[]
  checkedAt: string
}

export interface SchemaHealthIssue {
  severity: 'Warning' | 'Error' | 'Critical'
  category: string
  message: string
  tableName?: string
  columnName?: string
}

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

export interface BatchOperationResult {
  index: number
  success: boolean
  data?: Record<string, unknown>
  affectedRows?: number
  error?: string
}

export interface BatchResponse {
  results: BatchOperationResult[]
  successCount: number
  failureCount: number
}

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
  description?: string
}

// Request DTOs
export interface CreateTableRequest {
  name: string
  displayName?: string
  description?: string
  columns: CreateColumnRequest[]
}

export interface CreateColumnRequest {
  name: string
  displayName?: string
  dataType: string
  isNullable?: boolean
  isUnique?: boolean
  isIndexed?: boolean
  isPrimaryKey?: boolean
  defaultValue?: string
  description?: string
}

export interface UpdateColumnRequest {
  displayName?: string
  isNullable?: boolean
  isUnique?: boolean
  isIndexed?: boolean
  defaultValue?: string
  description?: string
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
  async listProjects(organizationId?: string, status?: ProjectStatus): Promise<ProjectApiResponse[]> {
    const params = new URLSearchParams()
    if (organizationId) params.append('organizationId', organizationId)
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
    return this.request<TableApiResponse[]>('/api/schema/tables', { headers })
  }

  async getTable(name: string, tenantId?: string): Promise<TableApiResponse> {
    const headers: Record<string, string> = {}
    if (tenantId) {
      headers['X-Tenant-Id'] = tenantId
    }
    return this.request<TableApiResponse>(`/api/schema/tables/${name}`, { headers })
  }

  async createTable(data: CreateTableRequest, tenantId?: string): Promise<TableApiResponse> {
    const headers: Record<string, string> = {}
    if (tenantId) {
      headers['X-Tenant-Id'] = tenantId
    }
    return this.request<TableApiResponse>('/api/schema/tables', {
      method: 'POST',
      headers,
      body: JSON.stringify(data)
    })
  }

  async renameTable(name: string, newName: string, tenantId?: string): Promise<TableApiResponse> {
    const headers: Record<string, string> = {}
    if (tenantId) {
      headers['X-Tenant-Id'] = tenantId
    }
    return this.request<TableApiResponse>(`/api/schema/tables/${name}/rename`, {
      method: 'POST',
      headers,
      body: JSON.stringify({ newName })
    })
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
    return this.request<ColumnApiResponse>(`/api/schema/tables/${tableName}/columns`, {
      method: 'POST',
      headers,
      body: JSON.stringify(data)
    })
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
    return this.request<ColumnApiResponse>(`/api/schema/tables/${tableName}/columns/${columnName}`, {
      method: 'PUT',
      headers,
      body: JSON.stringify(data)
    })
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
    displayName: response.displayName || response.name,
    dataType: response.dataType,
    isNullable: response.isNullable,
    isPrimaryKey: response.isPrimaryKey,
    defaultValue: response.defaultValue,
    description: response.description
  }
}
