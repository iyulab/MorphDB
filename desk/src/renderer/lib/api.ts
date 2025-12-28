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
  displayName: string
  description?: string
  columnCount: number
  schemaVersion: number
  createdAt: string
  updatedAt: string
  columns: ColumnApiResponse[]
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

  // Data
  async queryData(
    tableName: string,
    options?: {
      $top?: number
      $skip?: number
      $orderby?: string
      $filter?: string
      $select?: string
    }
  ): Promise<{ value: Record<string, unknown>[] }> {
    const params = new URLSearchParams()
    if (options?.$top) params.append('$top', options.$top.toString())
    if (options?.$skip) params.append('$skip', options.$skip.toString())
    if (options?.$orderby) params.append('$orderby', options.$orderby)
    if (options?.$filter) params.append('$filter', options.$filter)
    if (options?.$select) params.append('$select', options.$select)

    const query = params.toString() ? `?${params.toString()}` : ''
    return this.request<{ value: Record<string, unknown>[] }>(
      `/odata/${tableName}${query}`
    )
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
    columnCount: response.columnCount,
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
