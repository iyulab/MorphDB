import { useState, type ReactElement } from 'react'
import { X, Plus, Trash2, Loader2, Key, ChevronDown } from 'lucide-react'
import { Button } from '@/components/ui/Button'
import { Input } from '@/components/ui/Input'
import { Label } from '@/components/ui/Label'
import { cn } from '@/lib/utils'

interface ColumnDefinition {
  id: string
  name: string
  type: string
  nullable: boolean
  unique: boolean
  indexed: boolean
  isPrimaryKey: boolean
  defaultValue?: string
}

interface CreateTableDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  onSubmit: (tableName: string, columns: ColumnDefinition[]) => Promise<void>
}

const DATA_TYPES = [
  { value: 'string', label: 'String (Text)' },
  { value: 'int', label: 'Integer' },
  { value: 'long', label: 'Long' },
  { value: 'decimal', label: 'Decimal' },
  { value: 'double', label: 'Double' },
  { value: 'bool', label: 'Boolean' },
  { value: 'datetime', label: 'DateTime' },
  { value: 'guid', label: 'GUID' },
  { value: 'json', label: 'JSON' }
]

const createEmptyColumn = (): ColumnDefinition => ({
  id: crypto.randomUUID(),
  name: '',
  type: 'string',
  nullable: true,
  unique: false,
  indexed: false,
  isPrimaryKey: false
})

export function CreateTableDialog({
  open,
  onOpenChange,
  onSubmit
}: CreateTableDialogProps): ReactElement | null {
  const [tableName, setTableName] = useState('')
  const [columns, setColumns] = useState<ColumnDefinition[]>([
    { ...createEmptyColumn(), name: 'Id', type: 'guid', nullable: false, isPrimaryKey: true }
  ])
  const [isSubmitting, setIsSubmitting] = useState(false)
  const [error, setError] = useState<string | null>(null)

  if (!open) return null

  const addColumn = (): void => {
    setColumns([...columns, createEmptyColumn()])
  }

  const removeColumn = (id: string): void => {
    setColumns(columns.filter((c) => c.id !== id))
  }

  const updateColumn = (id: string, updates: Partial<ColumnDefinition>): void => {
    setColumns(
      columns.map((c) => {
        if (c.id !== id) return c

        // If setting as primary key, remove from others
        if (updates.isPrimaryKey) {
          return { ...c, ...updates, nullable: false }
        }
        return { ...c, ...updates }
      })
    )

    // If setting primary key, unset others
    if (updates.isPrimaryKey) {
      setColumns((cols) =>
        cols.map((c) => (c.id === id ? c : { ...c, isPrimaryKey: false }))
      )
    }
  }

  const handleSubmit = async (e: React.FormEvent): Promise<void> => {
    e.preventDefault()
    setError(null)

    if (!tableName.trim()) {
      setError('Table name is required')
      return
    }

    if (columns.length === 0) {
      setError('At least one column is required')
      return
    }

    if (columns.some((c) => !c.name.trim())) {
      setError('All columns must have a name')
      return
    }

    setIsSubmitting(true)
    try {
      await onSubmit(tableName, columns)
      handleClose()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to create table')
    } finally {
      setIsSubmitting(false)
    }
  }

  const handleClose = (): void => {
    onOpenChange(false)
    setTableName('')
    setColumns([
      { ...createEmptyColumn(), name: 'Id', type: 'guid', nullable: false, isPrimaryKey: true }
    ])
    setError(null)
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50">
      <div className="w-full max-w-2xl max-h-[90vh] overflow-hidden rounded-lg bg-card shadow-xl flex flex-col">
        {/* Header */}
        <div className="flex items-center justify-between border-b border-border px-6 py-4">
          <h2 className="text-lg font-semibold">Create New Table</h2>
          <Button variant="ghost" size="icon" className="h-8 w-8" onClick={handleClose}>
            <X className="h-4 w-4" />
          </Button>
        </div>

        {/* Content */}
        <form onSubmit={handleSubmit} className="flex flex-col flex-1 overflow-hidden">
          <div className="flex-1 overflow-y-auto px-6 py-4 space-y-4">
            {/* Table Name */}
            <div className="space-y-2">
              <Label htmlFor="tableName">Table Name</Label>
              <Input
                id="tableName"
                placeholder="Users, Products, Orders..."
                value={tableName}
                onChange={(e) => setTableName(e.target.value)}
                required
              />
            </div>

            {/* Columns */}
            <div className="space-y-2">
              <div className="flex items-center justify-between">
                <Label>Columns</Label>
                <Button type="button" variant="outline" size="sm" onClick={addColumn}>
                  <Plus className="h-4 w-4 mr-1" />
                  Add Column
                </Button>
              </div>

              <div className="space-y-2 max-h-[40vh] overflow-y-auto">
                {columns.map((column, index) => (
                  <div
                    key={column.id}
                    className="flex items-start gap-2 p-3 rounded-lg border border-border bg-background"
                  >
                    {/* Primary Key Indicator */}
                    <button
                      type="button"
                      onClick={() => updateColumn(column.id, { isPrimaryKey: !column.isPrimaryKey })}
                      className={cn(
                        'mt-2 p-1 rounded',
                        column.isPrimaryKey ? 'text-warning' : 'text-muted-foreground/30 hover:text-muted-foreground'
                      )}
                      title={column.isPrimaryKey ? 'Primary Key' : 'Set as Primary Key'}
                    >
                      <Key className="h-4 w-4" />
                    </button>

                    {/* Column Name */}
                    <div className="flex-1 space-y-1">
                      <Input
                        placeholder="Column name"
                        value={column.name}
                        onChange={(e) => updateColumn(column.id, { name: e.target.value })}
                        className="h-8"
                      />
                    </div>

                    {/* Data Type */}
                    <div className="w-32">
                      <div className="relative">
                        <select
                          value={column.type}
                          onChange={(e) => updateColumn(column.id, { type: e.target.value })}
                          className="w-full h-8 appearance-none rounded-md border border-input bg-background px-2 pr-8 text-sm focus:outline-none focus:ring-2 focus:ring-ring"
                        >
                          {DATA_TYPES.map((dt) => (
                            <option key={dt.value} value={dt.value}>
                              {dt.label}
                            </option>
                          ))}
                        </select>
                        <ChevronDown className="absolute right-2 top-2 h-4 w-4 text-muted-foreground pointer-events-none" />
                      </div>
                    </div>

                    {/* Flags */}
                    <div className="flex items-center gap-2 mt-1">
                      <label className="flex items-center gap-1 text-xs">
                        <input
                          type="checkbox"
                          checked={column.nullable}
                          onChange={(e) => updateColumn(column.id, { nullable: e.target.checked })}
                          disabled={column.isPrimaryKey}
                          className="rounded"
                        />
                        Null
                      </label>
                      <label className="flex items-center gap-1 text-xs">
                        <input
                          type="checkbox"
                          checked={column.unique}
                          onChange={(e) => updateColumn(column.id, { unique: e.target.checked })}
                          className="rounded"
                        />
                        Unique
                      </label>
                      <label className="flex items-center gap-1 text-xs">
                        <input
                          type="checkbox"
                          checked={column.indexed}
                          onChange={(e) => updateColumn(column.id, { indexed: e.target.checked })}
                          className="rounded"
                        />
                        Index
                      </label>
                    </div>

                    {/* Delete */}
                    <Button
                      type="button"
                      variant="ghost"
                      size="icon"
                      className="h-8 w-8 text-muted-foreground hover:text-destructive"
                      onClick={() => removeColumn(column.id)}
                      disabled={columns.length === 1}
                    >
                      <Trash2 className="h-4 w-4" />
                    </Button>
                  </div>
                ))}
              </div>
            </div>

            {/* Error */}
            {error && (
              <div className="p-3 rounded-md bg-destructive/10 text-destructive text-sm">
                {error}
              </div>
            )}
          </div>

          {/* Footer */}
          <div className="flex justify-end gap-2 border-t border-border px-6 py-4">
            <Button type="button" variant="outline" onClick={handleClose}>
              Cancel
            </Button>
            <Button type="submit" disabled={isSubmitting}>
              {isSubmitting && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}
              Create Table
            </Button>
          </div>
        </form>
      </div>
    </div>
  )
}
