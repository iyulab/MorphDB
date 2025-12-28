import { useEffect, useState, useRef, type ReactElement } from 'react'
import {
  Table2,
  ChevronRight,
  ChevronDown,
  RefreshCw,
  Columns3,
  Key,
  Hash,
  Type,
  MoreVertical,
  Loader2,
  AlertCircle,
  Pencil,
  Trash2,
  Plus,
  Eye
} from 'lucide-react'
import { useQuery } from '@tanstack/react-query'
import { Button } from '@/components/ui/Button'
import { MorphDBClient, type TableApiResponse, type ColumnApiResponse } from '@/lib/api'
import { useConnectionStore } from '@/stores/connectionStore'
import { useExplorerStore } from '@/stores/explorerStore'
import { cn } from '@/lib/utils'

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

export function TableExplorer(): ReactElement {
  const { activeConnection, getApiKey } = useConnectionStore()
  const {
    selectedTable,
    expandedNodes,
    setSelectedTable,
    toggleNode,
    setTables
  } = useExplorerStore()

  const [contextMenu, setContextMenu] = useState<ContextMenuState>({
    open: false,
    type: null,
    target: null,
    x: 0,
    y: 0
  })

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

  // Placeholder actions for context menu
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
                      {table.columnCount}
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

                  {/* Columns (Expanded) */}
                  {isExpanded && table.columns && (
                    <div className="ml-5 border-l border-border pl-2 mt-0.5">
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
                onClick={closeContextMenu}
                className="flex w-full items-center gap-2 rounded-sm px-2 py-1.5 text-sm hover:bg-accent"
              >
                <Plus className="h-4 w-4" />
                Add Column
              </button>
              <button
                onClick={closeContextMenu}
                className="flex w-full items-center gap-2 rounded-sm px-2 py-1.5 text-sm hover:bg-accent"
              >
                <Pencil className="h-4 w-4" />
                Rename
              </button>
              <button
                onClick={closeContextMenu}
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
                onClick={closeContextMenu}
                className="flex w-full items-center gap-2 rounded-sm px-2 py-1.5 text-sm hover:bg-accent"
              >
                <Pencil className="h-4 w-4" />
                Edit Column
              </button>
              <button
                onClick={closeContextMenu}
                className="flex w-full items-center gap-2 rounded-sm px-2 py-1.5 text-sm hover:bg-accent text-destructive"
              >
                <Trash2 className="h-4 w-4" />
                Delete Column
              </button>
            </>
          )}
        </div>
      )}
    </div>
  )
}
