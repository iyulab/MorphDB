import { useState, type ReactElement } from 'react'
import { useMutation } from '@tanstack/react-query'
import {
  Calculator,
  Plus,
  Trash2,
  Play,
  X,
  ChevronDown,
  ChevronUp,
  Loader2,
  AlertCircle
} from 'lucide-react'
import { Button } from '@/components/ui/Button'
import {
  MorphDBClient,
  type ColumnApiResponse,
  type AggregationFunction,
  type AggregationItem,
  type AggregationRequest,
  type AggregationResponse
} from '@/lib/api'
import { useConnectionStore } from '@/stores/connectionStore'
import { cn } from '@/lib/utils'

interface AggregationPanelProps {
  tableName: string
  columns: ColumnApiResponse[]
  className?: string
}

const AGGREGATION_FUNCTIONS: { value: AggregationFunction; label: string; requiresColumn: boolean }[] = [
  { value: 'count', label: 'COUNT', requiresColumn: false },
  { value: 'sum', label: 'SUM', requiresColumn: true },
  { value: 'avg', label: 'AVG', requiresColumn: true },
  { value: 'min', label: 'MIN', requiresColumn: true },
  { value: 'max', label: 'MAX', requiresColumn: true }
]

interface AggregationRow {
  id: string
  function: AggregationFunction
  column: string
  alias: string
}

export function AggregationPanel({
  tableName,
  columns,
  className
}: AggregationPanelProps): ReactElement {
  const { activeConnection, getApiKey } = useConnectionStore()

  const [isExpanded, setIsExpanded] = useState(false)
  const [aggregations, setAggregations] = useState<AggregationRow[]>([])
  const [groupByColumns, setGroupByColumns] = useState<string[]>([])
  const [limit, setLimit] = useState<number>(100)
  const [result, setResult] = useState<AggregationResponse | null>(null)

  // Numeric columns for SUM, AVG
  const numericColumns = columns.filter(c =>
    ['int', 'integer', 'bigint', 'decimal', 'numeric', 'float', 'double', 'real', 'money'].some(
      t => c.dataType.toLowerCase().includes(t)
    )
  )

  // All columns for MIN, MAX, GROUP BY
  const allColumns = columns.filter(c => !c.isPrimaryKey || c.name !== '_id')

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

  const aggregateMutation = useMutation({
    mutationFn: async (request: AggregationRequest) => {
      const client = await createClient()
      if (!client) throw new Error('No active connection')
      return client.aggregate(tableName, request)
    },
    onSuccess: (data) => {
      setResult(data)
    }
  })

  const addAggregation = (): void => {
    const newId = crypto.randomUUID()
    setAggregations(prev => [
      ...prev,
      {
        id: newId,
        function: 'count',
        column: '',
        alias: `agg_${prev.length + 1}`
      }
    ])
  }

  const removeAggregation = (id: string): void => {
    setAggregations(prev => prev.filter(a => a.id !== id))
  }

  const updateAggregation = (id: string, field: keyof AggregationRow, value: string): void => {
    setAggregations(prev =>
      prev.map(a => {
        if (a.id !== id) return a
        const updated = { ...a, [field]: value }
        // Auto-update alias when function or column changes
        if (field === 'function' || field === 'column') {
          const func = field === 'function' ? value : a.function
          const col = field === 'column' ? value : a.column
          if (col) {
            updated.alias = `${func}_${col}`
          } else {
            updated.alias = `${func}_all`
          }
        }
        return updated
      })
    )
  }

  const toggleGroupByColumn = (columnName: string): void => {
    setGroupByColumns(prev =>
      prev.includes(columnName)
        ? prev.filter(c => c !== columnName)
        : [...prev, columnName]
    )
  }

  const executeAggregation = async (): Promise<void> => {
    if (aggregations.length === 0) return

    const aggregationItems: AggregationItem[] = aggregations.map(a => ({
      function: a.function,
      column: a.column || undefined,
      alias: a.alias
    }))

    const request: AggregationRequest = {
      aggregations: aggregationItems,
      groupBy: groupByColumns.length > 0 ? groupByColumns : undefined,
      limit
    }

    await aggregateMutation.mutateAsync(request)
  }

  const clearAll = (): void => {
    setAggregations([])
    setGroupByColumns([])
    setResult(null)
  }

  const getColumnsForFunction = (func: AggregationFunction): ColumnApiResponse[] => {
    if (func === 'sum' || func === 'avg') {
      return numericColumns
    }
    return allColumns
  }

  if (!isExpanded) {
    return (
      <div className={cn('border-b border-border', className)}>
        <button
          onClick={() => setIsExpanded(true)}
          className="flex items-center gap-2 w-full px-4 py-2 text-sm hover:bg-accent text-left"
        >
          <Calculator className="h-4 w-4" />
          <span>Aggregation</span>
          <ChevronDown className="h-4 w-4 ml-auto" />
        </button>
      </div>
    )
  }

  return (
    <div className={cn('border-b border-border', className)}>
      {/* Header */}
      <div className="flex items-center justify-between px-4 py-2 bg-muted/30">
        <button
          onClick={() => setIsExpanded(false)}
          className="flex items-center gap-2 text-sm hover:text-primary"
        >
          <Calculator className="h-4 w-4" />
          <span className="font-medium">Aggregation</span>
          <ChevronUp className="h-4 w-4" />
        </button>
        <div className="flex items-center gap-2">
          {aggregations.length > 0 && (
            <Button
              variant="ghost"
              size="sm"
              onClick={clearAll}
              className="h-7 text-xs"
            >
              <X className="h-3 w-3 mr-1" />
              Clear
            </Button>
          )}
          <Button
            variant="default"
            size="sm"
            onClick={executeAggregation}
            disabled={aggregations.length === 0 || aggregateMutation.isPending}
            className="h-7 text-xs"
          >
            {aggregateMutation.isPending ? (
              <Loader2 className="h-3 w-3 mr-1 animate-spin" />
            ) : (
              <Play className="h-3 w-3 mr-1" />
            )}
            Execute
          </Button>
        </div>
      </div>

      {/* Aggregation Builder */}
      <div className="p-4 space-y-4">
        {/* Aggregations List */}
        <div className="space-y-2">
          <div className="flex items-center justify-between">
            <label className="text-xs font-medium text-muted-foreground">
              Aggregations
            </label>
            <Button
              variant="ghost"
              size="sm"
              onClick={addAggregation}
              className="h-6 text-xs"
            >
              <Plus className="h-3 w-3 mr-1" />
              Add
            </Button>
          </div>

          {aggregations.length === 0 ? (
            <p className="text-xs text-muted-foreground py-2">
              Click "Add" to add an aggregation function
            </p>
          ) : (
            <div className="space-y-2">
              {aggregations.map((agg) => {
                const funcDef = AGGREGATION_FUNCTIONS.find(f => f.value === agg.function)
                const availableColumns = getColumnsForFunction(agg.function)

                return (
                  <div
                    key={agg.id}
                    className="flex items-center gap-2 p-2 rounded bg-muted/50"
                  >
                    {/* Function Select */}
                    <select
                      value={agg.function}
                      onChange={(e) => updateAggregation(agg.id, 'function', e.target.value)}
                      className="h-7 rounded border bg-background px-2 text-xs"
                    >
                      {AGGREGATION_FUNCTIONS.map((f) => (
                        <option key={f.value} value={f.value}>
                          {f.label}
                        </option>
                      ))}
                    </select>

                    {/* Column Select (if required) */}
                    {funcDef?.requiresColumn ? (
                      <select
                        value={agg.column}
                        onChange={(e) => updateAggregation(agg.id, 'column', e.target.value)}
                        className="h-7 rounded border bg-background px-2 text-xs flex-1"
                      >
                        <option value="">Select column...</option>
                        {availableColumns.map((col) => (
                          <option key={col.name} value={col.name}>
                            {col.displayName || col.name}
                          </option>
                        ))}
                      </select>
                    ) : (
                      <span className="text-xs text-muted-foreground flex-1">
                        (all rows)
                      </span>
                    )}

                    {/* Alias */}
                    <span className="text-xs text-muted-foreground">as</span>
                    <input
                      type="text"
                      value={agg.alias}
                      onChange={(e) => updateAggregation(agg.id, 'alias', e.target.value)}
                      className="h-7 w-32 rounded border bg-background px-2 text-xs"
                      placeholder="alias"
                    />

                    {/* Remove */}
                    <Button
                      variant="ghost"
                      size="icon"
                      onClick={() => removeAggregation(agg.id)}
                      className="h-7 w-7"
                    >
                      <Trash2 className="h-3 w-3 text-destructive" />
                    </Button>
                  </div>
                )
              })}
            </div>
          )}
        </div>

        {/* Group By */}
        <div className="space-y-2">
          <label className="text-xs font-medium text-muted-foreground">
            Group By
          </label>
          <div className="flex flex-wrap gap-2">
            {allColumns.map((col) => (
              <button
                key={col.name}
                onClick={() => toggleGroupByColumn(col.name)}
                className={cn(
                  'px-2 py-1 rounded text-xs border transition-colors',
                  groupByColumns.includes(col.name)
                    ? 'bg-primary text-primary-foreground border-primary'
                    : 'bg-background border-border hover:border-primary'
                )}
              >
                {col.displayName || col.name}
              </button>
            ))}
          </div>
        </div>

        {/* Limit */}
        <div className="flex items-center gap-2">
          <label className="text-xs font-medium text-muted-foreground">
            Limit:
          </label>
          <input
            type="number"
            value={limit}
            onChange={(e) => setLimit(parseInt(e.target.value) || 100)}
            min={1}
            max={10000}
            className="h-7 w-24 rounded border bg-background px-2 text-xs"
          />
        </div>

        {/* Error */}
        {aggregateMutation.isError && (
          <div className="flex items-center gap-2 p-2 rounded bg-destructive/10 text-destructive text-xs">
            <AlertCircle className="h-4 w-4" />
            {(aggregateMutation.error as Error).message}
          </div>
        )}

        {/* Results */}
        {result && (
          <div className="space-y-2">
            <div className="flex items-center justify-between">
              <label className="text-xs font-medium text-muted-foreground">
                Results ({result.data.length} rows)
              </label>
              <span className="text-xs text-muted-foreground">
                Executed at {new Date(result.metadata.executedAt).toLocaleTimeString()}
              </span>
            </div>
            <div className="border rounded overflow-auto max-h-64">
              <table className="w-full text-xs">
                <thead className="bg-muted/50 sticky top-0">
                  <tr>
                    {result.data.length > 0 &&
                      Object.keys(result.data[0]).map((key) => (
                        <th
                          key={key}
                          className="px-3 py-2 text-left font-medium border-b"
                        >
                          {key}
                        </th>
                      ))}
                  </tr>
                </thead>
                <tbody>
                  {result.data.map((row, idx) => (
                    <tr key={idx} className="hover:bg-muted/30">
                      {Object.values(row).map((value, vidx) => (
                        <td key={vidx} className="px-3 py-2 border-b">
                          {value === null ? (
                            <span className="text-muted-foreground italic">null</span>
                          ) : typeof value === 'number' ? (
                            value.toLocaleString()
                          ) : (
                            String(value)
                          )}
                        </td>
                      ))}
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>
        )}
      </div>
    </div>
  )
}
