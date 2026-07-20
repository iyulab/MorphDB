import { useState, type ReactElement } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import {
  X,
  Loader2,
  AlertCircle,
  Plus,
  Trash2,
  Upload,
  Pencil,
  Check,
  XCircle
} from 'lucide-react'
import { Button } from '@/components/ui/Button'
import {
  MorphDBClient,
  type ColumnApiResponse,
  type BatchOperation,
  type BatchResponse
} from '@/lib/api'
import { useConnectionStore } from '@/stores/connectionStore'
import { cn } from '@/lib/utils'

interface BulkOperationsDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  tableName: string
  columns: ColumnApiResponse[]
}

type OperationType = 'INSERT' | 'UPDATE' | 'DELETE'

interface OperationRow {
  id: string
  type: OperationType
  recordId: string
  data: Record<string, string>
}

export function BulkOperationsDialog({
  open,
  onOpenChange,
  tableName,
  columns
}: BulkOperationsDialogProps): ReactElement | null {
  const { activeConnection, getApiKey } = useConnectionStore()
  const queryClient = useQueryClient()

  const [operations, setOperations] = useState<OperationRow[]>([])
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

  const executeMutation = useMutation({
    mutationFn: async (ops: BatchOperation[]) => {
      const client = await createClient()
      if (!client) throw new Error('No active connection')
      return client.executeBatch({ operations: ops })
    },
    onSuccess: (data) => {
      setResult(data)
      queryClient.invalidateQueries({ queryKey: ['table-data'] })
    },
    onError: (err) => {
      setError((err as Error).message)
    }
  })

  const addOperation = (type: OperationType): void => {
    const newId = crypto.randomUUID()
    const initialData: Record<string, string> = {}
    editableColumns.forEach(col => {
      initialData[col.name] = ''
    })

    setOperations(prev => [
      ...prev,
      { id: newId, type, recordId: '', data: initialData }
    ])
    setResult(null)
    setError(null)
  }

  const removeOperation = (id: string): void => {
    setOperations(prev => prev.filter(op => op.id !== id))
  }

  const updateOperation = (id: string, field: string, value: string): void => {
    setOperations(prev =>
      prev.map(op => {
        if (op.id !== id) return op
        if (field === 'recordId') {
          return { ...op, recordId: value }
        }
        return { ...op, data: { ...op.data, [field]: value } }
      })
    )
  }

  const executeOperations = async (): Promise<void> => {
    setError(null)
    setResult(null)

    const batchOps: BatchOperation[] = operations.map(op => {
      const data: Record<string, unknown> = {}
      Object.entries(op.data).forEach(([key, value]) => {
        if (value !== '') {
          // Try to parse as number or boolean
          if (value === 'true') data[key] = true
          else if (value === 'false') data[key] = false
          else if (!isNaN(Number(value)) && value.trim() !== '') data[key] = Number(value)
          else data[key] = value
        }
      })

      if (op.type === 'INSERT') {
        return { method: 'INSERT', table: tableName, data }
      } else if (op.type === 'UPDATE') {
        return { method: 'UPDATE', table: tableName, id: op.recordId, data }
      } else {
        return { method: 'DELETE', table: tableName, id: op.recordId }
      }
    })

    await executeMutation.mutateAsync(batchOps)
  }

  const clearAll = (): void => {
    setOperations([])
    setResult(null)
    setError(null)
  }

  const handleClose = (): void => {
    onOpenChange(false)
    // Reset state after a delay to allow animation
    setTimeout(() => {
      setOperations([])
      setResult(null)
      setError(null)
    }, 200)
  }

  if (!open) return null

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center">
      <div className="fixed inset-0 bg-black/50" onClick={handleClose} />
      <div className="relative z-50 w-full max-w-4xl rounded-lg border bg-background p-6 shadow-lg max-h-[90vh] overflow-y-auto">
        <div className="flex items-center justify-between mb-4">
          <h2 className="text-lg font-semibold">Batch Operations: {tableName}</h2>
          <Button variant="ghost" size="icon" onClick={handleClose}>
            <X className="h-4 w-4" />
          </Button>
        </div>

        {/* Add Operation Buttons */}
        <div className="flex gap-2 mb-4">
          <Button
            variant="outline"
            size="sm"
            onClick={() => addOperation('INSERT')}
            className="gap-1"
          >
            <Plus className="h-4 w-4" />
            Insert
          </Button>
          <Button
            variant="outline"
            size="sm"
            onClick={() => addOperation('UPDATE')}
            className="gap-1"
          >
            <Pencil className="h-4 w-4" />
            Update
          </Button>
          <Button
            variant="outline"
            size="sm"
            onClick={() => addOperation('DELETE')}
            className="gap-1"
          >
            <Trash2 className="h-4 w-4" />
            Delete
          </Button>
          {operations.length > 0 && (
            <Button
              variant="ghost"
              size="sm"
              onClick={clearAll}
              className="ml-auto"
            >
              Clear All
            </Button>
          )}
        </div>

        {/* Operations List */}
        {operations.length === 0 ? (
          <div className="text-center py-8 text-muted-foreground">
            <Upload className="h-12 w-12 mx-auto mb-2 opacity-50" />
            <p>Add operations using the buttons above</p>
            <p className="text-sm">Operations will be executed in order</p>
          </div>
        ) : (
          <div className="space-y-3 mb-4">
            {operations.map((op, index) => (
              <div
                key={op.id}
                className={cn(
                  'p-3 rounded-lg border',
                  op.type === 'INSERT' && 'border-success/50 bg-success/5',
                  op.type === 'UPDATE' && 'border-warning/50 bg-warning/5',
                  op.type === 'DELETE' && 'border-destructive/50 bg-destructive/5'
                )}
              >
                <div className="flex items-center justify-between mb-2">
                  <div className="flex items-center gap-2">
                    <span className="text-xs font-mono text-muted-foreground">
                      #{index + 1}
                    </span>
                    <span
                      className={cn(
                        'px-2 py-0.5 rounded text-xs font-medium',
                        op.type === 'INSERT' && 'bg-success/20 text-success',
                        op.type === 'UPDATE' && 'bg-warning/20 text-warning',
                        op.type === 'DELETE' && 'bg-destructive/20 text-destructive'
                      )}
                    >
                      {op.type}
                    </span>
                  </div>
                  <Button
                    variant="ghost"
                    size="icon"
                    onClick={() => removeOperation(op.id)}
                    className="h-6 w-6"
                  >
                    <X className="h-3 w-3" />
                  </Button>
                </div>

                {/* Record ID for UPDATE/DELETE */}
                {(op.type === 'UPDATE' || op.type === 'DELETE') && (
                  <div className="mb-2">
                    <label className="text-xs text-muted-foreground mb-1 block">
                      Record ID (_id)
                    </label>
                    <input
                      type="text"
                      value={op.recordId}
                      onChange={(e) => updateOperation(op.id, 'recordId', e.target.value)}
                      placeholder="Enter record UUID"
                      className="w-full rounded border bg-background px-2 py-1 text-sm"
                    />
                  </div>
                )}

                {/* Data fields for INSERT/UPDATE */}
                {(op.type === 'INSERT' || op.type === 'UPDATE') && (
                  <div className="grid grid-cols-2 md:grid-cols-3 gap-2">
                    {editableColumns.map(col => (
                      <div key={col.name}>
                        <label className="text-xs text-muted-foreground mb-1 block">
                          {col.displayName || col.name}
                        </label>
                        <input
                          type="text"
                          value={op.data[col.name] || ''}
                          onChange={(e) => updateOperation(op.id, col.name, e.target.value)}
                          placeholder={col.dataType}
                          className="w-full rounded border bg-background px-2 py-1 text-sm"
                        />
                      </div>
                    ))}
                  </div>
                )}

                {/* Result indicator */}
                {result && result.results[index] && (
                  <div className="mt-2 flex items-center gap-1 text-xs">
                    {result.results[index].success ? (
                      <>
                        <Check className="h-3 w-3 text-success" />
                        <span className="text-success">
                          Success ({result.results[index].affectedRows} affected)
                        </span>
                      </>
                    ) : (
                      <>
                        <XCircle className="h-3 w-3 text-destructive" />
                        <span className="text-destructive">
                          {result.results[index].error}
                        </span>
                      </>
                    )}
                  </div>
                )}
              </div>
            ))}
          </div>
        )}

        {/* Error */}
        {error && (
          <div className="flex items-center gap-2 p-3 mb-4 rounded bg-destructive/10 text-destructive text-sm">
            <AlertCircle className="h-4 w-4" />
            {error}
          </div>
        )}

        {/* Result Summary */}
        {result && (
          <div className="flex items-center gap-4 p-3 mb-4 rounded bg-muted text-sm">
            <span>
              <strong>Total:</strong> {result.results.length}
            </span>
            <span className="text-success">
              <strong>Success:</strong> {result.successCount}
            </span>
            <span className="text-destructive">
              <strong>Failed:</strong> {result.failureCount}
            </span>
          </div>
        )}

        {/* Actions */}
        <div className="flex justify-end gap-2">
          <Button variant="outline" onClick={handleClose}>
            Close
          </Button>
          <Button
            onClick={executeOperations}
            disabled={operations.length === 0 || executeMutation.isPending}
          >
            {executeMutation.isPending ? (
              <Loader2 className="h-4 w-4 mr-2 animate-spin" />
            ) : (
              <Upload className="h-4 w-4 mr-2" />
            )}
            Execute {operations.length} Operation{operations.length !== 1 ? 's' : ''}
          </Button>
        </div>
      </div>
    </div>
  )
}
