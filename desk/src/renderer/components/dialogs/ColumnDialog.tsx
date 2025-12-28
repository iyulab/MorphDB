import { useState, useEffect, type ReactElement } from 'react'
import { X, Loader2, ChevronDown } from 'lucide-react'
import { Button } from '@/components/ui/Button'
import { Input } from '@/components/ui/Input'
import { Label } from '@/components/ui/Label'
import type { ColumnApiResponse } from '@/lib/api'

interface ColumnDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  tableName: string
  column?: ColumnApiResponse | null // null = add mode
  onSubmit: (data: ColumnFormData) => Promise<void>
}

export interface ColumnFormData {
  name: string
  type: string
  nullable: boolean
  unique: boolean
  indexed: boolean
  defaultValue?: string
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

const initialFormData: ColumnFormData = {
  name: '',
  type: 'string',
  nullable: true,
  unique: false,
  indexed: false,
  defaultValue: ''
}

export function ColumnDialog({
  open,
  onOpenChange,
  tableName,
  column,
  onSubmit
}: ColumnDialogProps): ReactElement | null {
  const [formData, setFormData] = useState<ColumnFormData>(initialFormData)
  const [isSubmitting, setIsSubmitting] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const isEditMode = !!column

  useEffect(() => {
    if (column) {
      setFormData({
        name: column.name,
        type: column.dataType.toLowerCase(),
        nullable: column.isNullable,
        unique: column.isUnique,
        indexed: column.isIndexed,
        defaultValue: column.defaultValue || ''
      })
    } else {
      setFormData(initialFormData)
    }
    setError(null)
  }, [column, open])

  if (!open) return null

  const handleSubmit = async (e: React.FormEvent): Promise<void> => {
    e.preventDefault()
    setError(null)

    if (!formData.name.trim()) {
      setError('Column name is required')
      return
    }

    setIsSubmitting(true)
    try {
      await onSubmit(formData)
      handleClose()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to save column')
    } finally {
      setIsSubmitting(false)
    }
  }

  const handleClose = (): void => {
    onOpenChange(false)
    setFormData(initialFormData)
    setError(null)
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50">
      <div className="w-full max-w-md rounded-lg bg-card shadow-xl">
        {/* Header */}
        <div className="flex items-center justify-between border-b border-border px-6 py-4">
          <div>
            <h2 className="text-lg font-semibold">
              {isEditMode ? 'Edit Column' : 'Add Column'}
            </h2>
            <p className="text-sm text-muted-foreground">
              Table: {tableName}
            </p>
          </div>
          <Button variant="ghost" size="icon" className="h-8 w-8" onClick={handleClose}>
            <X className="h-4 w-4" />
          </Button>
        </div>

        {/* Form */}
        <form onSubmit={handleSubmit} className="p-6 space-y-4">
          {/* Column Name */}
          <div className="space-y-2">
            <Label htmlFor="columnName">Column Name</Label>
            <Input
              id="columnName"
              placeholder="email, status, createdAt..."
              value={formData.name}
              onChange={(e) => setFormData({ ...formData, name: e.target.value })}
              required
              disabled={isEditMode} // Can't rename in edit mode for safety
            />
          </div>

          {/* Data Type */}
          <div className="space-y-2">
            <Label htmlFor="dataType">Data Type</Label>
            <div className="relative">
              <select
                id="dataType"
                value={formData.type}
                onChange={(e) => setFormData({ ...formData, type: e.target.value })}
                className="w-full h-9 appearance-none rounded-md border border-input bg-background px-3 pr-8 text-sm focus:outline-none focus:ring-2 focus:ring-ring"
                disabled={isEditMode} // Can't change type in edit mode
              >
                {DATA_TYPES.map((dt) => (
                  <option key={dt.value} value={dt.value}>
                    {dt.label}
                  </option>
                ))}
              </select>
              <ChevronDown className="absolute right-3 top-2.5 h-4 w-4 text-muted-foreground pointer-events-none" />
            </div>
          </div>

          {/* Default Value */}
          <div className="space-y-2">
            <Label htmlFor="defaultValue">Default Value (optional)</Label>
            <Input
              id="defaultValue"
              placeholder="NULL, 0, 'default'..."
              value={formData.defaultValue}
              onChange={(e) => setFormData({ ...formData, defaultValue: e.target.value })}
            />
          </div>

          {/* Flags */}
          <div className="flex flex-wrap gap-4">
            <label className="flex items-center gap-2 cursor-pointer">
              <input
                type="checkbox"
                checked={formData.nullable}
                onChange={(e) => setFormData({ ...formData, nullable: e.target.checked })}
                className="rounded"
              />
              <span className="text-sm">Nullable</span>
            </label>
            <label className="flex items-center gap-2 cursor-pointer">
              <input
                type="checkbox"
                checked={formData.unique}
                onChange={(e) => setFormData({ ...formData, unique: e.target.checked })}
                className="rounded"
              />
              <span className="text-sm">Unique</span>
            </label>
            <label className="flex items-center gap-2 cursor-pointer">
              <input
                type="checkbox"
                checked={formData.indexed}
                onChange={(e) => setFormData({ ...formData, indexed: e.target.checked })}
                className="rounded"
              />
              <span className="text-sm">Indexed</span>
            </label>
          </div>

          {/* Error */}
          {error && (
            <div className="p-3 rounded-md bg-destructive/10 text-destructive text-sm">
              {error}
            </div>
          )}

          {/* Actions */}
          <div className="flex justify-end gap-2 pt-2">
            <Button type="button" variant="outline" onClick={handleClose}>
              Cancel
            </Button>
            <Button type="submit" disabled={isSubmitting}>
              {isSubmitting && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}
              {isEditMode ? 'Save Changes' : 'Add Column'}
            </Button>
          </div>
        </form>
      </div>
    </div>
  )
}
