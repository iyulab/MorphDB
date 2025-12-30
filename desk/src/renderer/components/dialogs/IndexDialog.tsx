import { useState, useEffect, type ReactElement } from 'react'
import { X, Plus, Trash2 } from 'lucide-react'
import { Button } from '@/components/ui/Button'
import type { ColumnApiResponse } from '@/lib/api'

interface IndexDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  tableName: string
  columns: ColumnApiResponse[]
  onSubmit: (data: IndexFormData) => Promise<void>
}

export interface IndexFormData {
  name: string
  columns: string[]
  type: 'btree' | 'hash' | 'gin' | 'gist'
  unique: boolean
}

export function IndexDialog({
  open,
  onOpenChange,
  tableName,
  columns,
  onSubmit
}: IndexDialogProps): ReactElement | null {
  const [formData, setFormData] = useState<IndexFormData>({
    name: '',
    columns: [],
    type: 'btree',
    unique: false
  })
  const [isSubmitting, setIsSubmitting] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (open) {
      setFormData({
        name: '',
        columns: [],
        type: 'btree',
        unique: false
      })
      setError(null)
    }
  }, [open])

  const handleSubmit = async (e: React.FormEvent): Promise<void> => {
    e.preventDefault()
    setError(null)

    if (!formData.name.trim()) {
      setError('Index name is required')
      return
    }

    if (formData.columns.length === 0) {
      setError('At least one column is required')
      return
    }

    setIsSubmitting(true)
    try {
      await onSubmit(formData)
      onOpenChange(false)
    } catch (err) {
      setError((err as Error).message)
    } finally {
      setIsSubmitting(false)
    }
  }

  const addColumn = (columnName: string): void => {
    if (!formData.columns.includes(columnName)) {
      setFormData((prev) => ({
        ...prev,
        columns: [...prev.columns, columnName]
      }))
    }
  }

  const removeColumn = (columnName: string): void => {
    setFormData((prev) => ({
      ...prev,
      columns: prev.columns.filter((c) => c !== columnName)
    }))
  }

  const availableColumns = columns.filter((c) => !formData.columns.includes(c.name))

  if (!open) return null

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center">
      <div className="fixed inset-0 bg-black/50" onClick={() => onOpenChange(false)} />
      <div className="relative z-50 w-full max-w-md rounded-lg border bg-background p-6 shadow-lg">
        <div className="flex items-center justify-between mb-4">
          <h2 className="text-lg font-semibold">Create Index</h2>
          <Button variant="ghost" size="icon" onClick={() => onOpenChange(false)}>
            <X className="h-4 w-4" />
          </Button>
        </div>

        <form onSubmit={handleSubmit} className="space-y-4">
          <div>
            <label className="block text-sm font-medium mb-1">Table</label>
            <input
              type="text"
              value={tableName}
              disabled
              className="w-full rounded-md border bg-muted px-3 py-2 text-sm"
            />
          </div>

          <div>
            <label className="block text-sm font-medium mb-1">Index Name</label>
            <input
              type="text"
              value={formData.name}
              onChange={(e) => setFormData((prev) => ({ ...prev, name: e.target.value }))}
              placeholder="idx_column_name"
              className="w-full rounded-md border bg-background px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-primary"
            />
          </div>

          <div>
            <label className="block text-sm font-medium mb-1">Index Type</label>
            <select
              value={formData.type}
              onChange={(e) =>
                setFormData((prev) => ({
                  ...prev,
                  type: e.target.value as IndexFormData['type']
                }))
              }
              className="w-full rounded-md border bg-background px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-primary"
            >
              <option value="btree">B-Tree (default)</option>
              <option value="hash">Hash</option>
              <option value="gin">GIN</option>
              <option value="gist">GiST</option>
            </select>
          </div>

          <div className="flex items-center gap-2">
            <input
              type="checkbox"
              id="unique"
              checked={formData.unique}
              onChange={(e) =>
                setFormData((prev) => ({ ...prev, unique: e.target.checked }))
              }
              className="rounded border-gray-300"
            />
            <label htmlFor="unique" className="text-sm">
              Unique index
            </label>
          </div>

          <div>
            <label className="block text-sm font-medium mb-1">Columns</label>
            {formData.columns.length > 0 && (
              <div className="space-y-1 mb-2">
                {formData.columns.map((col, idx) => (
                  <div
                    key={col}
                    className="flex items-center justify-between bg-muted rounded px-2 py-1 text-sm"
                  >
                    <span>
                      {idx + 1}. {col}
                    </span>
                    <Button
                      type="button"
                      variant="ghost"
                      size="icon"
                      className="h-6 w-6"
                      onClick={() => removeColumn(col)}
                    >
                      <Trash2 className="h-3 w-3" />
                    </Button>
                  </div>
                ))}
              </div>
            )}
            {availableColumns.length > 0 && (
              <select
                value=""
                onChange={(e) => {
                  if (e.target.value) addColumn(e.target.value)
                }}
                className="w-full rounded-md border bg-background px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-primary"
              >
                <option value="">Add column...</option>
                {availableColumns.map((col) => (
                  <option key={col.id} value={col.name}>
                    {col.name} ({col.dataType})
                  </option>
                ))}
              </select>
            )}
          </div>

          {error && <p className="text-sm text-destructive">{error}</p>}

          <div className="flex justify-end gap-2 pt-4">
            <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>
              Cancel
            </Button>
            <Button type="submit" disabled={isSubmitting}>
              {isSubmitting ? 'Creating...' : 'Create Index'}
            </Button>
          </div>
        </form>
      </div>
    </div>
  )
}
