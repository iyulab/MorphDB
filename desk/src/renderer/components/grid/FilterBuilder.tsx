import { useState, type ReactElement } from 'react'
import { Plus, X, Search, Trash2 } from 'lucide-react'
import { Button } from '@/components/ui/Button'
import type { ColumnApiResponse } from '@/lib/api'
import { cn } from '@/lib/utils'

export interface FilterCondition {
  id: string
  column: string
  operator: string
  value: string
}

interface FilterBuilderProps {
  columns: ColumnApiResponse[]
  onApply: (filter: string | null) => void
  className?: string
}

const OPERATORS: { value: string; label: string; types: string[] }[] = [
  { value: 'eq', label: '=', types: ['all'] },
  { value: 'ne', label: '≠', types: ['all'] },
  { value: 'gt', label: '>', types: ['number', 'date'] },
  { value: 'ge', label: '≥', types: ['number', 'date'] },
  { value: 'lt', label: '<', types: ['number', 'date'] },
  { value: 'le', label: '≤', types: ['number', 'date'] },
  { value: 'contains', label: 'contains', types: ['string'] },
  { value: 'startswith', label: 'starts with', types: ['string'] },
  { value: 'endswith', label: 'ends with', types: ['string'] }
]

function getColumnType(dataType: string): 'string' | 'number' | 'date' | 'boolean' {
  const type = dataType.toLowerCase()
  if (type.includes('int') || type.includes('decimal') || type.includes('numeric') || type.includes('float') || type.includes('double')) {
    return 'number'
  }
  if (type.includes('date') || type.includes('time')) {
    return 'date'
  }
  if (type.includes('bool')) {
    return 'boolean'
  }
  return 'string'
}

function getAvailableOperators(dataType: string): typeof OPERATORS {
  const colType = getColumnType(dataType)
  return OPERATORS.filter(op =>
    op.types.includes('all') || op.types.includes(colType)
  )
}

function buildODataFilter(conditions: FilterCondition[], columns: ColumnApiResponse[]): string | null {
  if (conditions.length === 0) return null

  const parts = conditions.map(cond => {
    const col = columns.find(c => c.name === cond.column)
    if (!col) return null

    const colType = getColumnType(col.dataType)
    let value = cond.value

    // Handle null/empty value
    if (value === '' || value.toLowerCase() === 'null') {
      if (cond.operator === 'eq') {
        return `${cond.column} eq null`
      } else if (cond.operator === 'ne') {
        return `${cond.column} ne null`
      }
    }

    // Format value based on type and operator
    if (cond.operator === 'contains') {
      return `contains(${cond.column}, '${value}')`
    } else if (cond.operator === 'startswith') {
      return `startswith(${cond.column}, '${value}')`
    } else if (cond.operator === 'endswith') {
      return `endswith(${cond.column}, '${value}')`
    } else {
      // Standard comparison operators
      if (colType === 'string') {
        value = `'${value}'`
      } else if (colType === 'boolean') {
        value = value.toLowerCase() === 'true' ? 'true' : 'false'
      }
      return `${cond.column} ${cond.operator} ${value}`
    }
  }).filter(Boolean)

  return parts.length > 0 ? parts.join(' and ') : null
}

export function FilterBuilder({ columns, onApply, className }: FilterBuilderProps): ReactElement {
  const [isExpanded, setIsExpanded] = useState(false)
  const [conditions, setConditions] = useState<FilterCondition[]>([])

  const addCondition = (): void => {
    const firstColumn = columns[0]
    if (!firstColumn) return

    const operators = getAvailableOperators(firstColumn.dataType)
    setConditions(prev => [
      ...prev,
      {
        id: crypto.randomUUID(),
        column: firstColumn.name,
        operator: operators[0]?.value || 'eq',
        value: ''
      }
    ])
    setIsExpanded(true)
  }

  const updateCondition = (id: string, field: keyof FilterCondition, value: string): void => {
    setConditions(prev => prev.map(cond => {
      if (cond.id !== id) return cond

      const updated = { ...cond, [field]: value }

      // Reset operator when column changes
      if (field === 'column') {
        const col = columns.find(c => c.name === value)
        if (col) {
          const operators = getAvailableOperators(col.dataType)
          if (!operators.find(op => op.value === cond.operator)) {
            updated.operator = operators[0]?.value || 'eq'
          }
        }
      }

      return updated
    }))
  }

  const removeCondition = (id: string): void => {
    setConditions(prev => prev.filter(cond => cond.id !== id))
  }

  const handleApply = (): void => {
    const filter = buildODataFilter(conditions, columns)
    onApply(filter)
  }

  const handleClear = (): void => {
    setConditions([])
    onApply(null)
  }

  if (columns.length === 0) return <></>

  return (
    <div className={cn('border-b border-border', className)}>
      {/* Filter Toggle Bar */}
      <div className="flex items-center gap-2 px-3 py-2">
        <Button
          variant="ghost"
          size="sm"
          onClick={() => conditions.length > 0 ? setIsExpanded(!isExpanded) : addCondition()}
          className="h-7"
        >
          <Search className="h-3.5 w-3.5 mr-1" />
          Filter
          {conditions.length > 0 && (
            <span className="ml-1 rounded-full bg-primary text-primary-foreground text-[10px] px-1.5">
              {conditions.length}
            </span>
          )}
        </Button>

        {conditions.length > 0 && (
          <>
            <Button
              variant="outline"
              size="sm"
              onClick={handleApply}
              className="h-7"
            >
              Apply
            </Button>
            <Button
              variant="ghost"
              size="sm"
              onClick={handleClear}
              className="h-7 text-muted-foreground hover:text-destructive"
            >
              Clear
            </Button>
          </>
        )}
      </div>

      {/* Filter Conditions */}
      {isExpanded && conditions.length > 0 && (
        <div className="px-3 pb-3 space-y-2">
          {conditions.map((condition, index) => {
            const col = columns.find(c => c.name === condition.column)
            const operators = col ? getAvailableOperators(col.dataType) : OPERATORS.filter(op => op.types.includes('all'))

            return (
              <div key={condition.id} className="flex items-center gap-2 text-sm">
                {index > 0 && (
                  <span className="text-xs text-muted-foreground w-8">AND</span>
                )}
                {index === 0 && (
                  <span className="text-xs text-muted-foreground w-8">WHERE</span>
                )}

                {/* Column Select */}
                <select
                  value={condition.column}
                  onChange={(e) => updateCondition(condition.id, 'column', e.target.value)}
                  className="h-7 rounded border bg-background px-2 text-xs focus:outline-none focus:ring-1 focus:ring-primary"
                >
                  {columns.map(col => (
                    <option key={col.id} value={col.name}>
                      {col.displayName || col.name}
                    </option>
                  ))}
                </select>

                {/* Operator Select */}
                <select
                  value={condition.operator}
                  onChange={(e) => updateCondition(condition.id, 'operator', e.target.value)}
                  className="h-7 rounded border bg-background px-2 text-xs focus:outline-none focus:ring-1 focus:ring-primary min-w-[80px]"
                >
                  {operators.map(op => (
                    <option key={op.value} value={op.value}>
                      {op.label}
                    </option>
                  ))}
                </select>

                {/* Value Input */}
                <input
                  type="text"
                  value={condition.value}
                  onChange={(e) => updateCondition(condition.id, 'value', e.target.value)}
                  onKeyDown={(e) => {
                    if (e.key === 'Enter') handleApply()
                  }}
                  placeholder="value"
                  className="h-7 flex-1 rounded border bg-background px-2 text-xs focus:outline-none focus:ring-1 focus:ring-primary min-w-[100px]"
                />

                {/* Remove Button */}
                <Button
                  variant="ghost"
                  size="icon"
                  className="h-7 w-7 text-muted-foreground hover:text-destructive"
                  onClick={() => removeCondition(condition.id)}
                >
                  <X className="h-3.5 w-3.5" />
                </Button>
              </div>
            )
          })}

          {/* Add Condition Button */}
          <Button
            variant="ghost"
            size="sm"
            onClick={addCondition}
            className="h-7 text-xs"
          >
            <Plus className="h-3.5 w-3.5 mr-1" />
            Add Condition
          </Button>
        </div>
      )}
    </div>
  )
}
