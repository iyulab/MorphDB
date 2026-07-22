import { useState, type ReactElement } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import {
  Eye,
  Plus,
  Loader2,
  AlertCircle,
  RefreshCw,
  Database,
  Trash2,
  Pencil,
  Play,
  Clock,
  CheckCircle,
  AlertTriangle,
  MoreVertical
} from 'lucide-react'
import { Button } from '@/components/ui/Button'
import { ViewDialog } from '@/components/dialogs/ViewDialog'
import { DeleteConfirmationDialog } from '@/components/dialogs/DeleteConfirmationDialog'
import {
  MorphDBClient,
  type ViewApiResponse,
  type CreateViewApiRequest,
  type UpdateViewApiRequest,
  type ViewQueryApiResponse
} from '@/lib/api'
import { useConnectionStore } from '@/stores/connectionStore'
import { cn } from '@/lib/utils'

interface DialogState {
  createView: boolean
  editView: { open: boolean; view: ViewApiResponse | null }
  deleteView: { open: boolean; view: ViewApiResponse | null }
  viewData: { open: boolean; view: ViewApiResponse | null; data: ViewQueryApiResponse | null }
}

const initialDialogState: DialogState = {
  createView: false,
  editView: { open: false, view: null },
  deleteView: { open: false, view: null },
  viewData: { open: false, view: null, data: null }
}

export function ViewsPage(): ReactElement {
  const { activeConnection } = useConnectionStore()
  const queryClient = useQueryClient()

  const [dialogs, setDialogs] = useState<DialogState>(initialDialogState)
  const [typeFilter, setTypeFilter] = useState<'all' | 'standard' | 'materialized'>('all')
  const [menuOpen, setMenuOpen] = useState<string | null>(null)

  // Helper to create API client
  const createClient = async (): Promise<MorphDBClient | null> => {
    if (!activeConnection) return null
    return new MorphDBClient({
      url: activeConnection.url,
      projectId: activeConnection.projectId
    })
  }

  // Fetch views
  const {
    data: views,
    isLoading,
    error,
    refetch
  } = useQuery({
    queryKey: ['views', activeConnection?.id],
    queryFn: async () => {
      const client = await createClient()
      if (!client) return []
      return client.listViews()
    },
    enabled: !!activeConnection && activeConnection.status === 'connected'
  })

  // Create view mutation
  const createViewMutation = useMutation({
    mutationFn: async (data: CreateViewApiRequest) => {
      const client = await createClient()
      if (!client) throw new Error('No active connection')
      return client.createView(data)
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['views'] })
    }
  })

  // Update view mutation
  const updateViewMutation = useMutation({
    mutationFn: async ({ name, data }: { name: string; data: UpdateViewApiRequest }) => {
      const client = await createClient()
      if (!client) throw new Error('No active connection')
      return client.updateView(name, data)
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['views'] })
    }
  })

  // Delete view mutation
  const deleteViewMutation = useMutation({
    mutationFn: async (name: string) => {
      const client = await createClient()
      if (!client) throw new Error('No active connection')
      return client.deleteView(name)
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['views'] })
    }
  })

  // Refresh materialized view mutation
  const refreshViewMutation = useMutation({
    mutationFn: async ({ name, concurrent }: { name: string; concurrent: boolean }) => {
      const client = await createClient()
      if (!client) throw new Error('No active connection')
      return client.refreshMaterializedView(name, concurrent)
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['views'] })
    }
  })

  // Query view data mutation
  const queryViewMutation = useMutation({
    mutationFn: async (name: string) => {
      const client = await createClient()
      if (!client) throw new Error('No active connection')
      return client.queryViewData(name, { take: 100 })
    }
  })

  // Handlers
  const handleCreateView = async (data: CreateViewApiRequest | UpdateViewApiRequest): Promise<void> => {
    await createViewMutation.mutateAsync(data as CreateViewApiRequest)
  }

  const handleUpdateView = async (data: CreateViewApiRequest | UpdateViewApiRequest): Promise<void> => {
    if (!dialogs.editView.view) return
    await updateViewMutation.mutateAsync({
      name: dialogs.editView.view.name,
      data: data as UpdateViewApiRequest
    })
  }

  const handleDeleteView = async (): Promise<void> => {
    if (!dialogs.deleteView.view) return
    await deleteViewMutation.mutateAsync(dialogs.deleteView.view.name)
  }

  const handleRefreshView = async (view: ViewApiResponse): Promise<void> => {
    await refreshViewMutation.mutateAsync({ name: view.name, concurrent: false })
    setMenuOpen(null)
  }

  const handleQueryView = async (view: ViewApiResponse): Promise<void> => {
    try {
      const data = await queryViewMutation.mutateAsync(view.name)
      setDialogs(prev => ({
        ...prev,
        viewData: { open: true, view, data }
      }))
    } catch (err) {
      console.error('Failed to query view:', err)
    }
    setMenuOpen(null)
  }

  // Filter views
  const filteredViews = views?.filter(v => {
    if (typeFilter === 'all') return true
    if (typeFilter === 'materialized') return v.isMaterialized
    return !v.isMaterialized
  }) || []

  // Stats
  const stats = {
    total: views?.length || 0,
    standard: views?.filter(v => !v.isMaterialized).length || 0,
    materialized: views?.filter(v => v.isMaterialized).length || 0,
    stale: views?.filter(v => v.isMaterialized && v.isStale).length || 0
  }

  if (!activeConnection) {
    return (
      <div className="flex flex-col h-full bg-background items-center justify-center">
        <Eye className="h-16 w-16 text-muted-foreground/50 mb-4" />
        <h2 className="text-xl font-semibold">Views Management</h2>
        <p className="mt-2 text-muted-foreground">
          Select a connection to manage views
        </p>
      </div>
    )
  }

  if (activeConnection.status !== 'connected') {
    return (
      <div className="flex flex-col h-full bg-background items-center justify-center">
        <AlertCircle className="h-16 w-16 text-muted-foreground/50 mb-4" />
        <h2 className="text-xl font-semibold">Not Connected</h2>
        <p className="mt-2 text-muted-foreground">
          Connect to the server to manage views
        </p>
      </div>
    )
  }

  return (
    <div className="flex flex-col h-full bg-background">
      {/* Header */}
      <div className="flex h-10 items-center justify-between border-b border-border px-4">
        <div className="flex items-center">
          <Eye className="h-4 w-4 mr-2" />
          <span className="font-medium text-sm">Views</span>
          <span className="ml-2 text-xs text-muted-foreground">
            ({stats.total} total)
          </span>
        </div>
        <div className="flex items-center gap-2">
          <Button
            variant="ghost"
            size="icon"
            className="h-7 w-7"
            onClick={() => refetch()}
            disabled={isLoading}
          >
            <RefreshCw className={cn('h-4 w-4', isLoading && 'animate-spin')} />
          </Button>
          <Button
            variant="default"
            size="sm"
            className="gap-1"
            onClick={() => setDialogs(prev => ({ ...prev, createView: true }))}
          >
            <Plus className="h-4 w-4" />
            New View
          </Button>
        </div>
      </div>

      {/* Stats Bar */}
      <div className="flex items-center gap-4 px-4 py-2 border-b border-border bg-muted/30">
        <button
          onClick={() => setTypeFilter('all')}
          className={cn(
            'text-xs px-2 py-1 rounded',
            typeFilter === 'all' ? 'bg-primary text-primary-foreground' : 'hover:bg-accent'
          )}
        >
          All ({stats.total})
        </button>
        <button
          onClick={() => setTypeFilter('standard')}
          className={cn(
            'flex items-center gap-1 text-xs px-2 py-1 rounded',
            typeFilter === 'standard' ? 'bg-primary/20 text-primary' : 'hover:bg-accent'
          )}
        >
          <Eye className="h-3 w-3" />
          Standard ({stats.standard})
        </button>
        <button
          onClick={() => setTypeFilter('materialized')}
          className={cn(
            'flex items-center gap-1 text-xs px-2 py-1 rounded',
            typeFilter === 'materialized' ? 'bg-success/20 text-success' : 'hover:bg-accent'
          )}
        >
          <Database className="h-3 w-3" />
          Materialized ({stats.materialized})
        </button>
        {stats.stale > 0 && (
          <span className="flex items-center gap-1 text-xs text-warning">
            <AlertTriangle className="h-3 w-3" />
            {stats.stale} stale
          </span>
        )}
      </div>

      {/* Content */}
      <div className="flex-1 overflow-auto p-4">
        {isLoading ? (
          <div className="flex items-center justify-center h-full">
            <Loader2 className="h-8 w-8 animate-spin text-muted-foreground" />
          </div>
        ) : error ? (
          <div className="flex flex-col items-center justify-center h-full">
            <AlertCircle className="h-12 w-12 text-destructive mb-4" />
            <p className="text-destructive">{(error as Error).message}</p>
            <Button variant="outline" className="mt-4" onClick={() => refetch()}>
              Retry
            </Button>
          </div>
        ) : filteredViews.length === 0 ? (
          <div className="flex flex-col items-center justify-center h-full text-center">
            <Eye className="h-16 w-16 text-muted-foreground/50 mb-4" />
            <h2 className="text-xl font-semibold">
              {typeFilter === 'all' ? 'No Views Yet' : `No ${typeFilter} Views`}
            </h2>
            <p className="mt-2 text-muted-foreground max-w-md">
              {typeFilter === 'all'
                ? 'Create your first view to save queries and create virtual tables.'
                : `No views of type "${typeFilter}" found.`}
            </p>
            {typeFilter === 'all' && (
              <Button
                className="mt-6"
                onClick={() => setDialogs(prev => ({ ...prev, createView: true }))}
              >
                <Plus className="h-4 w-4 mr-2" />
                Create View
              </Button>
            )}
          </div>
        ) : (
          <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
            {filteredViews.map(view => (
              <div
                key={view.id}
                className="p-4 rounded-lg border bg-card hover:border-primary/50 transition-colors"
              >
                <div className="flex items-start justify-between mb-3">
                  <div className="flex items-center gap-2">
                    {view.isMaterialized ? (
                      <Database className="h-5 w-5 text-success" />
                    ) : (
                      <Eye className="h-5 w-5 text-primary" />
                    )}
                    <div>
                      <h3 className="font-medium">{view.name}</h3>
                      <p className="text-xs text-muted-foreground">
                        from {view.baseTable}
                      </p>
                    </div>
                  </div>
                  <div className="relative">
                    <Button
                      variant="ghost"
                      size="icon"
                      className="h-7 w-7"
                      onClick={() => setMenuOpen(menuOpen === view.id ? null : view.id)}
                    >
                      <MoreVertical className="h-4 w-4" />
                    </Button>
                    {menuOpen === view.id && (
                      <>
                        <div
                          className="fixed inset-0"
                          onClick={() => setMenuOpen(null)}
                        />
                        <div className="absolute right-0 top-full mt-1 z-50 min-w-[140px] rounded-md border bg-popover p-1 shadow-md">
                          <button
                            onClick={() => handleQueryView(view)}
                            className="w-full flex items-center gap-2 rounded px-2 py-1.5 text-sm hover:bg-accent text-left"
                          >
                            <Play className="h-4 w-4" />
                            Query Data
                          </button>
                          {view.isMaterialized && (
                            <button
                              onClick={() => handleRefreshView(view)}
                              className="w-full flex items-center gap-2 rounded px-2 py-1.5 text-sm hover:bg-accent text-left"
                              disabled={refreshViewMutation.isPending}
                            >
                              <RefreshCw className={cn('h-4 w-4', refreshViewMutation.isPending && 'animate-spin')} />
                              Refresh
                            </button>
                          )}
                          <button
                            onClick={() => {
                              setDialogs(prev => ({
                                ...prev,
                                editView: { open: true, view }
                              }))
                              setMenuOpen(null)
                            }}
                            className="w-full flex items-center gap-2 rounded px-2 py-1.5 text-sm hover:bg-accent text-left"
                          >
                            <Pencil className="h-4 w-4" />
                            Edit
                          </button>
                          <button
                            onClick={() => {
                              setDialogs(prev => ({
                                ...prev,
                                deleteView: { open: true, view }
                              }))
                              setMenuOpen(null)
                            }}
                            className="w-full flex items-center gap-2 rounded px-2 py-1.5 text-sm hover:bg-accent text-left text-destructive"
                          >
                            <Trash2 className="h-4 w-4" />
                            Delete
                          </button>
                        </div>
                      </>
                    )}
                  </div>
                </div>

                {/* View info */}
                <div className="space-y-2 text-sm">
                  <div className="flex items-center gap-2 text-muted-foreground">
                    <span>{view.columns.length} columns</span>
                    {view.distinct && <span className="text-xs">DISTINCT</span>}
                    {view.limit && <span className="text-xs">LIMIT {view.limit}</span>}
                  </div>

                  {view.isMaterialized && (
                    <div className="flex items-center gap-2">
                      {view.isStale ? (
                        <span className="flex items-center gap-1 text-xs text-warning">
                          <AlertTriangle className="h-3 w-3" />
                          Stale
                        </span>
                      ) : (
                        <span className="flex items-center gap-1 text-xs text-success">
                          <CheckCircle className="h-3 w-3" />
                          Fresh
                        </span>
                      )}
                      {view.lastRefreshedAt && (
                        <span className="flex items-center gap-1 text-xs text-muted-foreground">
                          <Clock className="h-3 w-3" />
                          {new Date(view.lastRefreshedAt).toLocaleDateString()}
                        </span>
                      )}
                    </div>
                  )}

                  {view.refreshPolicy && view.isMaterialized && (
                    <div className="text-xs text-muted-foreground">
                      Refresh: {view.refreshPolicy}
                      {view.refreshSchedule && ` (${view.refreshSchedule})`}
                    </div>
                  )}
                </div>

                <div className="mt-3 pt-3 border-t border-border text-xs text-muted-foreground">
                  Updated {new Date(view.updatedAt).toLocaleDateString()}
                </div>
              </div>
            ))}
          </div>
        )}
      </div>

      {/* Dialogs */}
      <ViewDialog
        open={dialogs.createView}
        onOpenChange={(open) => setDialogs(prev => ({ ...prev, createView: open }))}
        onSubmit={handleCreateView}
      />

      <ViewDialog
        open={dialogs.editView.open}
        onOpenChange={(open) => setDialogs(prev => ({
          ...prev,
          editView: { ...prev.editView, open }
        }))}
        view={dialogs.editView.view}
        onSubmit={handleUpdateView}
      />

      <DeleteConfirmationDialog
        open={dialogs.deleteView.open}
        onOpenChange={(open) => setDialogs(prev => ({
          ...prev,
          deleteView: { ...prev.deleteView, open }
        }))}
        title="Delete View"
        description="This will permanently delete the view. This action cannot be undone."
        itemName={dialogs.deleteView.view?.name || ''}
        onConfirm={handleDeleteView}
      />

      {/* View Data Dialog */}
      {dialogs.viewData.open && dialogs.viewData.view && dialogs.viewData.data && (
        <div className="fixed inset-0 z-50 flex items-center justify-center">
          <div
            className="fixed inset-0 bg-black/50"
            onClick={() => setDialogs(prev => ({
              ...prev,
              viewData: { open: false, view: null, data: null }
            }))}
          />
          <div className="relative z-50 w-full max-w-4xl rounded-lg border bg-background p-6 shadow-lg max-h-[90vh] overflow-hidden flex flex-col">
            <div className="flex items-center justify-between mb-4">
              <div className="flex items-center gap-2">
                <Play className="h-5 w-5 text-primary" />
                <h2 className="text-lg font-semibold">
                  View Data: {dialogs.viewData.view.name}
                </h2>
              </div>
              <span className="text-sm text-muted-foreground">
                {dialogs.viewData.data.totalCount} total rows
                {dialogs.viewData.data.hasMore && ' (showing first 100)'}
              </span>
            </div>

            <div className="flex-1 overflow-auto border rounded">
              <table className="w-full text-sm">
                <thead className="bg-muted sticky top-0">
                  <tr>
                    {dialogs.viewData.view.columns.map((col) => (
                      <th key={col.name} className="px-3 py-2 text-left font-medium border-b">
                        {col.name}
                        <span className="ml-1 text-xs text-muted-foreground">({col.dataType})</span>
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {dialogs.viewData.data.data.map((row, idx) => (
                    <tr key={idx} className="hover:bg-muted/50">
                      {dialogs.viewData.view!.columns.map((col) => (
                        <td key={col.name} className="px-3 py-2 border-b">
                          {row[col.name] !== null && row[col.name] !== undefined
                            ? String(row[col.name])
                            : <span className="text-muted-foreground italic">null</span>}
                        </td>
                      ))}
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            <div className="flex justify-end mt-4">
              <Button
                variant="outline"
                onClick={() => setDialogs(prev => ({
                  ...prev,
                  viewData: { open: false, view: null, data: null }
                }))}
              >
                Close
              </Button>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}
