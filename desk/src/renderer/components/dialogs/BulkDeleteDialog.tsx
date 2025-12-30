import { useState, type ReactElement } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { X, Loader2, AlertCircle, AlertTriangle, Trash2 } from 'lucide-react'
import { Button } from '@/components/ui/Button'
import { MorphDBClient, type ColumnApiResponse, type BatchResponse } from '@/lib/api'
import { useConnectionStore } from '@/stores/connectionStore'

interface BulkDeleteDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  tableName: string
  columns: ColumnApiResponse[]
}

export function BulkDeleteDialog({
  open,
  onOpenChange,
  tableName,
  columns
}: BulkDeleteDialogProps): ReactElement | null {
  const { activeConnection, getApiKey } = useConnectionStore()
  const queryClient = useQueryClient()

  const [filter, setFilter] = useState('')
  const [confirmText, setConfirmText] = useState('')
  const [result, setResult] = useState<BatchResponse | null>(null)
  const [error, setError] = useState<string | null>(null)

  const filterableColumns = columns.filter(c => !c.name.startsWith('_'))

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

  const deleteMutation = useMutation({
    mutationFn: async () => {
      const client = await createClient()
      if (!client) throw new Error('No active connection')
      return client.bulkDelete(tableName, filter)
    },
    onSuccess: (data) => {
      setResult(data)
      queryClient.invalidateQueries({ queryKey: ['table-data'] })
    },
    onError: (err) => {
      setError((err as Error).message)
    }
  })

  const handleSubmit = async (): Promise<void> => {
    setError(null)
    setResult(null)

    if (!filter.trim()) {
      setError('A filter is required for bulk delete operations')
      return
    }

    if (confirmText !== 'DELETE') {
      setError('Please type DELETE to confirm')
      return
    }

    await deleteMutation.mutateAsync()
  }

  const handleClose = (): void => {
    onOpenChange(false)
    setTimeout(() => {
      setFilter('')
      setConfirmText('')
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
            <Trash2 className="h-5 w-5 text-destructive" />
            <h2 className="text-lg font-semibold">Bulk Delete: {tableName}</h2>
          </div>
          <Button variant="ghost" size="icon" onClick={handleClose}>
            <X className="h-4 w-4" />
          </Button>
        </div>

        {/* Warning */}
        <div className="flex items-start gap-2 p-3 mb-4 rounded bg-destructive/10 text-destructive text-sm">
          <AlertTriangle className="h-4 w-4 mt-0.5 shrink-0" />
          <div>
            <p className="font-medium">This action is irreversible!</p>
            <p className="text-xs mt-1 opacity-80">
              All records matching the filter will be permanently deleted.
            </p>
          </div>
        </div>

        {/* Filter */}
        <div className="mb-4">
          <label className="block text-sm font-medium mb-1">
            Filter (required)
          </label>
          <input
            type="text"
            value={filter}
            onChange={(e) => setFilter(e.target.value)}
            placeholder="e.g., status:eq:archived,createdAt:lt:2024-01-01"
            className="w-full rounded-md border bg-background px-3 py-2 text-sm"
          />
          <p className="text-xs text-muted-foreground mt-1">
            Format: column:operator:value (comma-separated). Filter is required.
          </p>
        </div>

        {/* Available Columns Reference */}
        <div className="mb-4">
          <label className="block text-sm font-medium mb-1">
            Available Columns
          </label>
          <div className="flex flex-wrap gap-1">
            {filterableColumns.map(col => (
              <span
                key={col.name}
                className="px-2 py-0.5 rounded bg-muted text-xs text-muted-foreground"
              >
                {col.name}
              </span>
            ))}
          </div>
        </div>

        {/* Confirmation */}
        <div className="mb-4">
          <label className="block text-sm font-medium mb-1">
            Type DELETE to confirm
          </label>
          <input
            type="text"
            value={confirmText}
            onChange={(e) => setConfirmText(e.target.value)}
            placeholder="Type DELETE"
            className="w-full rounded-md border bg-background px-3 py-2 text-sm"
          />
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
          <div className="p-3 mb-4 rounded bg-muted text-sm">
            <p className="font-medium">Delete operation completed</p>
            <p className="text-xs mt-1 text-muted-foreground">
              {result.results[0]?.affectedRows || 0} record(s) deleted
            </p>
          </div>
        )}

        {/* Actions */}
        <div className="flex justify-end gap-2">
          <Button variant="outline" onClick={handleClose}>
            Cancel
          </Button>
          <Button
            variant="destructive"
            onClick={handleSubmit}
            disabled={!filter.trim() || confirmText !== 'DELETE' || deleteMutation.isPending}
          >
            {deleteMutation.isPending ? (
              <Loader2 className="h-4 w-4 mr-2 animate-spin" />
            ) : (
              <Trash2 className="h-4 w-4 mr-2" />
            )}
            Delete Records
          </Button>
        </div>
      </div>
    </div>
  )
}
