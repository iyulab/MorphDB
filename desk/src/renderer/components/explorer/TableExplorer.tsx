import { useEffect, useState, useRef, type ReactElement } from 'react'
import {
  Table2,
  ChevronRight,
  ChevronDown,
  RefreshCw,
  Key,
  Hash,
  Type,
  MoreVertical,
  Loader2,
  AlertCircle,
  Pencil,
  Trash2,
  Plus,
  Eye,
  ListTree,
  Link2
} from 'lucide-react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { Button } from '@/components/ui/Button'
import {
  MorphDBClient,
  type TableApiResponse,
  type ColumnApiResponse,
  type IndexApiResponse,
  type RelationApiResponse
} from '@/lib/api'
import { useConnectionStore } from '@/stores/connectionStore'
import { useExplorerStore } from '@/stores/explorerStore'
import { cn } from '@/lib/utils'
import { CreateTableDialog } from '@/components/dialogs/CreateTableDialog'
import { ColumnDialog, type ColumnFormData } from '@/components/dialogs/ColumnDialog'
import { DeleteConfirmationDialog } from '@/components/dialogs/DeleteConfirmationDialog'
import { RenameDialog } from '@/components/dialogs/RenameDialog'
import { IndexDialog, type IndexFormData } from '@/components/dialogs/IndexDialog'
import { RelationDialog, type RelationFormData } from '@/components/dialogs/RelationDialog'

interface ContextMenuState {
  open: boolean
  type: 'table' | 'column' | null
  target: TableApiResponse | ColumnApiResponse | null
  x: number
  y: number
}

function getColumnIcon(dataType: string, isPrimaryKey: boolean): ReactElement {
  if (isPrimaryKey) {
    return <Key className="h-3.5 w-3.5 text-warning" />
  }

  const type = dataType.toLowerCase()
  if (type.includes('int') || type.includes('decimal') || type.includes('numeric')) {
    return <Hash className="h-3.5 w-3.5 text-info" />
  }
  return <Type className="h-3.5 w-3.5 text-muted-foreground" />
}

interface DialogState {
  createTable: boolean
  addColumn: { open: boolean; tableName: string }
  editColumn: { open: boolean; tableName: string; column: ColumnApiResponse | null }
  deleteTable: { open: boolean; tableName: string }
  deleteColumn: { open: boolean; tableName: string; columnName: string }
  renameTable: { open: boolean; tableName: string }
  createIndex: { open: boolean; table: TableApiResponse | null }
  deleteIndex: { open: boolean; indexId: string; indexName: string }
  createRelation: { open: boolean; table: TableApiResponse | null }
  deleteRelation: { open: boolean; relationId: string; relationName: string }
}

const initialDialogState: DialogState = {
  createTable: false,
  addColumn: { open: false, tableName: '' },
  editColumn: { open: false, tableName: '', column: null },
  deleteTable: { open: false, tableName: '' },
  deleteColumn: { open: false, tableName: '', columnName: '' },
  renameTable: { open: false, tableName: '' },
  createIndex: { open: false, table: null },
  deleteIndex: { open: false, indexId: '', indexName: '' },
  createRelation: { open: false, table: null },
  deleteRelation: { open: false, relationId: '', relationName: '' }
}

export function TableExplorer(): ReactElement {
  const { activeConnection, getApiKey } = useConnectionStore()
  const {
    selectedTable,
    expandedNodes,
    setSelectedTable,
    toggleNode,
    setTables
  } = useExplorerStore()

  const queryClient = useQueryClient()

  const [contextMenu, setContextMenu] = useState<ContextMenuState>({
    open: false,
    type: null,
    target: null,
    x: 0,
    y: 0
  })

  const [dialogs, setDialogs] = useState<DialogState>(initialDialogState)
  const [currentTableForColumn, setCurrentTableForColumn] = useState<string>('')

  const contextMenuRef = useRef<HTMLDivElement>(null)

  // Fetch tables when connection changes
  const {
    data: tables,
    isLoading,
    error,
    refetch
  } = useQuery({
    queryKey: ['tables', activeConnection?.id],
    queryFn: async () => {
      if (!activeConnection) return []

      const apiKey = await getApiKey(activeConnection.id)
      if (!apiKey) {
        throw new Error('No API key found for this connection')
      }

      const client = new MorphDBClient({
        url: activeConnection.url,
        apiKey,
        tenantId: activeConnection.tenantId
      })

      return client.listTables(activeConnection.tenantId)
    },
    enabled: !!activeConnection && activeConnection.status === 'connected'
  })

  // Sync tables to store
  useEffect(() => {
    if (tables) {
      setTables(tables)
    }
  }, [tables, setTables])

  // Helper to create API client
  const createClient = async (): Promise<MorphDBClient | null> => {
    if (!activeConnection) return null
    const apiKey = await getApiKey(activeConnection.id)
    if (!apiKey) return null
    return new MorphDBClient({
      url: activeConnection.url,
      apiKey,
      tenantId: activeConnection.tenantId
    })
  }

  // Create table mutation
  const createTableMutation = useMutation({
    mutationFn: async ({ name, columns }: { name: string; columns: { name: string; type: string; nullable: boolean; unique: boolean; indexed: boolean; isPrimaryKey: boolean }[] }) => {
      const client = await createClient()
      if (!client) throw new Error('No active connection')
      return client.createTable({
        name,
        columns: columns.map((col) => ({
          name: col.name,
          dataType: col.type,
          isNullable: col.nullable,
          isUnique: col.unique,
          isIndexed: col.indexed,
          isPrimaryKey: col.isPrimaryKey
        }))
      }, activeConnection?.tenantId)
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['tables', activeConnection?.id] })
    }
  })

  // Delete table mutation
  const deleteTableMutation = useMutation({
    mutationFn: async (tableName: string) => {
      const client = await createClient()
      if (!client) throw new Error('No active connection')
      return client.deleteTable(tableName, activeConnection?.tenantId)
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['tables', activeConnection?.id] })
    }
  })

  // Rename table mutation
  const renameTableMutation = useMutation({
    mutationFn: async ({ oldName, newName }: { oldName: string; newName: string }) => {
      const client = await createClient()
      if (!client) throw new Error('No active connection')
      return client.renameTable(oldName, newName, activeConnection?.tenantId)
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['tables', activeConnection?.id] })
    }
  })

  // Add column mutation
  const addColumnMutation = useMutation({
    mutationFn: async ({ tableName, data }: { tableName: string; data: ColumnFormData }) => {
      const client = await createClient()
      if (!client) throw new Error('No active connection')
      return client.addColumn(tableName, {
        name: data.name,
        dataType: data.type,
        isNullable: data.nullable,
        isUnique: data.unique,
        isIndexed: data.indexed,
        defaultValue: data.defaultValue || undefined
      }, activeConnection?.tenantId)
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['tables', activeConnection?.id] })
    }
  })

  // Update column mutation
  const updateColumnMutation = useMutation({
    mutationFn: async ({ tableName, columnName, data }: { tableName: string; columnName: string; data: ColumnFormData }) => {
      const client = await createClient()
      if (!client) throw new Error('No active connection')
      return client.updateColumn(tableName, columnName, {
        isNullable: data.nullable,
        isUnique: data.unique,
        isIndexed: data.indexed,
        defaultValue: data.defaultValue || undefined
      }, activeConnection?.tenantId)
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['tables', activeConnection?.id] })
    }
  })

  // Delete column mutation
  const deleteColumnMutation = useMutation({
    mutationFn: async ({ tableName, columnName }: { tableName: string; columnName: string }) => {
      const client = await createClient()
      if (!client) throw new Error('No active connection')
      return client.deleteColumn(tableName, columnName, activeConnection?.tenantId)
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['tables', activeConnection?.id] })
    }
  })

  // Create index mutation
  const createIndexMutation = useMutation({
    mutationFn: async ({ tableName, data }: { tableName: string; data: IndexFormData }) => {
      const client = await createClient()
      if (!client) throw new Error('No active connection')
      return client.createIndex(tableName, {
        name: data.name,
        columns: data.columns,
        type: data.type,
        unique: data.unique
      }, activeConnection?.tenantId)
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['tables', activeConnection?.id] })
    }
  })

  // Delete index mutation
  const deleteIndexMutation = useMutation({
    mutationFn: async (indexId: string) => {
      const client = await createClient()
      if (!client) throw new Error('No active connection')
      return client.deleteIndex(indexId, activeConnection?.tenantId)
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['tables', activeConnection?.id] })
    }
  })

  // Create relation mutation
  const createRelationMutation = useMutation({
    mutationFn: async ({ sourceTable, data }: { sourceTable: string; data: RelationFormData }) => {
      const client = await createClient()
      if (!client) throw new Error('No active connection')
      return client.createRelation({
        name: data.name,
        sourceTable,
        sourceColumn: data.sourceColumn,
        targetTable: data.targetTable,
        targetColumn: data.targetColumn,
        type: data.type,
        onDelete: data.onDelete
      }, activeConnection?.tenantId)
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['tables', activeConnection?.id] })
    }
  })

  // Delete relation mutation
  const deleteRelationMutation = useMutation({
    mutationFn: async (relationId: string) => {
      const client = await createClient()
      if (!client) throw new Error('No active connection')
      return client.deleteRelation(relationId, activeConnection?.tenantId)
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['tables', activeConnection?.id] })
    }
  })

  // Close context menu on outside click
  useEffect(() => {
    const handleClickOutside = (e: MouseEvent): void => {
      if (contextMenuRef.current && !contextMenuRef.current.contains(e.target as Node)) {
        setContextMenu({ open: false, type: null, target: null, x: 0, y: 0 })
      }
    }

    document.addEventListener('mousedown', handleClickOutside)
    return () => document.removeEventListener('mousedown', handleClickOutside)
  }, [])

  const handleTableContextMenu = (e: React.MouseEvent, table: TableApiResponse): void => {
    e.preventDefault()
    e.stopPropagation()
    setContextMenu({
      open: true,
      type: 'table',
      target: table,
      x: e.clientX,
      y: e.clientY
    })
  }

  const handleColumnContextMenu = (e: React.MouseEvent, column: ColumnApiResponse): void => {
    e.preventDefault()
    e.stopPropagation()
    setContextMenu({
      open: true,
      type: 'column',
      target: column,
      x: e.clientX,
      y: e.clientY
    })
  }

  const closeContextMenu = (): void => {
    setContextMenu({ open: false, type: null, target: null, x: 0, y: 0 })
  }

  // Context menu actions
  const handleViewData = (): void => {
    if (contextMenu.type === 'table' && contextMenu.target) {
      setSelectedTable((contextMenu.target as TableApiResponse).name)
    }
    closeContextMenu()
  }

  const handleRefresh = (): void => {
    refetch()
    closeContextMenu()
  }

  const handleAddColumn = (): void => {
    if (contextMenu.type === 'table' && contextMenu.target) {
      const tableName = (contextMenu.target as TableApiResponse).name
      setCurrentTableForColumn(tableName)
      setDialogs((prev) => ({ ...prev, addColumn: { open: true, tableName } }))
    }
    closeContextMenu()
  }

  const handleRenameTable = (): void => {
    if (contextMenu.type === 'table' && contextMenu.target) {
      const tableName = (contextMenu.target as TableApiResponse).name
      setDialogs((prev) => ({ ...prev, renameTable: { open: true, tableName } }))
    }
    closeContextMenu()
  }

  const handleDeleteTable = (): void => {
    if (contextMenu.type === 'table' && contextMenu.target) {
      const tableName = (contextMenu.target as TableApiResponse).name
      setDialogs((prev) => ({ ...prev, deleteTable: { open: true, tableName } }))
    }
    closeContextMenu()
  }

  const handleCreateIndex = (): void => {
    if (contextMenu.type === 'table' && contextMenu.target) {
      const table = contextMenu.target as TableApiResponse
      setDialogs((prev) => ({ ...prev, createIndex: { open: true, table } }))
    }
    closeContextMenu()
  }

  const handleCreateRelation = (): void => {
    if (contextMenu.type === 'table' && contextMenu.target) {
      const table = contextMenu.target as TableApiResponse
      setDialogs((prev) => ({ ...prev, createRelation: { open: true, table } }))
    }
    closeContextMenu()
  }

  const handleEditColumn = (): void => {
    if (contextMenu.type === 'column' && contextMenu.target) {
      const column = contextMenu.target as ColumnApiResponse
      // Find the parent table for this column
      const parentTable = tables?.find((t) => t.columns.some((c) => c.id === column.id))
      if (parentTable) {
        setCurrentTableForColumn(parentTable.name)
        setDialogs((prev) => ({
          ...prev,
          editColumn: { open: true, tableName: parentTable.name, column }
        }))
      }
    }
    closeContextMenu()
  }

  const handleDeleteColumn = (): void => {
    if (contextMenu.type === 'column' && contextMenu.target) {
      const column = contextMenu.target as ColumnApiResponse
      const parentTable = tables?.find((t) => t.columns.some((c) => c.id === column.id))
      if (parentTable) {
        setDialogs((prev) => ({
          ...prev,
          deleteColumn: { open: true, tableName: parentTable.name, columnName: column.name }
        }))
      }
    }
    closeContextMenu()
  }

  // Dialog handlers
  const handleCreateTableSubmit = async (tableName: string, columns: { id: string; name: string; type: string; nullable: boolean; unique: boolean; indexed: boolean; isPrimaryKey: boolean }[]): Promise<void> => {
    await createTableMutation.mutateAsync({ name: tableName, columns })
  }

  const handleColumnSubmit = async (data: ColumnFormData): Promise<void> => {
    if (dialogs.editColumn.open && dialogs.editColumn.column) {
      await updateColumnMutation.mutateAsync({
        tableName: dialogs.editColumn.tableName,
        columnName: dialogs.editColumn.column.name,
        data
      })
    } else if (dialogs.addColumn.open) {
      await addColumnMutation.mutateAsync({
        tableName: dialogs.addColumn.tableName,
        data
      })
    }
  }

  const handleDeleteTableConfirm = async (): Promise<void> => {
    await deleteTableMutation.mutateAsync(dialogs.deleteTable.tableName)
  }

  const handleDeleteColumnConfirm = async (): Promise<void> => {
    await deleteColumnMutation.mutateAsync({
      tableName: dialogs.deleteColumn.tableName,
      columnName: dialogs.deleteColumn.columnName
    })
  }

  const handleRenameTableSubmit = async (newName: string): Promise<void> => {
    await renameTableMutation.mutateAsync({
      oldName: dialogs.renameTable.tableName,
      newName
    })
  }

  const handleCreateIndexSubmit = async (data: IndexFormData): Promise<void> => {
    if (!dialogs.createIndex.table) return
    await createIndexMutation.mutateAsync({
      tableName: dialogs.createIndex.table.name,
      data
    })
  }

  const handleDeleteIndexConfirm = async (): Promise<void> => {
    await deleteIndexMutation.mutateAsync(dialogs.deleteIndex.indexId)
  }

  const handleCreateRelationSubmit = async (data: RelationFormData): Promise<void> => {
    if (!dialogs.createRelation.table) return
    await createRelationMutation.mutateAsync({
      sourceTable: dialogs.createRelation.table.name,
      data
    })
  }

  const handleDeleteRelationConfirm = async (): Promise<void> => {
    await deleteRelationMutation.mutateAsync(dialogs.deleteRelation.relationId)
  }

  if (!activeConnection) {
    return (
      <div className="flex h-full items-center justify-center p-4 text-center text-muted-foreground">
        <p className="text-sm">Select a connection to browse tables</p>
      </div>
    )
  }

  if (activeConnection.status !== 'connected') {
    return (
      <div className="flex h-full flex-col items-center justify-center p-4 text-center">
        <AlertCircle className="h-8 w-8 text-muted-foreground mb-2" />
        <p className="text-sm text-muted-foreground">
          Connection not active.
          <br />
          Connect to load tables.
        </p>
      </div>
    )
  }

  return (
    <div className="flex h-full flex-col">
      {/* Header */}
      <div className="flex items-center justify-between border-b border-border px-3 py-2">
        <h3 className="text-xs font-medium uppercase tracking-wider text-muted-foreground">
          Tables
        </h3>
        <div className="flex items-center gap-1">
          <Button
            variant="ghost"
            size="icon"
            className="h-6 w-6"
            onClick={() => setDialogs((prev) => ({ ...prev, createTable: true }))}
            title="Create Table"
          >
            <Plus className="h-3.5 w-3.5" />
          </Button>
          <Button
            variant="ghost"
            size="icon"
            className="h-6 w-6"
            onClick={() => refetch()}
            disabled={isLoading}
            title="Refresh"
          >
            <RefreshCw className={cn('h-3.5 w-3.5', isLoading && 'animate-spin')} />
          </Button>
        </div>
      </div>

      {/* Tree View */}
      <div className="flex-1 overflow-y-auto p-2">
        {isLoading ? (
          <div className="flex items-center justify-center py-8">
            <Loader2 className="h-5 w-5 animate-spin text-muted-foreground" />
          </div>
        ) : error ? (
          <div className="flex flex-col items-center justify-center py-8 text-center">
            <AlertCircle className="h-5 w-5 text-destructive mb-2" />
            <p className="text-xs text-destructive">{(error as Error).message}</p>
            <Button variant="ghost" size="sm" className="mt-2" onClick={() => refetch()}>
              Retry
            </Button>
          </div>
        ) : !tables || tables.length === 0 ? (
          <div className="flex flex-col items-center justify-center py-8 text-center">
            <Table2 className="h-8 w-8 text-muted-foreground/50 mb-2" />
            <p className="text-xs text-muted-foreground">No tables found</p>
          </div>
        ) : (
          <div className="space-y-0.5">
            {tables.map((table) => {
              const isExpanded = expandedNodes.has(table.id)
              const isSelected = selectedTable === table.name

              return (
                <div key={table.id}>
                  {/* Table Row */}
                  <div
                    className={cn(
                      'group flex items-center gap-1 rounded-md px-1.5 py-1 text-sm cursor-pointer',
                      'hover:bg-accent transition-colors',
                      isSelected && 'bg-accent text-accent-foreground'
                    )}
                    onClick={() => setSelectedTable(table.name)}
                    onContextMenu={(e) => handleTableContextMenu(e, table)}
                  >
                    <button
                      onClick={(e) => {
                        e.stopPropagation()
                        toggleNode(table.id)
                      }}
                      className="flex-shrink-0 p-0.5 hover:bg-background/50 rounded"
                    >
                      {isExpanded ? (
                        <ChevronDown className="h-3.5 w-3.5" />
                      ) : (
                        <ChevronRight className="h-3.5 w-3.5" />
                      )}
                    </button>
                    <Table2 className="h-4 w-4 flex-shrink-0 text-primary" />
                    <span className="truncate flex-1">{table.displayName || table.name}</span>
                    <span className="text-xs text-muted-foreground opacity-0 group-hover:opacity-100">
                      {table.columns?.length || 0}
                    </span>
                    <Button
                      variant="ghost"
                      size="icon"
                      className="h-5 w-5 opacity-0 group-hover:opacity-100"
                      onClick={(e) => {
                        e.stopPropagation()
                        handleTableContextMenu(e, table)
                      }}
                    >
                      <MoreVertical className="h-3 w-3" />
                    </Button>
                  </div>

                  {/* Expanded Content */}
                  {isExpanded && (
                    <div className="ml-5 border-l border-border pl-2 mt-0.5">
                      {/* Columns Section */}
                      {table.columns && table.columns.length > 0 && (
                        <div className="mb-1">
                          <div className="text-[10px] uppercase text-muted-foreground/70 px-1.5 py-0.5 font-medium">
                            Columns
                          </div>
                          {table.columns.map((column) => (
                            <div
                              key={column.id}
                              className={cn(
                                'group flex items-center gap-2 rounded-md px-1.5 py-0.5 text-xs cursor-pointer',
                                'hover:bg-accent/50 transition-colors'
                              )}
                              onContextMenu={(e) => handleColumnContextMenu(e, column)}
                            >
                              {getColumnIcon(column.dataType, column.isPrimaryKey)}
                              <span className="truncate flex-1">
                                {column.displayName || column.name}
                              </span>
                              <span className="text-muted-foreground/70 text-[10px]">
                                {column.dataType}
                              </span>
                              {column.isNullable && (
                                <span className="text-muted-foreground/50 text-[10px]">?</span>
                              )}
                            </div>
                          ))}
                        </div>
                      )}

                      {/* Indexes Section */}
                      {table.indexes && table.indexes.length > 0 && (
                        <div className="mb-1">
                          <div className="text-[10px] uppercase text-muted-foreground/70 px-1.5 py-0.5 font-medium">
                            Indexes
                          </div>
                          {table.indexes.map((index) => (
                            <div
                              key={index.id}
                              className={cn(
                                'group flex items-center gap-2 rounded-md px-1.5 py-0.5 text-xs cursor-pointer',
                                'hover:bg-accent/50 transition-colors'
                              )}
                            >
                              <ListTree className="h-3.5 w-3.5 text-orange-500" />
                              <span className="truncate flex-1">{index.name}</span>
                              <span className="text-muted-foreground/70 text-[10px]">
                                {index.type}
                              </span>
                              {index.unique && (
                                <span className="text-yellow-500 text-[10px]">U</span>
                              )}
                              <Button
                                variant="ghost"
                                size="icon"
                                className="h-4 w-4 opacity-0 group-hover:opacity-100"
                                onClick={(e) => {
                                  e.stopPropagation()
                                  setDialogs((prev) => ({
                                    ...prev,
                                    deleteIndex: { open: true, indexId: index.id, indexName: index.name }
                                  }))
                                }}
                              >
                                <Trash2 className="h-3 w-3 text-destructive" />
                              </Button>
                            </div>
                          ))}
                        </div>
                      )}

                      {/* Relations Section */}
                      {table.relations && table.relations.length > 0 && (
                        <div className="mb-1">
                          <div className="text-[10px] uppercase text-muted-foreground/70 px-1.5 py-0.5 font-medium">
                            Relations
                          </div>
                          {table.relations.map((relation) => (
                            <div
                              key={relation.id}
                              className={cn(
                                'group flex items-center gap-2 rounded-md px-1.5 py-0.5 text-xs cursor-pointer',
                                'hover:bg-accent/50 transition-colors'
                              )}
                            >
                              <Link2 className="h-3.5 w-3.5 text-blue-500" />
                              <span className="truncate flex-1">{relation.name}</span>
                              <span className="text-muted-foreground/70 text-[10px]">
                                {relation.type}
                              </span>
                              <Button
                                variant="ghost"
                                size="icon"
                                className="h-4 w-4 opacity-0 group-hover:opacity-100"
                                onClick={(e) => {
                                  e.stopPropagation()
                                  setDialogs((prev) => ({
                                    ...prev,
                                    deleteRelation: { open: true, relationId: relation.id, relationName: relation.name }
                                  }))
                                }}
                              >
                                <Trash2 className="h-3 w-3 text-destructive" />
                              </Button>
                            </div>
                          ))}
                        </div>
                      )}
                    </div>
                  )}
                </div>
              )
            })}
          </div>
        )}
      </div>

      {/* Context Menu */}
      {contextMenu.open && (
        <div
          ref={contextMenuRef}
          className="fixed z-50 min-w-[140px] rounded-md border bg-popover p-1 shadow-md"
          style={{
            left: Math.min(contextMenu.x, window.innerWidth - 160),
            top: Math.min(contextMenu.y, window.innerHeight - 180)
          }}
        >
          {contextMenu.type === 'table' && (
            <>
              <button
                onClick={handleViewData}
                className="flex w-full items-center gap-2 rounded-sm px-2 py-1.5 text-sm hover:bg-accent"
              >
                <Eye className="h-4 w-4" />
                View Data
              </button>
              <button
                onClick={handleRefresh}
                className="flex w-full items-center gap-2 rounded-sm px-2 py-1.5 text-sm hover:bg-accent"
              >
                <RefreshCw className="h-4 w-4" />
                Refresh
              </button>
              <div className="my-1 h-px bg-border" />
              <button
                onClick={handleAddColumn}
                className="flex w-full items-center gap-2 rounded-sm px-2 py-1.5 text-sm hover:bg-accent"
              >
                <Plus className="h-4 w-4" />
                Add Column
              </button>
              <button
                onClick={handleCreateIndex}
                className="flex w-full items-center gap-2 rounded-sm px-2 py-1.5 text-sm hover:bg-accent"
              >
                <ListTree className="h-4 w-4" />
                Add Index
              </button>
              <button
                onClick={handleCreateRelation}
                className="flex w-full items-center gap-2 rounded-sm px-2 py-1.5 text-sm hover:bg-accent"
              >
                <Link2 className="h-4 w-4" />
                Add Relation
              </button>
              <div className="my-1 h-px bg-border" />
              <button
                onClick={handleRenameTable}
                className="flex w-full items-center gap-2 rounded-sm px-2 py-1.5 text-sm hover:bg-accent"
              >
                <Pencil className="h-4 w-4" />
                Rename
              </button>
              <button
                onClick={handleDeleteTable}
                className="flex w-full items-center gap-2 rounded-sm px-2 py-1.5 text-sm hover:bg-accent text-destructive"
              >
                <Trash2 className="h-4 w-4" />
                Delete
              </button>
            </>
          )}
          {contextMenu.type === 'column' && (
            <>
              <button
                onClick={handleEditColumn}
                className="flex w-full items-center gap-2 rounded-sm px-2 py-1.5 text-sm hover:bg-accent"
              >
                <Pencil className="h-4 w-4" />
                Edit Column
              </button>
              <button
                onClick={handleDeleteColumn}
                className="flex w-full items-center gap-2 rounded-sm px-2 py-1.5 text-sm hover:bg-accent text-destructive"
              >
                <Trash2 className="h-4 w-4" />
                Delete Column
              </button>
            </>
          )}
        </div>
      )}

      {/* Dialogs */}
      <CreateTableDialog
        open={dialogs.createTable}
        onOpenChange={(open) => setDialogs((prev) => ({ ...prev, createTable: open }))}
        onSubmit={handleCreateTableSubmit}
      />

      <ColumnDialog
        open={dialogs.addColumn.open}
        onOpenChange={(open) =>
          setDialogs((prev) => ({ ...prev, addColumn: { ...prev.addColumn, open } }))
        }
        tableName={dialogs.addColumn.tableName}
        column={null}
        onSubmit={handleColumnSubmit}
      />

      <ColumnDialog
        open={dialogs.editColumn.open}
        onOpenChange={(open) =>
          setDialogs((prev) => ({ ...prev, editColumn: { ...prev.editColumn, open } }))
        }
        tableName={dialogs.editColumn.tableName}
        column={dialogs.editColumn.column}
        onSubmit={handleColumnSubmit}
      />

      <DeleteConfirmationDialog
        open={dialogs.deleteTable.open}
        onOpenChange={(open) =>
          setDialogs((prev) => ({ ...prev, deleteTable: { ...prev.deleteTable, open } }))
        }
        title="Delete Table"
        description="This will permanently delete the table and all its data. This action cannot be undone."
        itemName={dialogs.deleteTable.tableName}
        requireTypedConfirmation={true}
        onConfirm={handleDeleteTableConfirm}
      />

      <DeleteConfirmationDialog
        open={dialogs.deleteColumn.open}
        onOpenChange={(open) =>
          setDialogs((prev) => ({ ...prev, deleteColumn: { ...prev.deleteColumn, open } }))
        }
        title="Delete Column"
        description={`This will permanently delete the column "${dialogs.deleteColumn.columnName}" and all its data.`}
        itemName={dialogs.deleteColumn.columnName}
        requireTypedConfirmation={false}
        onConfirm={handleDeleteColumnConfirm}
      />

      <RenameDialog
        open={dialogs.renameTable.open}
        onOpenChange={(open) =>
          setDialogs((prev) => ({ ...prev, renameTable: { ...prev.renameTable, open } }))
        }
        title="Rename Table"
        currentName={dialogs.renameTable.tableName}
        onSubmit={handleRenameTableSubmit}
      />

      <IndexDialog
        open={dialogs.createIndex.open}
        onOpenChange={(open) =>
          setDialogs((prev) => ({ ...prev, createIndex: { ...prev.createIndex, open } }))
        }
        tableName={dialogs.createIndex.table?.name || ''}
        columns={dialogs.createIndex.table?.columns || []}
        onSubmit={handleCreateIndexSubmit}
      />

      <DeleteConfirmationDialog
        open={dialogs.deleteIndex.open}
        onOpenChange={(open) =>
          setDialogs((prev) => ({ ...prev, deleteIndex: { ...prev.deleteIndex, open } }))
        }
        title="Delete Index"
        description={`This will permanently delete the index "${dialogs.deleteIndex.indexName}".`}
        itemName={dialogs.deleteIndex.indexName}
        requireTypedConfirmation={false}
        onConfirm={handleDeleteIndexConfirm}
      />

      <RelationDialog
        open={dialogs.createRelation.open}
        onOpenChange={(open) =>
          setDialogs((prev) => ({ ...prev, createRelation: { ...prev.createRelation, open } }))
        }
        sourceTable={dialogs.createRelation.table || { id: '', name: '', version: 0, createdAt: '', updatedAt: '', columns: [], indexes: [], relations: [] }}
        tables={tables || []}
        onSubmit={handleCreateRelationSubmit}
      />

      <DeleteConfirmationDialog
        open={dialogs.deleteRelation.open}
        onOpenChange={(open) =>
          setDialogs((prev) => ({ ...prev, deleteRelation: { ...prev.deleteRelation, open } }))
        }
        title="Delete Relation"
        description={`This will permanently delete the relation "${dialogs.deleteRelation.relationName}".`}
        itemName={dialogs.deleteRelation.relationName}
        requireTypedConfirmation={false}
        onConfirm={handleDeleteRelationConfirm}
      />
    </div>
  )
}
