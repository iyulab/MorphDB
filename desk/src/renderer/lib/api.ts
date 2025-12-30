import type { Project, TableInfo, ColumnInfo } from '@/types/connection'

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
  async listProjects(): Promise<Project[]> {
    const response = await this.request<PagedResponse<Project>>('/api/projects')
    return response.data
  }

  async getProject(id: string): Promise<Project> {
    return this.request<Project>(`/api/projects/${id}`)
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
