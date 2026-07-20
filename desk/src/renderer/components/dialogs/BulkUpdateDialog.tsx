import { useState, type ReactElement } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { X, Loader2, AlertCircle, AlertTriangle, Pencil } from 'lucide-react'
import { Button } from '@/components/ui/Button'
import { MorphDBClient, type ColumnApiResponse, type BatchResponse } from '@/lib/api'
import { useConnectionStore } from '@/stores/connectionStore'

interface BulkUpdateDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  tableName: string
  columns: ColumnApiResponse[]
}

export function BulkUpdateDialog({
  open,
  onOpenChange,
  tableName,
  columns
}: BulkUpdateDialogProps): ReactElement | null {
  const { activeConnection, getApiKey } = useConnectionStore()
  const queryClient = useQueryClient()

  const [filter, setFilter] = useState('')
  const [updateData, setUpdateData] = useState<Record<string, string>>({})
  const [result, setResult] = useState<BatchResponse | null>(null)
  const [error, setError] = useState<string | null>(null)

  const editableColumns = columns.filter(c => !c.isPrimaryKey && !c.name.startsWith('_'))

  const createClient = async (): Promise<MorphDBClient | null> => {
    if (!activeConnection) return null
    const apiKey = await getApiKey(activeConnection.id)
    if (!apiKey) return null
    return new MorphDBClient({
      url: activeConnection.url,
      apiKey,
      projectId: activeConnection.projectId
    })
  }

  const updateMutation = useMutation({
    mutationFn: async () => {
      const client = await createClient()
      if (!client) throw new Error('No active connection')

      // Convert string values to appropriate types
      const data: Record<string, unknown> = {}
      Object.entries(updateData).forEach(([key, value]) => {
        if (value !== '') {
          if (value === 'true') data[key] = true
          else if (value === 'false') data[key] = false
          else if (!isNaN(Number(value)) && value.trim() !== '') data[key] = Number(value)
          else data[key] = value
        }
      })

      return client.bulkUpdate(tableName, data, filter || undefined)
    },
    onSuccess: (data) => {
      setResult(data)
      queryClient.invalidateQueries({ queryKey: ['table-data'] })
    },
    onError: (err) => {
      setError((err as Error).message)
    }
  })

  const handleColumnChange = (columnName: string, value: string): void => {
    setUpdateData(prev => {
      const updated = { ...prev }
      if (value === '') {
        delete updated[columnName]
      } else {
        updated[columnName] = value
      }
      return updated
    })
  }

  const handleSubmit = async (): Promise<void> => {
    setError(null)
    setResult(null)

    if (Object.keys(updateData).length === 0) {
      setError('Please specify at least one column to update')
      return
    }

    await updateMutation.mutateAsync()
  }

  const handleClose = (): void => {
    onOpenChange(false)
    setTimeout(() => {
      setFilter('')
      setUpdateData({})
      setResult(null)
      setError(null)
    }, 200)
  }

  if (!open) return null

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center">
      <div className="fixed inset-0 bg-black/50" onClick={handleClose} />
      <div className="relative z-50 w-full max-w-lg rounded-lg border bg-background p-6 shadow-lg max-h-[90vh] overflow-y-auto">
        <div className="flex items-center justify-between mb-4">
          <div className="flex items-center gap-2">
            <Pencil className="h-5 w-5 text-warning" />
            <h2 className="text-lg font-semibold">Bulk Update: {tableName}</h2>
          </div>
          <Button variant="ghost" size="icon" onClick={handleClose}>
            <X className="h-4 w-4" />
          </Button>
        </div>

        {/* Warning */}
        <div className="flex items-start gap-2 p-3 mb-4 rounded bg-warning/10 text-warning text-sm">
          <AlertTriangle className="h-4 w-4 mt-0.5 shrink-0" />
          <div>
            <p className="font-medium">This will update multiple records</p>
            <p className="text-xs mt-1 opacity-80">
              All records matching the filter will be updated. Use with caution.
            </p>
          </div>
        </div>

        {/* Filter */}
        <div className="mb-4">
          <label className="block text-sm font-medium mb-1">
            Filter (optional)
          </label>
          <input
            type="text"
            value={filter}
            onChange={(e) => setFilter(e.target.value)}
            placeholder="e.g., status:eq:active,category:eq:products"
            className="w-full rounded-md border bg-background px-3 py-2 text-sm"
          />
          <p className="text-xs text-muted-foreground mt-1">
            Format: column:operator:value (comma-separated). Leave empty to update all records.
          </p>
        </div>

        {/* Update Fields */}
        <div className="mb-4">
          <label className="block text-sm font-medium mb-2">
            Fields to Update
          </label>
          <div className="space-y-2 max-h-64 overflow-y-auto">
            {editableColumns.map(col => (
              <div key={col.name} className="flex items-center gap-2">
                <label className="w-1/3 text-sm text-muted-foreground truncate">
                  {col.displayName || col.name}
                </label>
                <input
                  type="text"
                  value={updateData[col.name] || ''}
                  onChange={(e) => handleColumnChange(col.name, e.target.value)}
                  placeholder={`New value (${col.dataType})`}
                  className="flex-1 rounded border bg-background px-2 py-1.5 text-sm"
                />
              </div>
            ))}
          </div>
          <p className="text-xs text-muted-foreground mt-2">
            Only non-empty fields will be updated
          </p>
        </div>

        {/* Error */}
        {error && (
          <div className="flex items-center gap-2 p-3 mb-4 rounded bg-destructive/10 text-destructive text-sm">
            <AlertCircle className="h-4 w-4" />
            {error}
          </div>
        )}

        {/* Result */}
        {result && (
          <div className="p-3 mb-4 rounded bg-success/10 text-success text-sm">
            <p className="font-medium">Update completed successfully</p>
            <p className="text-xs mt-1">
              {result.results[0]?.affectedRows || 0} record(s) updated
            </p>
          </div>
        )}

        {/* Actions */}
        <div className="flex justify-end gap-2">
          <Button variant="outline" onClick={handleClose}>
            Cancel
          </Button>
          <Button
            onClick={handleSubmit}
            disabled={Object.keys(updateData).length === 0 || updateMutation.isPending}
            className="bg-warning text-warning-foreground hover:bg-warning/90"
          >
            {updateMutation.isPending ? (
              <Loader2 className="h-4 w-4 mr-2 animate-spin" />
            ) : (
              <Pencil className="h-4 w-4 mr-2" />
            )}
            Update Records
          </Button>
        </div>
      </div>
    </div>
  )
}
