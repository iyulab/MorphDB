import { useState, useMemo, type ReactElement } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { AlertCircle, Database } from 'lucide-react'
import { Button } from '@/components/ui/Button'
import { DataGrid } from './DataGrid'
import { FilterBuilder } from './FilterBuilder'
import { AggregationPanel } from './AggregationPanel'
import { AddRecordDialog } from '@/components/dialogs/AddRecordDialog'
import { MorphDBClient, type TableApiResponse } from '@/lib/api'
import { useConnectionStore } from '@/stores/connectionStore'

interface TableViewProps {
  tableName: string
}

const PAGE_SIZE = 50

export function TableView({ tableName }: TableViewProps): ReactElement {
  const { activeConnection, getApiKey } = useConnectionStore()
  const queryClient = useQueryClient()

  const [currentPage, setCurrentPage] = useState(1)
  const [sortBy, setSortBy] = useState<string | null>(null)
  const [sortOrder, setSortOrder] = useState<'asc' | 'desc'>('asc')
  const [filter, setFilter] = useState<string | null>(null)
  const [addRecordOpen, setAddRecordOpen] = useState(false)

  // Helper to create API client
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

  // Fetch table schema
  const {
    data: tableSchema,
    isLoading: isLoadingSchema,
    error: schemaError
  } = useQuery({
    queryKey: ['table-schema', activeConnection?.id, tableName],
    queryFn: async () => {
      const client = await createClient()
      if (!client) throw new Error('No active connection')
      return client.getTable(tableName, activeConnection?.tenantId)
    },
    enabled: !!activeConnection && !!tableName
  })

  // Fetch table data
  const {
    data: tableData,
    isLoading: isLoadingData,
    error: dataError,
    refetch: refetchData
  } = useQuery({
    queryKey: ['table-data', activeConnection?.id, tableName, currentPage, sortBy, sortOrder, filter],
    queryFn: async () => {
      const client = await createClient()
      if (!client) throw new Error('No active connection')

      const options: {
        $top: number
        $skip: number
        $count: boolean
        $orderby?: string
        $filter?: string
      } = {
        $top: PAGE_SIZE,
        $skip: (currentPage - 1) * PAGE_SIZE,
        $count: true
      }

      if (sortBy) {
        options.$orderby = `${sortBy} ${sortOrder}`
      }

      if (filter) {
        options.$filter = filter
      }

      return client.queryData(tableName, options)
    },
    enabled: !!activeConnection && !!tableName && !!tableSchema
  })

  // Find primary key column
  const primaryKeyColumn = useMemo(() => {
    return tableSchema?.columns.find((c) => c.isPrimaryKey)
  }, [tableSchema])

  // Create record mutation
  const createRecordMutation = useMutation({
    mutationFn: async (data: Record<string, unknown>) => {
      const client = await createClient()
      if (!client) throw new Error('No active connection')
      return client.createRecord(tableName, data)
    },
    onSuccess: () => {
      queryClient.invalidateQueries({
        queryKey: ['table-data', activeConnection?.id, tableName]
      })
    }
  })

  // Update record mutation
  const updateRecordMutation = useMutation({
    mutationFn: async ({
      key,
      data
    }: {
      key: string
      data: Record<string, unknown>
    }) => {
      const client = await createClient()
      if (!client) throw new Error('No active connection')
      await client.updateRecord(tableName, key, data)
    },
    onSuccess: () => {
      queryClient.invalidateQueries({
        queryKey: ['table-data', activeConnection?.id, tableName]
      })
    }
  })

  // Delete record mutation
  const deleteRecordMutation = useMutation({
    mutationFn: async (key: string) => {
      const client = await createClient()
      if (!client) throw new Error('No active connection')
      await client.deleteRecord(tableName, key)
    },
    onSuccess: () => {
      queryClient.invalidateQueries({
        queryKey: ['table-data', activeConnection?.id, tableName]
      })
    }
  })

  // Handlers
  const handleAddRecord = async (data: Record<string, unknown>): Promise<void> => {
    await createRecordMutation.mutateAsync(data)
  }

  const handleUpdateRecord = async (
    rowIndex: number,
    columnId: string,
    value: unknown
  ): Promise<void> => {
    if (!primaryKeyColumn || !tableData?.value) return

    const row = tableData.value[rowIndex]
    const key = String(row[primaryKeyColumn.name])

    await updateRecordMutation.mutateAsync({
      key,
      data: { [columnId]: value }
    })
  }

  const handleDeleteRecord = async (rowIndex: number): Promise<void> => {
    if (!primaryKeyColumn || !tableData?.value) return

    const row = tableData.value[rowIndex]
    const key = String(row[primaryKeyColumn.name])

    await deleteRecordMutation.mutateAsync(key)
  }

  const handleSortChange = (newSortBy: string | null, newSortOrder: 'asc' | 'desc'): void => {
    setSortBy(newSortBy)
    setSortOrder(newSortOrder)
    setCurrentPage(1) // Reset to first page when sorting changes
  }

  const handlePageChange = (page: number): void => {
    setCurrentPage(page)
  }

  const handleFilterChange = (newFilter: string | null): void => {
    setFilter(newFilter)
    setCurrentPage(1) // Reset to first page when filter changes
  }

  // Error state
  if (schemaError || dataError) {
    return (
      <div className="flex h-full flex-col items-center justify-center p-4 text-center">
        <AlertCircle className="h-8 w-8 text-destructive mb-2" />
        <p className="text-sm text-destructive">
          {(schemaError as Error)?.message || (dataError as Error)?.message}
        </p>
        <Button variant="outline" size="sm" className="mt-4" onClick={() => refetchData()}>
          Retry
        </Button>
      </div>
    )
  }

  return (
    <div className="h-full flex flex-col">
      {/* Header */}
      <div className="flex items-center justify-between border-b border-border px-4 py-3">
        <div className="flex items-center gap-2">
          <Database className="h-5 w-5 text-primary" />
          <h2 className="text-lg font-semibold">{tableName}</h2>
          {tableSchema && (
            <span className="text-xs text-muted-foreground">
              ({tableSchema.columns.length} columns)
            </span>
          )}
        </div>
      </div>

      {/* Filter Builder */}
      {tableSchema && (
        <FilterBuilder
          columns={tableSchema.columns}
          onApply={handleFilterChange}
        />
      )}

      {/* Aggregation Panel */}
      {tableSchema && (
        <AggregationPanel
          tableName={tableName}
          columns={tableSchema.columns}
        />
      )}

      {/* Data Grid */}
      <div className="flex-1 overflow-hidden">
        <DataGrid
          columns={tableSchema?.columns || []}
          data={tableData?.value || []}
          isLoading={isLoadingSchema || isLoadingData}
          onAddRecord={() => setAddRecordOpen(true)}
          onUpdateRecord={primaryKeyColumn ? handleUpdateRecord : undefined}
          onDeleteRecord={primaryKeyColumn ? handleDeleteRecord : undefined}
          pageSize={PAGE_SIZE}
          totalCount={tableData?.['@odata.count'] || 0}
          currentPage={currentPage}
          onPageChange={handlePageChange}
          onSortChange={handleSortChange}
        />
      </div>

      {/* Add Record Dialog */}
      {tableSchema && (
        <AddRecordDialog
          open={addRecordOpen}
          onOpenChange={setAddRecordOpen}
          tableName={tableName}
          columns={tableSchema.columns}
          onSubmit={handleAddRecord}
        />
      )}
    </div>
  )
}
