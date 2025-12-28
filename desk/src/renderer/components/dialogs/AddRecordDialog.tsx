import { useState, useEffect, type ReactElement } from 'react'
import { X, Loader2, ChevronDown } from 'lucide-react'
import { Button } from '@/components/ui/Button'
import { Input } from '@/components/ui/Input'
import { Label } from '@/components/ui/Label'
import type { ColumnApiResponse } from '@/lib/api'

interface AddRecordDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  tableName: string
  columns: ColumnApiResponse[]
  onSubmit: (data: Record<string, unknown>) => Promise<void>
}

function getDefaultValue(column: ColumnApiResponse): unknown {
  if (column.defaultValue) {
    return column.defaultValue
  }
  if (column.isNullable) {
    return null
  }

  const type = column.dataType.toLowerCase()
  if (type.includes('int') || type === 'long' || type.includes('decimal') || type === 'double') {
    return 0
  }
  if (type === 'bool' || type === 'boolean') {
    return false
  }
  if (type === 'guid') {
    return crypto.randomUUID()
  }
  if (type === 'datetime') {
    return new Date().toISOString()
  }
  return ''
}

function parseValue(value: string, dataType: string): unknown {
  if (value === '' || value.toLowerCase() === 'null') {
    return null
  }

  const type = dataType.toLowerCase()
  if (type.includes('int') || type === 'long') {
    const parsed = parseInt(value, 10)
    return isNaN(parsed) ? value : parsed
  }
  if (type.includes('decimal') || type === 'double' || type === 'float') {
    const parsed = parseFloat(value)
    return isNaN(parsed) ? value : parsed
  }
  if (type === 'bool' || type === 'boolean') {
    return value.toLowerCase() === 'true'
  }
  if (type === 'json') {
    try {
      return JSON.parse(value)
    } catch {
      return value
    }
  }
  return value
}

export function AddRecordDialog({
  open,
  onOpenChange,
  tableName,
  columns,
  onSubmit
}: AddRecordDialogProps): ReactElement | null {
  const [formData, setFormData] = useState<Record<string, string>>({})
  const [isSubmitting, setIsSubmitting] = useState(false)
  const [error, setError] = useState<string | null>(null)

  // Initialize form data when dialog opens
  useEffect(() => {
    if (open) {
      const initial: Record<string, string> = {}
      columns.forEach((col) => {
        const defaultVal = getDefaultValue(col)
        initial[col.name] = defaultVal === null ? '' : String(defaultVal)
      })
      setFormData(initial)
      setError(null)
    }
  }, [open, columns])

  if (!open) return null

  const editableColumns = columns.filter((col) => !col.isPrimaryKey || col.dataType.toLowerCase() !== 'guid')

  const handleSubmit = async (e: React.FormEvent): Promise<void> => {
    e.preventDefault()
    setError(null)

    // Validate required fields
    for (const col of columns) {
      if (!col.isNullable && !col.isPrimaryKey) {
        const value = formData[col.name]
        if (value === '' || value === undefined) {
          setError(`${col.displayName || col.name} is required`)
          return
        }
      }
    }

    setIsSubmitting(true)
    try {
      // Parse values based on data types
      const parsedData: Record<string, unknown> = {}
      columns.forEach((col) => {
        const rawValue = formData[col.name] ?? ''
        parsedData[col.name] = parseValue(rawValue, col.dataType)
      })

      await onSubmit(parsedData)
      handleClose()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to add record')
    } finally {
      setIsSubmitting(false)
    }
  }

  const handleClose = (): void => {
    onOpenChange(false)
    setFormData({})
    setError(null)
  }

  const renderInput = (column: ColumnApiResponse): ReactElement => {
    const value = formData[column.name] ?? ''
    const type = column.dataType.toLowerCase()

    if (type === 'bool' || type === 'boolean') {
      return (
        <div className="relative">
          <select
            value={value}
            onChange={(e) => setFormData({ ...formData, [column.name]: e.target.value })}
            className="w-full h-9 appearance-none rounded-md border border-input bg-background px-3 pr-8 text-sm focus:outline-none focus:ring-2 focus:ring-ring"
          >
            <option value="">NULL</option>
            <option value="true">true</option>
            <option value="false">false</option>
          </select>
          <ChevronDown className="absolute right-3 top-2.5 h-4 w-4 text-muted-foreground pointer-events-none" />
        </div>
      )
    }

    if (type === 'json') {
      return (
        <textarea
          value={value}
          onChange={(e) => setFormData({ ...formData, [column.name]: e.target.value })}
          placeholder="{}"
          className="w-full min-h-[60px] rounded-md border border-input bg-background px-3 py-2 text-sm font-mono focus:outline-none focus:ring-2 focus:ring-ring"
        />
      )
    }

    return (
      <Input
        value={value}
        onChange={(e) => setFormData({ ...formData, [column.name]: e.target.value })}
        placeholder={column.isNullable ? 'NULL' : `Enter ${column.displayName || column.name}...`}
        type={type.includes('int') || type.includes('decimal') || type === 'double' ? 'text' : 'text'}
      />
    )
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50">
      <div className="w-full max-w-lg max-h-[90vh] overflow-hidden rounded-lg bg-card shadow-xl flex flex-col">
        {/* Header */}
        <div className="flex items-center justify-between border-b border-border px-6 py-4">
          <div>
            <h2 className="text-lg font-semibold">Add Record</h2>
            <p className="text-sm text-muted-foreground">Table: {tableName}</p>
          </div>
          <Button variant="ghost" size="icon" className="h-8 w-8" onClick={handleClose}>
            <X className="h-4 w-4" />
          </Button>
        </div>

        {/* Form */}
        <form onSubmit={handleSubmit} className="flex flex-col flex-1 overflow-hidden">
          <div className="flex-1 overflow-y-auto px-6 py-4 space-y-4">
            {editableColumns.map((column) => (
              <div key={column.id} className="space-y-2">
                <Label htmlFor={column.name}>
                  {column.displayName || column.name}
                  {!column.isNullable && <span className="text-destructive ml-1">*</span>}
                  <span className="ml-2 text-xs text-muted-foreground font-normal">
                    ({column.dataType})
                  </span>
                </Label>
                {renderInput(column)}
              </div>
            ))}

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
              Add Record
            </Button>
          </div>
        </form>
      </div>
    </div>
  )
}
