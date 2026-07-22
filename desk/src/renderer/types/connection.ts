export type ConnectionStatus = 'disconnected' | 'connecting' | 'connected' | 'error'

export interface Connection {
  id: string
  name: string
  url: string
  projectId?: string
  createdAt: string
  lastUsedAt?: string
  status: ConnectionStatus
  errorMessage?: string
}

export interface ConnectionFormData {
  name: string
  url: string
}

export interface Project {
  projectId: string
  name: string
  slug: string
  description?: string
  environment: string
  status: string
  createdAt: string
}

export interface TableInfo {
  name: string
  displayName: string
  description?: string
  columnCount: number
  createdAt: string
  updatedAt: string
}

export interface ColumnInfo {
  name: string
  displayName: string
  dataType: string
  isNullable: boolean
  isPrimaryKey: boolean
  defaultValue?: string
  description?: string
}
