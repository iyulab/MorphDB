import { useMemo, useRef, useState, useCallback, type ReactElement } from 'react'
import {
  useReactTable,
  getCoreRowModel,
  getSortedRowModel,
  flexRender,
  type ColumnDef,
  type SortingState,
  type CellContext,
  type Updater
} from '@tanstack/react-table'
import { useVirtualizer } from '@tanstack/react-virtual'
import { ChevronUp, ChevronDown, Loader2, Plus, Trash2 } from 'lucide-react'
import { Button } from '@/components/ui/Button'
import { cn } from '@/lib/utils'
import type { ColumnApiResponse } from '@/lib/api'

interface DataGridProps {
  columns: ColumnApiResponse[]
  data: Record<string, unknown>[]
  isLoading?: boolean
  onAddRecord?: () => void
  onUpdateRecord?: (rowIndex: number, columnId: string, value: unknown) => Promise<void>
  onDeleteRecord?: (rowIndex: number) => Promise<void>
  pageSize?: number
  totalCount?: number
  currentPage?: number
  onPageChange?: (page: number) => void
  onSortChange?: (sortBy: string | null, sortOrder: 'asc' | 'desc') => void
}

interface EditingCell {
  rowIndex: number
  columnId: string
  originalValue: unknown
}

// Cell editor component
function CellEditor({
  value,
  dataType,
  onSave,
  onCancel
}: {
  value: unknown
  dataType: string
  onSave: (newValue: unknown) => void
  onCancel: () => void
}): ReactElement {
  const [editValue, setEditValue] = useState(
    value === null || value === undefined ? '' : String(value)
  )
  const inputRef = useRef<HTMLInputElement>(null)

  const handleKeyDown = (e: React.KeyboardEvent): void => {
    if (e.key === 'Enter') {
      handleSave()
    } else if (e.key === 'Escape') {
      onCancel()
    }
  }

  const handleSave = (): void => {
    let parsedValue: unknown = editValue

    // Parse value based on data type
    const type = dataType.toLowerCase()
    if (editValue === '' || editValue === 'null') {
      parsedValue = null
    } else if (type.includes('int') || type === 'long') {
      parsedValue = parseInt(editValue, 10)
      if (isNaN(parsedValue as number)) {
        parsedValue = editValue
      }
    } else if (type.includes('decimal') || type === 'double' || type === 'float') {
      parsedValue = parseFloat(editValue)
      if (isNaN(parsedValue as number)) {
        parsedValue = editValue
      }
    } else if (type === 'bool' || type === 'boolean') {
      parsedValue = editValue.toLowerCase() === 'true'
    }

    onSave(parsedValue)
  }

  return (
    <div className="flex items-center gap-1">
      <input
        ref={inputRef}
        type="text"
        value={editValue}
        onChange={(e) => setEditValue(e.target.value)}
        onKeyDown={handleKeyDown}
        onBlur={handleSave}
        autoFocus
        className="flex-1 h-6 px-1 text-xs bg-background border border-primary rounded focus:outline-none"
      />
    </div>
  )
}

export function DataGrid({
  columns: schemaColumns,
  data,
  isLoading = false,
  onAddRecord,
  onUpdateRecord,
  onDeleteRecord,
  pageSize = 50,
  totalCount = 0,
  currentPage = 1,
  onPageChange,
  onSortChange
}: DataGridProps): ReactElement {
  const tableContainerRef = useRef<HTMLDivElement>(null)
  const [sorting, setSorting] = useState<SortingState>([])
  const [editingCell, setEditingCell] = useState<EditingCell | null>(null)
  const [isUpdating, setIsUpdating] = useState(false)

  // Handle sorting change
  const handleSortingChange = useCallback(
    (updaterOrValue: Updater<SortingState>) => {
      const newSorting = typeof updaterOrValue === 'function'
        ? updaterOrValue(sorting)
        : updaterOrValue
      setSorting(newSorting)
      if (onSortChange) {
        if (newSorting.length > 0) {
          onSortChange(newSorting[0].id, newSorting[0].desc ? 'desc' : 'asc')
        } else {
          onSortChange(null, 'asc')
        }
      }
    },
    [onSortChange, sorting]
  )

  // Create columns from schema
  const columns = useMemo<ColumnDef<Record<string, unknown>>[]>(() => {
    const cols: ColumnDef<Record<string, unknown>>[] = schemaColumns.map((col) => ({
      id: col.name,
      accessorKey: col.name,
      header: ({ column }) => (
        <button
          className="flex items-center gap-1 font-medium text-left"
          onClick={() => column.toggleSorting(column.getIsSorted() === 'asc')}
        >
          <span className="truncate">{col.displayName || col.name}</span>
          {column.getIsSorted() === 'asc' && <ChevronUp className="h-3 w-3" />}
          {column.getIsSorted() === 'desc' && <ChevronDown className="h-3 w-3" />}
        </button>
      ),
      cell: ({ row, getValue }: CellContext<Record<string, unknown>, unknown>) => {
        const value = getValue()
        const isEditing =
          editingCell?.rowIndex === row.index && editingCell?.columnId === col.name

        if (isEditing && onUpdateRecord) {
          return (
            <CellEditor
              value={value}
              dataType={col.dataType}
              onSave={async (newValue) => {
                setIsUpdating(true)
                try {
                  await onUpdateRecord(row.index, col.name, newValue)
                } finally {
                  setIsUpdating(false)
                  setEditingCell(null)
                }
              }}
              onCancel={() => setEditingCell(null)}
            />
          )
        }

        // Format display value
        let displayValue: string
        if (value === null || value === undefined) {
          displayValue = 'NULL'
        } else if (typeof value === 'boolean') {
          displayValue = value ? 'true' : 'false'
        } else if (typeof value === 'object') {
          displayValue = JSON.stringify(value)
        } else {
          displayValue = String(value)
        }

        return (
          <div
            className={cn(
              'truncate cursor-pointer px-1 py-0.5 rounded hover:bg-accent/50',
              value === null && 'text-muted-foreground italic',
              col.isPrimaryKey && 'font-medium'
            )}
            onDoubleClick={() => {
              if (onUpdateRecord && !col.isPrimaryKey) {
                setEditingCell({
                  rowIndex: row.index,
                  columnId: col.name,
                  originalValue: value
                })
              }
            }}
            title={displayValue}
          >
            {displayValue}
          </div>
        )
      },
      size: col.isPrimaryKey ? 120 : 150
    }))

    // Add actions column if delete is available
    if (onDeleteRecord) {
      cols.push({
        id: '_actions',
        header: () => null,
        cell: ({ row }) => (
          <Button
            variant="ghost"
            size="icon"
            className="h-6 w-6 text-muted-foreground hover:text-destructive"
            onClick={async () => {
              if (confirm('Delete this record?')) {
                await onDeleteRecord(row.index)
              }
            }}
          >
            <Trash2 className="h-3.5 w-3.5" />
          </Button>
        ),
        size: 40
      })
    }

    return cols
  }, [schemaColumns, editingCell, onUpdateRecord, onDeleteRecord])

  // Create table instance
  const table = useReactTable({
    data,
    columns,
    state: { sorting },
    onSortingChange: handleSortingChange,
    getCoreRowModel: getCoreRowModel(),
    getSortedRowModel: getSortedRowModel(),
    manualSorting: !!onSortChange,
    debugTable: false
  })

  const { rows } = table.getRowModel()

  // Virtualize rows
  const rowVirtualizer = useVirtualizer({
    count: rows.length,
    getScrollElement: () => tableContainerRef.current,
    estimateSize: () => 32,
    overscan: 10
  })

  const virtualRows = rowVirtualizer.getVirtualItems()
  const totalSize = rowVirtualizer.getTotalSize()

  // Pagination
  const totalPages = Math.ceil(totalCount / pageSize)

  if (isLoading) {
    return (
      <div className="flex h-full items-center justify-center">
        <Loader2 className="h-8 w-8 animate-spin text-muted-foreground" />
      </div>
    )
  }

  if (schemaColumns.length === 0) {
    return (
      <div className="flex h-full items-center justify-center text-muted-foreground">
        No columns defined
      </div>
    )
  }

  return (
    <div className="flex h-full flex-col">
      {/* Toolbar */}
      <div className="flex items-center justify-between border-b border-border px-3 py-2">
        <div className="text-xs text-muted-foreground">
          {totalCount > 0 ? (
            <>
              {(currentPage - 1) * pageSize + 1}-
              {Math.min(currentPage * pageSize, totalCount)} of {totalCount} records
            </>
          ) : (
            'No records'
          )}
        </div>
        <div className="flex items-center gap-2">
          {isUpdating && (
            <span className="flex items-center gap-1 text-xs text-muted-foreground">
              <Loader2 className="h-3 w-3 animate-spin" />
              Saving...
            </span>
          )}
          {onAddRecord && (
            <Button variant="outline" size="sm" onClick={onAddRecord}>
              <Plus className="h-3.5 w-3.5 mr-1" />
              Add Record
            </Button>
          )}
        </div>
      </div>

      {/* Table */}
      <div ref={tableContainerRef} className="flex-1 overflow-auto">
        <div style={{ height: `${totalSize}px`, position: 'relative' }}>
          {/* Header */}
          <div className="sticky top-0 z-10 flex bg-muted/50 border-b border-border">
            {table.getHeaderGroups().map((headerGroup) =>
              headerGroup.headers.map((header) => (
                <div
                  key={header.id}
                  className="flex-shrink-0 px-2 py-1.5 text-xs font-medium text-muted-foreground border-r border-border last:border-r-0"
                  style={{ width: header.getSize() }}
                >
                  {header.isPlaceholder
                    ? null
                    : flexRender(header.column.columnDef.header, header.getContext())}
                </div>
              ))
            )}
          </div>

          {/* Body */}
          {virtualRows.length === 0 ? (
            <div className="flex items-center justify-center py-8 text-sm text-muted-foreground">
              No data available
            </div>
          ) : (
            virtualRows.map((virtualRow) => {
              const row = rows[virtualRow.index]
              return (
                <div
                  key={row.id}
                  className={cn(
                    'absolute left-0 right-0 flex border-b border-border/50',
                    'hover:bg-accent/30 transition-colors',
                    virtualRow.index % 2 === 0 ? 'bg-background' : 'bg-muted/20'
                  )}
                  style={{
                    height: `${virtualRow.size}px`,
                    transform: `translateY(${virtualRow.start + 32}px)`
                  }}
                >
                  {row.getVisibleCells().map((cell) => (
                    <div
                      key={cell.id}
                      className="flex-shrink-0 flex items-center px-2 text-xs border-r border-border/30 last:border-r-0 overflow-hidden"
                      style={{ width: cell.column.getSize() }}
                    >
                      {flexRender(cell.column.columnDef.cell, cell.getContext())}
                    </div>
                  ))}
                </div>
              )
            })
          )}
        </div>
      </div>

      {/* Pagination */}
      {totalPages > 1 && onPageChange && (
        <div className="flex items-center justify-center gap-2 border-t border-border px-3 py-2">
          <Button
            variant="outline"
            size="sm"
            onClick={() => onPageChange(1)}
            disabled={currentPage === 1}
          >
            First
          </Button>
          <Button
            variant="outline"
            size="sm"
            onClick={() => onPageChange(currentPage - 1)}
            disabled={currentPage === 1}
          >
            Previous
          </Button>
          <span className="text-xs text-muted-foreground">
            Page {currentPage} of {totalPages}
          </span>
          <Button
            variant="outline"
            size="sm"
            onClick={() => onPageChange(currentPage + 1)}
            disabled={currentPage === totalPages}
          >
            Next
          </Button>
          <Button
            variant="outline"
            size="sm"
            onClick={() => onPageChange(totalPages)}
            disabled={currentPage === totalPages}
          >
            Last
          </Button>
        </div>
      )}
    </div>
  )
}
