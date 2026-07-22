import { useState, useEffect, type ReactElement } from 'react'
import { useQuery } from '@tanstack/react-query'
import {
  X,
  Loader2,
  Plus,
  Trash2,
  Eye,
  Database
} from 'lucide-react'
import { Button } from '@/components/ui/Button'
import { Input } from '@/components/ui/Input'
import { Label } from '@/components/ui/Label'
import {
  MorphDBClient,
  type ViewApiResponse,
  type CreateViewApiRequest,
  type UpdateViewApiRequest,
  type ViewColumnApiSpec,
  type ViewFilterApiSpec,
  type ViewOrderApiSpec,
  type TableApiResponse
} from '@/lib/api'
import { useConnectionStore } from '@/stores/connectionStore'
import { cn } from '@/lib/utils'

interface ViewDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  view?: ViewApiResponse | null
  onSubmit: (data: CreateViewApiRequest | UpdateViewApiRequest) => Promise<void>
}

interface ColumnSpec {
  source: string
  alias: string
  expression: string
  aggregation: string
}

interface FilterSpec {
  field: string
  operator: string
  value: string
}

interface OrderSpec {
  column: string
  descending: boolean
}

export function ViewDialog({
  open,
  onOpenChange,
  view,
  onSubmit
}: ViewDialogProps): ReactElement | null {
  const { activeConnection } = useConnectionStore()
  const isEdit = !!view

  const [name, setName] = useState('')
  const [baseTable, setBaseTable] = useState('')
  const [columns, setColumns] = useState<ColumnSpec[]>([{ source: '', alias: '', expression: '', aggregation: '' }])
  const [filters, setFilters] = useState<FilterSpec[]>([])
  const [orderBy, setOrderBy] = useState<OrderSpec[]>([])
  const [groupBy, setGroupBy] = useState<string[]>([])
  const [limit, setLimit] = useState<string>('')
  const [distinct, setDistinct] = useState(false)
  const [materialized, setMaterialized] = useState(false)
  const [refreshPolicy, setRefreshPolicy] = useState('OnDemand')
  const [refreshSchedule, setRefreshSchedule] = useState('')
  const [description, setDescription] = useState('')
  const [isSubmitting, setIsSubmitting] = useState(false)
  const [error, setError] = useState<string | null>(null)

  // Create client helper
  const createClient = async (): Promise<MorphDBClient | null> => {
    if (!activeConnection) return null
    return new MorphDBClient({
      url: activeConnection.url,
      projectId: activeConnection.projectId
    })
  }

  // Fetch available tables
  const { data: tables } = useQuery({
    queryKey: ['tables', activeConnection?.id],
    queryFn: async () => {
      const client = await createClient()
      if (!client) return []
      return client.listTables(activeConnection?.projectId)
    },
    enabled: !!activeConnection && open
  })

  // Reset form when dialog opens
  useEffect(() => {
    if (open) {
      if (view) {
        setName(view.name)
        setBaseTable(view.baseTable)
        setColumns(view.columns.map(c => ({
          source: c.name,
          alias: c.name,
          expression: c.expression || '',
          aggregation: ''
        })))
        setFilters(view.filters?.map(f => ({
          field: f.field,
          operator: f.operator,
          value: String(f.value || '')
        })) || [])
        setOrderBy(view.orderBy?.map(o => ({
          column: o.column,
          descending: o.descending
        })) || [])
        setGroupBy(view.groupBy || [])
        setLimit(view.limit?.toString() || '')
        setDistinct(view.distinct)
        setMaterialized(view.isMaterialized)
        setRefreshPolicy(view.refreshPolicy || 'OnDemand')
        setRefreshSchedule(view.refreshSchedule || '')
        setDescription('')
      } else {
        setName('')
        setBaseTable('')
        setColumns([{ source: '', alias: '', expression: '', aggregation: '' }])
        setFilters([])
        setOrderBy([])
        setGroupBy([])
        setLimit('')
        setDistinct(false)
        setMaterialized(false)
        setRefreshPolicy('OnDemand')
        setRefreshSchedule('')
        setDescription('')
      }
      setError(null)
    }
  }, [open, view])

  const handleAddColumn = (): void => {
    setColumns([...columns, { source: '', alias: '', expression: '', aggregation: '' }])
  }

  const handleRemoveColumn = (index: number): void => {
    setColumns(columns.filter((_, i) => i !== index))
  }

  const handleColumnChange = (index: number, field: keyof ColumnSpec, value: string): void => {
    const newColumns = [...columns]
    newColumns[index][field] = value
    setColumns(newColumns)
  }

  const handleAddFilter = (): void => {
    setFilters([...filters, { field: '', operator: 'eq', value: '' }])
  }

  const handleRemoveFilter = (index: number): void => {
    setFilters(filters.filter((_, i) => i !== index))
  }

  const handleFilterChange = (index: number, field: keyof FilterSpec, value: string): void => {
    const newFilters = [...filters]
    newFilters[index][field] = value
    setFilters(newFilters)
  }

  const handleAddOrder = (): void => {
    setOrderBy([...orderBy, { column: '', descending: false }])
  }

  const handleRemoveOrder = (index: number): void => {
    setOrderBy(orderBy.filter((_, i) => i !== index))
  }

  const handleSubmit = async (): Promise<void> => {
    if (!name.trim()) {
      setError('View name is required')
      return
    }

    if (!isEdit && !baseTable) {
      setError('Base table is required')
      return
    }

    const validColumns = columns.filter(c => c.alias.trim())
    if (validColumns.length === 0) {
      setError('At least one column is required')
      return
    }

    setIsSubmitting(true)
    setError(null)

    try {
      const columnSpecs: ViewColumnApiSpec[] = validColumns.map(c => ({
        source: c.source || undefined,
        expression: c.expression || undefined,
        alias: c.alias,
        aggregation: c.aggregation || undefined
      }))

      const filterSpecs: ViewFilterApiSpec[] | undefined = filters.length > 0
        ? filters.filter(f => f.field && f.operator).map(f => ({
            field: f.field,
            operator: f.operator,
            value: f.value,
            logicalOp: 'And' as const
          }))
        : undefined

      const orderSpecs: ViewOrderApiSpec[] | undefined = orderBy.length > 0
        ? orderBy.filter(o => o.column).map(o => ({
            column: o.column,
            descending: o.descending,
            nullOrdering: 'Last' as const
          }))
        : undefined

      if (isEdit) {
        const updateData: UpdateViewApiRequest = {
          name: name !== view?.name ? name : undefined,
          columns: columnSpecs,
          filters: filterSpecs,
          orderBy: orderSpecs,
          groupBy: groupBy.length > 0 ? groupBy : undefined,
          limit: limit ? parseInt(limit) : undefined,
          distinct,
          refreshPolicy: materialized ? refreshPolicy : undefined,
          refreshSchedule: materialized && refreshPolicy === 'Scheduled' ? refreshSchedule : undefined,
          description: description || undefined
        }
        await onSubmit(updateData)
      } else {
        const createData: CreateViewApiRequest = {
          name,
          baseTable,
          columns: columnSpecs,
          filters: filterSpecs,
          orderBy: orderSpecs,
          groupBy: groupBy.length > 0 ? groupBy : undefined,
          limit: limit ? parseInt(limit) : undefined,
          distinct,
          materialized,
          refreshPolicy: materialized ? refreshPolicy : undefined,
          refreshSchedule: materialized && refreshPolicy === 'Scheduled' ? refreshSchedule : undefined,
          description: description || undefined
        }
        await onSubmit(createData)
      }

      onOpenChange(false)
    } catch (err) {
      setError((err as Error).message)
    } finally {
      setIsSubmitting(false)
    }
  }

  if (!open) return null

  // Get columns from selected base table
  const selectedTable = tables?.find(t => t.name === baseTable)
  const tableColumns = selectedTable?.columns || []

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center">
      <div className="fixed inset-0 bg-black/50" onClick={() => onOpenChange(false)} />
      <div className="relative z-50 w-full max-w-2xl rounded-lg border bg-background p-6 shadow-lg max-h-[90vh] overflow-y-auto">
        <div className="flex items-center justify-between mb-4">
          <div className="flex items-center gap-2">
            <Eye className="h-5 w-5 text-primary" />
            <h2 className="text-lg font-semibold">
              {isEdit ? `Edit View: ${view?.name}` : 'Create View'}
            </h2>
          </div>
          <Button variant="ghost" size="icon" onClick={() => onOpenChange(false)}>
            <X className="h-4 w-4" />
          </Button>
        </div>

        <div className="space-y-4">
          {/* Basic Info */}
          <div className="grid grid-cols-2 gap-4">
            <div>
              <Label htmlFor="view-name">View Name</Label>
              <Input
                id="view-name"
                value={name}
                onChange={(e) => setName(e.target.value)}
                placeholder="my_view"
              />
            </div>
            <div>
              <Label htmlFor="base-table">Base Table</Label>
              <select
                id="base-table"
                value={baseTable}
                onChange={(e) => setBaseTable(e.target.value)}
                disabled={isEdit}
                className="w-full h-9 rounded-md border bg-background px-3 text-sm"
              >
                <option value="">Select a table...</option>
                {tables?.map((t) => (
                  <option key={t.id} value={t.name}>
                    {t.name}
                  </option>
                ))}
              </select>
            </div>
          </div>

          {/* Columns */}
          <div>
            <div className="flex items-center justify-between mb-2">
              <Label>Columns</Label>
              <Button variant="ghost" size="sm" onClick={handleAddColumn}>
                <Plus className="h-3 w-3 mr-1" />
                Add Column
              </Button>
            </div>
            <div className="space-y-2 max-h-48 overflow-y-auto">
              {columns.map((col, idx) => (
                <div key={idx} className="flex items-center gap-2 p-2 rounded bg-muted/30">
                  <select
                    value={col.source}
                    onChange={(e) => handleColumnChange(idx, 'source', e.target.value)}
                    className="flex-1 h-8 rounded border bg-background px-2 text-sm"
                  >
                    <option value="">Select column...</option>
                    {tableColumns.map((tc) => (
                      <option key={tc.id} value={tc.name}>
                        {tc.name} ({tc.dataType})
                      </option>
                    ))}
                  </select>
                  <Input
                    value={col.alias}
                    onChange={(e) => handleColumnChange(idx, 'alias', e.target.value)}
                    placeholder="Alias"
                    className="flex-1 h-8"
                  />
                  <select
                    value={col.aggregation}
                    onChange={(e) => handleColumnChange(idx, 'aggregation', e.target.value)}
                    className="w-24 h-8 rounded border bg-background px-2 text-sm"
                  >
                    <option value="">No agg</option>
                    <option value="count">COUNT</option>
                    <option value="sum">SUM</option>
                    <option value="avg">AVG</option>
                    <option value="min">MIN</option>
                    <option value="max">MAX</option>
                  </select>
                  <Button
                    variant="ghost"
                    size="icon"
                    className="h-8 w-8"
                    onClick={() => handleRemoveColumn(idx)}
                    disabled={columns.length <= 1}
                  >
                    <Trash2 className="h-3 w-3" />
                  </Button>
                </div>
              ))}
            </div>
          </div>

          {/* Filters */}
          <div>
            <div className="flex items-center justify-between mb-2">
              <Label>Filters (optional)</Label>
              <Button variant="ghost" size="sm" onClick={handleAddFilter}>
                <Plus className="h-3 w-3 mr-1" />
                Add Filter
              </Button>
            </div>
            {filters.length > 0 && (
              <div className="space-y-2 max-h-32 overflow-y-auto">
                {filters.map((f, idx) => (
                  <div key={idx} className="flex items-center gap-2 p-2 rounded bg-muted/30">
                    <select
                      value={f.field}
                      onChange={(e) => handleFilterChange(idx, 'field', e.target.value)}
                      className="flex-1 h-8 rounded border bg-background px-2 text-sm"
                    >
                      <option value="">Select column...</option>
                      {tableColumns.map((tc) => (
                        <option key={tc.id} value={tc.name}>
                          {tc.name}
                        </option>
                      ))}
                    </select>
                    <select
                      value={f.operator}
                      onChange={(e) => handleFilterChange(idx, 'operator', e.target.value)}
                      className="w-28 h-8 rounded border bg-background px-2 text-sm"
                    >
                      <option value="eq">=</option>
                      <option value="neq">!=</option>
                      <option value="gt">&gt;</option>
                      <option value="gte">&gt;=</option>
                      <option value="lt">&lt;</option>
                      <option value="lte">&lt;=</option>
                      <option value="like">LIKE</option>
                      <option value="contains">Contains</option>
                    </select>
                    <Input
                      value={f.value}
                      onChange={(e) => handleFilterChange(idx, 'value', e.target.value)}
                      placeholder="Value"
                      className="flex-1 h-8"
                    />
                    <Button
                      variant="ghost"
                      size="icon"
                      className="h-8 w-8"
                      onClick={() => handleRemoveFilter(idx)}
                    >
                      <Trash2 className="h-3 w-3" />
                    </Button>
                  </div>
                ))}
              </div>
            )}
          </div>

          {/* Order By */}
          <div>
            <div className="flex items-center justify-between mb-2">
              <Label>Order By (optional)</Label>
              <Button variant="ghost" size="sm" onClick={handleAddOrder}>
                <Plus className="h-3 w-3 mr-1" />
                Add Order
              </Button>
            </div>
            {orderBy.length > 0 && (
              <div className="space-y-2 max-h-24 overflow-y-auto">
                {orderBy.map((o, idx) => (
                  <div key={idx} className="flex items-center gap-2 p-2 rounded bg-muted/30">
                    <select
                      value={o.column}
                      onChange={(e) => {
                        const newOrder = [...orderBy]
                        newOrder[idx].column = e.target.value
                        setOrderBy(newOrder)
                      }}
                      className="flex-1 h-8 rounded border bg-background px-2 text-sm"
                    >
                      <option value="">Select column...</option>
                      {tableColumns.map((tc) => (
                        <option key={tc.id} value={tc.name}>
                          {tc.name}
                        </option>
                      ))}
                    </select>
                    <select
                      value={o.descending ? 'desc' : 'asc'}
                      onChange={(e) => {
                        const newOrder = [...orderBy]
                        newOrder[idx].descending = e.target.value === 'desc'
                        setOrderBy(newOrder)
                      }}
                      className="w-24 h-8 rounded border bg-background px-2 text-sm"
                    >
                      <option value="asc">ASC</option>
                      <option value="desc">DESC</option>
                    </select>
                    <Button
                      variant="ghost"
                      size="icon"
                      className="h-8 w-8"
                      onClick={() => handleRemoveOrder(idx)}
                    >
                      <Trash2 className="h-3 w-3" />
                    </Button>
                  </div>
                ))}
              </div>
            )}
          </div>

          {/* Options */}
          <div className="grid grid-cols-2 gap-4">
            <div>
              <Label htmlFor="limit">Row Limit (optional)</Label>
              <Input
                id="limit"
                type="number"
                value={limit}
                onChange={(e) => setLimit(e.target.value)}
                placeholder="No limit"
                min={1}
              />
            </div>
            <div className="flex items-end">
              <label className="flex items-center gap-2 text-sm">
                <input
                  type="checkbox"
                  checked={distinct}
                  onChange={(e) => setDistinct(e.target.checked)}
                  className="rounded"
                />
                Distinct rows only
              </label>
            </div>
          </div>

          {/* Materialized View Options */}
          <div className="p-3 rounded bg-muted/30">
            <label className="flex items-center gap-2 text-sm font-medium mb-3">
              <input
                type="checkbox"
                checked={materialized}
                onChange={(e) => setMaterialized(e.target.checked)}
                disabled={isEdit}
                className="rounded"
              />
              <Database className="h-4 w-4" />
              Materialized View
            </label>
            {materialized && (
              <div className="grid grid-cols-2 gap-3">
                <div>
                  <Label htmlFor="refresh-policy" className="text-xs">Refresh Policy</Label>
                  <select
                    id="refresh-policy"
                    value={refreshPolicy}
                    onChange={(e) => setRefreshPolicy(e.target.value)}
                    className="w-full h-8 rounded border bg-background px-2 text-sm"
                  >
                    <option value="OnDemand">On Demand</option>
                    <option value="Scheduled">Scheduled</option>
                    <option value="Incremental">Incremental</option>
                  </select>
                </div>
                {refreshPolicy === 'Scheduled' && (
                  <div>
                    <Label htmlFor="refresh-schedule" className="text-xs">Cron Schedule</Label>
                    <Input
                      id="refresh-schedule"
                      value={refreshSchedule}
                      onChange={(e) => setRefreshSchedule(e.target.value)}
                      placeholder="0 */6 * * *"
                      className="h-8"
                    />
                  </div>
                )}
              </div>
            )}
          </div>

          {/* Description */}
          <div>
            <Label htmlFor="description">Description (optional)</Label>
            <textarea
              id="description"
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              placeholder="What does this view show?"
              className="w-full rounded-md border bg-background px-3 py-2 text-sm min-h-[60px]"
            />
          </div>

          {/* Error */}
          {error && (
            <div className="p-3 rounded bg-destructive/10 text-destructive text-sm">
              {error}
            </div>
          )}

          {/* Actions */}
          <div className="flex justify-end gap-2 pt-2">
            <Button variant="outline" onClick={() => onOpenChange(false)}>
              Cancel
            </Button>
            <Button onClick={handleSubmit} disabled={isSubmitting}>
              {isSubmitting && <Loader2 className="h-4 w-4 mr-2 animate-spin" />}
              {isEdit ? 'Update View' : 'Create View'}
            </Button>
          </div>
        </div>
      </div>
    </div>
  )
}
