import { useState, useEffect, type ReactElement } from 'react'
import { X } from 'lucide-react'
import { Button } from '@/components/ui/Button'
import type { TableApiResponse } from '@/lib/api'

interface RelationDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  sourceTable: TableApiResponse
  tables: TableApiResponse[]
  onSubmit: (data: RelationFormData) => Promise<void>
}

export interface RelationFormData {
  name: string
  sourceColumn: string
  targetTable: string
  targetColumn: string
  type: 'one-to-one' | 'one-to-many' | 'many-to-one' | 'many-to-many'
  onDelete: 'no-action' | 'cascade' | 'set-null' | 'restrict'
}

export function RelationDialog({
  open,
  onOpenChange,
  sourceTable,
  tables,
  onSubmit
}: RelationDialogProps): ReactElement | null {
  const [formData, setFormData] = useState<RelationFormData>({
    name: '',
    sourceColumn: '',
    targetTable: '',
    targetColumn: '',
    type: 'one-to-many',
    onDelete: 'no-action'
  })
  const [isSubmitting, setIsSubmitting] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (open) {
      setFormData({
        name: '',
        sourceColumn: '',
        targetTable: '',
        targetColumn: '',
        type: 'one-to-many',
        onDelete: 'no-action'
      })
      setError(null)
    }
  }, [open])

  const selectedTargetTable = tables.find((t) => t.name === formData.targetTable)

  const handleSubmit = async (e: React.FormEvent): Promise<void> => {
    e.preventDefault()
    setError(null)

    if (!formData.name.trim()) {
      setError('Relation name is required')
      return
    }

    if (!formData.sourceColumn) {
      setError('Source column is required')
      return
    }

    if (!formData.targetTable) {
      setError('Target table is required')
      return
    }

    if (!formData.targetColumn) {
      setError('Target column is required')
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

  if (!open) return null

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center">
      <div className="fixed inset-0 bg-black/50" onClick={() => onOpenChange(false)} />
      <div className="relative z-50 w-full max-w-md rounded-lg border bg-background p-6 shadow-lg max-h-[90vh] overflow-y-auto">
        <div className="flex items-center justify-between mb-4">
          <h2 className="text-lg font-semibold">Create Relation</h2>
          <Button variant="ghost" size="icon" onClick={() => onOpenChange(false)}>
            <X className="h-4 w-4" />
          </Button>
        </div>

        <form onSubmit={handleSubmit} className="space-y-4">
          <div>
            <label className="block text-sm font-medium mb-1">Relation Name</label>
            <input
              type="text"
              value={formData.name}
              onChange={(e) => setFormData((prev) => ({ ...prev, name: e.target.value }))}
              placeholder="fk_source_target"
              className="w-full rounded-md border bg-background px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-primary"
            />
          </div>

          <div className="p-3 rounded-md bg-muted/50 space-y-3">
            <h3 className="text-sm font-medium">Source</h3>
            <div>
              <label className="block text-xs text-muted-foreground mb-1">Table</label>
              <input
                type="text"
                value={sourceTable.name}
                disabled
                className="w-full rounded-md border bg-muted px-3 py-2 text-sm"
              />
            </div>
            <div>
              <label className="block text-xs text-muted-foreground mb-1">Column</label>
              <select
                value={formData.sourceColumn}
                onChange={(e) =>
                  setFormData((prev) => ({ ...prev, sourceColumn: e.target.value }))
                }
                className="w-full rounded-md border bg-background px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-primary"
              >
                <option value="">Select column...</option>
                {sourceTable.columns.map((col) => (
                  <option key={col.id} value={col.name}>
                    {col.name} ({col.dataType})
                  </option>
                ))}
              </select>
            </div>
          </div>

          <div className="p-3 rounded-md bg-muted/50 space-y-3">
            <h3 className="text-sm font-medium">Target</h3>
            <div>
              <label className="block text-xs text-muted-foreground mb-1">Table</label>
              <select
                value={formData.targetTable}
                onChange={(e) =>
                  setFormData((prev) => ({
                    ...prev,
                    targetTable: e.target.value,
                    targetColumn: ''
                  }))
                }
                className="w-full rounded-md border bg-background px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-primary"
              >
                <option value="">Select table...</option>
                {tables.map((table) => (
                  <option key={table.id} value={table.name}>
                    {table.name}
                  </option>
                ))}
              </select>
            </div>
            <div>
              <label className="block text-xs text-muted-foreground mb-1">Column</label>
              <select
                value={formData.targetColumn}
                onChange={(e) =>
                  setFormData((prev) => ({ ...prev, targetColumn: e.target.value }))
                }
                disabled={!selectedTargetTable}
                className="w-full rounded-md border bg-background px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-primary disabled:bg-muted"
              >
                <option value="">Select column...</option>
                {selectedTargetTable?.columns.map((col) => (
                  <option key={col.id} value={col.name}>
                    {col.name} ({col.dataType})
                  </option>
                ))}
              </select>
            </div>
          </div>

          <div className="grid grid-cols-2 gap-4">
            <div>
              <label className="block text-sm font-medium mb-1">Relation Type</label>
              <select
                value={formData.type}
                onChange={(e) =>
                  setFormData((prev) => ({
                    ...prev,
                    type: e.target.value as RelationFormData['type']
                  }))
                }
                className="w-full rounded-md border bg-background px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-primary"
              >
                <option value="one-to-one">One to One</option>
                <option value="one-to-many">One to Many</option>
                <option value="many-to-one">Many to One</option>
                <option value="many-to-many">Many to Many</option>
              </select>
            </div>

            <div>
              <label className="block text-sm font-medium mb-1">On Delete</label>
              <select
                value={formData.onDelete}
                onChange={(e) =>
                  setFormData((prev) => ({
                    ...prev,
                    onDelete: e.target.value as RelationFormData['onDelete']
                  }))
                }
                className="w-full rounded-md border bg-background px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-primary"
              >
                <option value="no-action">No Action</option>
                <option value="cascade">Cascade</option>
                <option value="set-null">Set Null</option>
                <option value="restrict">Restrict</option>
              </select>
            </div>
          </div>

          {error && <p className="text-sm text-destructive">{error}</p>}

          <div className="flex justify-end gap-2 pt-4">
            <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>
              Cancel
            </Button>
            <Button type="submit" disabled={isSubmitting}>
              {isSubmitting ? 'Creating...' : 'Create Relation'}
            </Button>
          </div>
        </form>
      </div>
    </div>
  )
}
