import { useState, type ReactElement } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import {
  Webhook,
  Plus,
  MoreVertical,
  Pencil,
  Trash2,
  Power,
  PowerOff,
  Bell,
  AlertTriangle,
  History,
  RotateCcw,
  Archive,
  CheckCircle,
  XCircle,
  Clock,
  Loader2
} from 'lucide-react'
import { Button } from '@/components/ui/Button'
import { useConnectionStore } from '@/stores/connectionStore'
import { cn } from '@/lib/utils'
import { WebhookDialog } from '@/components/dialogs/WebhookDialog'
import {
  MorphDBClient,
  type WebhookApiResponse,
  type CreateWebhookApiRequest,
  type UpdateWebhookApiRequest,
  type WebhookDeliveryApiResponse,
  type DlqMessageApiResponse,
  type DlqStatisticsApiResponse
} from '@/lib/api'

type TabType = 'webhooks' | 'dlq'

export function WebhooksPage(): ReactElement {
  const { activeConnection } = useConnectionStore()
  const queryClient = useQueryClient()

  // Helper to create API client
  const createClient = async (): Promise<MorphDBClient | null> => {
    if (!activeConnection) return null
    return new MorphDBClient({
      url: activeConnection.url,
      projectId: activeConnection.projectId
    })
  }

  const [activeTab, setActiveTab] = useState<TabType>('webhooks')
  const [dialogOpen, setDialogOpen] = useState(false)
  const [editingWebhook, setEditingWebhook] = useState<WebhookApiResponse | null>(null)
  const [selectedWebhookId, setSelectedWebhookId] = useState<string | null>(null)
  const [showDeliveries, setShowDeliveries] = useState(false)
  const [confirmDelete, setConfirmDelete] = useState<string | null>(null)
  const [contextMenu, setContextMenu] = useState<{ id: string; x: number; y: number } | null>(null)
  const [dlqFilter, setDlqFilter] = useState<string>('pending')

  // Fetch webhooks
  const { data: webhooks = [], isLoading: webhooksLoading } = useQuery<WebhookApiResponse[]>({
    queryKey: ['webhooks', activeConnection?.id],
    queryFn: async () => {
      const client = await createClient()
      if (!client) return []
      return client.listWebhooks()
    },
    enabled: !!activeConnection
  })

  // Fetch DLQ statistics
  const { data: dlqStats } = useQuery<DlqStatisticsApiResponse | undefined>({
    queryKey: ['dlq-stats', activeConnection?.id],
    queryFn: async () => {
      const client = await createClient()
      if (!client) return undefined
      return client.getDlqStatistics()
    },
    enabled: !!activeConnection
  })

  // Fetch DLQ messages
  const { data: dlqMessages = [], isLoading: dlqLoading } = useQuery<DlqMessageApiResponse[]>({
    queryKey: ['dlq-messages', activeConnection?.id, dlqFilter],
    queryFn: async () => {
      const client = await createClient()
      if (!client) return []
      return client.listDlqMessages({ status: dlqFilter !== 'all' ? dlqFilter : undefined })
    },
    enabled: !!activeConnection && activeTab === 'dlq'
  })

  // Fetch deliveries for selected webhook
  const { data: deliveries = [], isLoading: deliveriesLoading } = useQuery<WebhookDeliveryApiResponse[]>({
    queryKey: ['webhook-deliveries', selectedWebhookId],
    queryFn: async () => {
      const client = await createClient()
      if (!client || !selectedWebhookId) return []
      return client.listWebhookDeliveries(selectedWebhookId)
    },
    enabled: !!activeConnection && !!selectedWebhookId && showDeliveries
  })

  // Create webhook mutation
  const createMutation = useMutation({
    mutationFn: async (data: CreateWebhookApiRequest) => {
      const client = await createClient()
      if (!client) throw new Error('No active connection')
      return client.createWebhook(data)
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['webhooks'] })
    }
  })

  // Update webhook mutation
  const updateMutation = useMutation({
    mutationFn: async ({ id, data }: { id: string; data: UpdateWebhookApiRequest }) => {
      const client = await createClient()
      if (!client) throw new Error('No active connection')
      return client.updateWebhook(id, data)
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['webhooks'] })
    }
  })

  // Delete webhook mutation
  const deleteMutation = useMutation({
    mutationFn: async (id: string) => {
      const client = await createClient()
      if (!client) throw new Error('No active connection')
      return client.deleteWebhook(id)
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['webhooks'] })
      setConfirmDelete(null)
    }
  })

  // Toggle webhook active state
  const toggleActiveMutation = useMutation({
    mutationFn: async ({ id, isActive }: { id: string; isActive: boolean }) => {
      const client = await createClient()
      if (!client) throw new Error('No active connection')
      return client.updateWebhook(id, { isActive })
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['webhooks'] })
    }
  })

  // Replay DLQ message mutation
  const replayMutation = useMutation({
    mutationFn: async (dlqId: string) => {
      const client = await createClient()
      if (!client) throw new Error('No active connection')
      return client.replayDlqMessage(dlqId)
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['dlq-messages'] })
      queryClient.invalidateQueries({ queryKey: ['dlq-stats'] })
    }
  })

  // Resolve DLQ message mutation
  const resolveMutation = useMutation({
    mutationFn: async ({ dlqId, notes }: { dlqId: string; notes: string }) => {
      const client = await createClient()
      if (!client) throw new Error('No active connection')
      return client.resolveDlqMessage(dlqId, { resolutionNotes: notes })
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['dlq-messages'] })
      queryClient.invalidateQueries({ queryKey: ['dlq-stats'] })
    }
  })

  // Archive DLQ messages mutation
  const archiveMutation = useMutation({
    mutationFn: async (olderThanDays?: number) => {
      const client = await createClient()
      if (!client) throw new Error('No active connection')
      return client.archiveDlqMessages({ olderThanDays })
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['dlq-messages'] })
      queryClient.invalidateQueries({ queryKey: ['dlq-stats'] })
    }
  })

  const handleCreate = (): void => {
    setEditingWebhook(null)
    setDialogOpen(true)
  }

  const handleEdit = (webhook: WebhookApiResponse): void => {
    setEditingWebhook(webhook)
    setDialogOpen(true)
    setContextMenu(null)
  }

  const handleSubmit = async (
    data: CreateWebhookApiRequest | UpdateWebhookApiRequest
  ): Promise<void> => {
    if (editingWebhook) {
      await updateMutation.mutateAsync({ id: editingWebhook.id, data: data as UpdateWebhookApiRequest })
    } else {
      await createMutation.mutateAsync(data as CreateWebhookApiRequest)
    }
  }

  const handleDelete = async (id: string): Promise<void> => {
    if (confirmDelete !== id) {
      setConfirmDelete(id)
      return
    }
    await deleteMutation.mutateAsync(id)
    setContextMenu(null)
  }

  const handleToggleActive = async (webhook: WebhookApiResponse): Promise<void> => {
    await toggleActiveMutation.mutateAsync({ id: webhook.id, isActive: !webhook.isActive })
    setContextMenu(null)
  }

  const handleShowDeliveries = (webhookId: string): void => {
    setSelectedWebhookId(webhookId)
    setShowDeliveries(true)
    setContextMenu(null)
  }

  const getWebhookName = (webhookId: string): string => {
    const webhook = webhooks.find((w) => w.id === webhookId)
    return webhook?.name || webhookId
  }

  const getStatusIcon = (status: string): ReactElement => {
    switch (status) {
      case 'delivered':
        return <CheckCircle className="h-4 w-4 text-success" />
      case 'failed':
        return <XCircle className="h-4 w-4 text-destructive" />
      case 'pending':
        return <Clock className="h-4 w-4 text-warning" />
      default:
        return <Clock className="h-4 w-4 text-muted-foreground" />
    }
  }

  if (!activeConnection) {
    return (
      <div className="flex h-full items-center justify-center">
        <div className="text-center text-muted-foreground">
          <Webhook className="h-12 w-12 mx-auto mb-4 opacity-50" />
          <p>Select a connection to manage webhooks</p>
        </div>
      </div>
    )
  }

  return (
    <div className="h-full flex flex-col">
      {/* Header */}
      <div className="flex items-center justify-between p-4 border-b">
        <div className="flex items-center gap-4">
          <h1 className="text-xl font-semibold">Webhooks</h1>
          {/* Tabs */}
          <div className="flex gap-1 p-1 bg-muted rounded-lg">
            <button
              onClick={() => setActiveTab('webhooks')}
              className={cn(
                'px-3 py-1.5 text-sm rounded-md transition-colors',
                activeTab === 'webhooks'
                  ? 'bg-background shadow-sm'
                  : 'text-muted-foreground hover:text-foreground'
              )}
            >
              <div className="flex items-center gap-2">
                <Bell className="h-4 w-4" />
                Subscriptions
                <span className="text-xs bg-muted-foreground/20 px-1.5 py-0.5 rounded">
                  {webhooks.length}
                </span>
              </div>
            </button>
            <button
              onClick={() => setActiveTab('dlq')}
              className={cn(
                'px-3 py-1.5 text-sm rounded-md transition-colors',
                activeTab === 'dlq'
                  ? 'bg-background shadow-sm'
                  : 'text-muted-foreground hover:text-foreground'
              )}
            >
              <div className="flex items-center gap-2">
                <AlertTriangle className="h-4 w-4" />
                Dead Letter Queue
                {dlqStats && dlqStats.pendingReviewCount > 0 && (
                  <span className="text-xs bg-destructive text-destructive-foreground px-1.5 py-0.5 rounded">
                    {dlqStats.pendingReviewCount}
                  </span>
                )}
              </div>
            </button>
          </div>
        </div>

        {activeTab === 'webhooks' && (
          <Button onClick={handleCreate}>
            <Plus className="h-4 w-4 mr-2" />
            New Webhook
          </Button>
        )}

        {activeTab === 'dlq' && dlqStats && dlqStats.pendingReviewCount > 0 && (
          <Button variant="outline" onClick={() => archiveMutation.mutate(30)}>
            <Archive className="h-4 w-4 mr-2" />
            Archive Old Messages
          </Button>
        )}
      </div>

      {/* Content */}
      <div className="flex-1 overflow-auto p-4">
        {activeTab === 'webhooks' && (
          <>
            {webhooksLoading ? (
              <div className="flex items-center justify-center h-full">
                <Loader2 className="h-8 w-8 animate-spin text-muted-foreground" />
              </div>
            ) : webhooks.length === 0 ? (
              <div className="flex flex-col items-center justify-center h-full text-center">
                <Webhook className="h-12 w-12 text-muted-foreground mb-4" />
                <h3 className="text-lg font-medium mb-2">No webhooks yet</h3>
                <p className="text-muted-foreground mb-4">
                  Create a webhook to receive real-time notifications when data changes.
                </p>
                <Button onClick={handleCreate}>
                  <Plus className="h-4 w-4 mr-2" />
                  Create Webhook
                </Button>
              </div>
            ) : (
              <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-3">
                {webhooks.map((webhook) => (
                  <div
                    key={webhook.id}
                    className={cn(
                      'rounded-lg border p-4 hover:shadow-md transition-shadow',
                      !webhook.isActive && 'opacity-60'
                    )}
                  >
                    <div className="flex items-start justify-between mb-3">
                      <div className="flex items-center gap-2">
                        <div
                          className={cn(
                            'h-2 w-2 rounded-full',
                            webhook.isActive ? 'bg-success' : 'bg-muted-foreground'
                          )}
                        />
                        <h3 className="font-medium truncate">{webhook.name}</h3>
                      </div>
                      <Button
                        variant="ghost"
                        size="icon"
                        className="h-8 w-8"
                        onClick={(e) => {
                          e.stopPropagation()
                          setContextMenu({
                            id: webhook.id,
                            x: e.clientX,
                            y: e.clientY
                          })
                        }}
                      >
                        <MoreVertical className="h-4 w-4" />
                      </Button>
                    </div>

                    <div className="space-y-2 text-sm">
                      <div className="flex items-center gap-2 text-muted-foreground">
                        <span className="font-medium">Table:</span>
                        <span className="truncate">{webhook.table}</span>
                      </div>
                      <div className="flex items-center gap-2 text-muted-foreground">
                        <span className="font-medium">URL:</span>
                        <span className="truncate text-xs">{webhook.url}</span>
                      </div>
                      <div className="flex flex-wrap gap-1 mt-2">
                        {webhook.events.map((event) => (
                          <span
                            key={event}
                            className="text-xs bg-muted px-2 py-0.5 rounded capitalize"
                          >
                            {event}
                          </span>
                        ))}
                      </div>
                    </div>
                  </div>
                ))}
              </div>
            )}
          </>
        )}

        {activeTab === 'dlq' && (
          <>
            {/* DLQ Stats */}
            {dlqStats && (
              <div className="grid gap-4 md:grid-cols-4 mb-6">
                <div className="rounded-lg border p-4">
                  <div className="text-2xl font-bold">{dlqStats.totalMessages}</div>
                  <div className="text-sm text-muted-foreground">Total Messages</div>
                </div>
                <div className="rounded-lg border p-4">
                  <div className="text-2xl font-bold text-destructive">
                    {dlqStats.pendingReviewCount}
                  </div>
                  <div className="text-sm text-muted-foreground">Pending Review</div>
                </div>
                <div className="rounded-lg border p-4">
                  <div className="text-2xl font-bold text-success">{dlqStats.resolvedCount}</div>
                  <div className="text-sm text-muted-foreground">Resolved</div>
                </div>
                <div className="rounded-lg border p-4">
                  <div className="text-2xl font-bold">{dlqStats.archivedCount}</div>
                  <div className="text-sm text-muted-foreground">Archived</div>
                </div>
              </div>
            )}

            {/* DLQ Filter */}
            <div className="flex gap-2 mb-4">
              {['all', 'pending', 'resolved', 'archived'].map((filter) => (
                <button
                  key={filter}
                  onClick={() => setDlqFilter(filter)}
                  className={cn(
                    'px-3 py-1.5 text-sm rounded-md border transition-colors capitalize',
                    dlqFilter === filter
                      ? 'bg-primary text-primary-foreground border-primary'
                      : 'bg-background border-input hover:bg-muted'
                  )}
                >
                  {filter}
                </button>
              ))}
            </div>

            {/* DLQ Messages */}
            {dlqLoading ? (
              <div className="flex items-center justify-center py-8">
                <Loader2 className="h-8 w-8 animate-spin text-muted-foreground" />
              </div>
            ) : dlqMessages.length === 0 ? (
              <div className="text-center py-8 text-muted-foreground">
                <AlertTriangle className="h-12 w-12 mx-auto mb-4 opacity-50" />
                <p>No messages in dead letter queue</p>
              </div>
            ) : (
              <div className="space-y-3">
                {dlqMessages.map((msg) => (
                  <div key={msg.dlqId} className="rounded-lg border p-4">
                    <div className="flex items-start justify-between">
                      <div className="space-y-1">
                        <div className="flex items-center gap-2">
                          {getStatusIcon(msg.status)}
                          <span className="font-medium">{getWebhookName(msg.webhookId)}</span>
                          <span className="text-xs bg-muted px-2 py-0.5 rounded capitalize">
                            {msg.event}
                          </span>
                        </div>
                        <div className="text-sm text-muted-foreground">
                          Reason: {msg.reason}
                        </div>
                        {msg.lastErrorMessage && (
                          <div className="text-sm text-destructive">
                            Error: {msg.lastErrorMessage}
                          </div>
                        )}
                        <div className="text-xs text-muted-foreground">
                          Attempts: {msg.attemptCount} | DLQ at:{' '}
                          {new Date(msg.dlqAt).toLocaleString()}
                        </div>
                      </div>

                      {msg.status === 'pending' && (
                        <div className="flex gap-2">
                          <Button
                            variant="outline"
                            size="sm"
                            onClick={() => replayMutation.mutate(msg.dlqId)}
                            disabled={replayMutation.isPending}
                          >
                            <RotateCcw className="h-4 w-4 mr-1" />
                            Retry
                          </Button>
                          <Button
                            variant="outline"
                            size="sm"
                            onClick={() =>
                              resolveMutation.mutate({
                                dlqId: msg.dlqId,
                                notes: 'Manually resolved'
                              })
                            }
                            disabled={resolveMutation.isPending}
                          >
                            <CheckCircle className="h-4 w-4 mr-1" />
                            Resolve
                          </Button>
                        </div>
                      )}
                    </div>
                  </div>
                ))}
              </div>
            )}
          </>
        )}
      </div>

      {/* Context Menu */}
      {contextMenu && (
        <>
          <div className="fixed inset-0 z-40" onClick={() => setContextMenu(null)} />
          <div
            className="fixed z-50 min-w-[160px] rounded-md border bg-popover p-1 shadow-md"
            style={{
              left: Math.min(contextMenu.x, window.innerWidth - 180),
              top: Math.min(contextMenu.y, window.innerHeight - 200)
            }}
          >
            <button
              onClick={() => handleShowDeliveries(contextMenu.id)}
              className="flex w-full items-center gap-2 rounded-sm px-2 py-1.5 text-sm hover:bg-accent"
            >
              <History className="h-4 w-4" />
              Delivery History
            </button>
            <button
              onClick={() => {
                const webhook = webhooks.find((w) => w.id === contextMenu.id)
                if (webhook) handleToggleActive(webhook)
              }}
              className="flex w-full items-center gap-2 rounded-sm px-2 py-1.5 text-sm hover:bg-accent"
            >
              {webhooks.find((w) => w.id === contextMenu.id)?.isActive ? (
                <>
                  <PowerOff className="h-4 w-4" />
                  Deactivate
                </>
              ) : (
                <>
                  <Power className="h-4 w-4" />
                  Activate
                </>
              )}
            </button>
            <div className="my-1 h-px bg-border" />
            <button
              onClick={() => {
                const webhook = webhooks.find((w) => w.id === contextMenu.id)
                if (webhook) handleEdit(webhook)
              }}
              className="flex w-full items-center gap-2 rounded-sm px-2 py-1.5 text-sm hover:bg-accent"
            >
              <Pencil className="h-4 w-4" />
              Edit
            </button>
            <button
              onClick={() => handleDelete(contextMenu.id)}
              className={cn(
                'flex w-full items-center gap-2 rounded-sm px-2 py-1.5 text-sm hover:bg-accent',
                confirmDelete === contextMenu.id && 'text-destructive hover:bg-destructive/10'
              )}
            >
              <Trash2 className="h-4 w-4" />
              {confirmDelete === contextMenu.id ? 'Click again to confirm' : 'Delete'}
            </button>
          </div>
        </>
      )}

      {/* Deliveries Dialog */}
      {showDeliveries && selectedWebhookId && (
        <div className="fixed inset-0 z-50 flex items-center justify-center">
          <div className="fixed inset-0 bg-black/50" onClick={() => setShowDeliveries(false)} />
          <div className="relative z-10 w-full max-w-2xl rounded-lg border bg-background p-6 shadow-lg max-h-[80vh] overflow-hidden flex flex-col">
            <div className="flex items-center justify-between mb-4">
              <h2 className="text-lg font-semibold">
                Delivery History - {getWebhookName(selectedWebhookId)}
              </h2>
              <Button variant="ghost" size="sm" onClick={() => setShowDeliveries(false)}>
                Close
              </Button>
            </div>

            <div className="flex-1 overflow-auto">
              {deliveriesLoading ? (
                <div className="flex items-center justify-center py-8">
                  <Loader2 className="h-8 w-8 animate-spin text-muted-foreground" />
                </div>
              ) : deliveries.length === 0 ? (
                <div className="text-center py-8 text-muted-foreground">
                  No delivery history available
                </div>
              ) : (
                <div className="space-y-2">
                  {deliveries.map((delivery) => (
                    <div
                      key={delivery.id}
                      className="flex items-center justify-between rounded-lg border p-3"
                    >
                      <div className="flex items-center gap-3">
                        {getStatusIcon(delivery.status)}
                        <div>
                          <div className="flex items-center gap-2">
                            <span className="text-sm font-medium capitalize">
                              {delivery.event}
                            </span>
                            {delivery.httpStatusCode && (
                              <span
                                className={cn(
                                  'text-xs px-1.5 py-0.5 rounded',
                                  delivery.httpStatusCode >= 200 && delivery.httpStatusCode < 300
                                    ? 'bg-success/20 text-success'
                                    : 'bg-destructive/20 text-destructive'
                                )}
                              >
                                {delivery.httpStatusCode}
                              </span>
                            )}
                          </div>
                          <div className="text-xs text-muted-foreground">
                            {new Date(delivery.createdAt).toLocaleString()}
                            {delivery.attemptCount > 1 && ` (${delivery.attemptCount} attempts)`}
                          </div>
                        </div>
                      </div>
                      {delivery.errorMessage && (
                        <div className="text-xs text-destructive max-w-[200px] truncate">
                          {delivery.errorMessage}
                        </div>
                      )}
                    </div>
                  ))}
                </div>
              )}
            </div>
          </div>
        </div>
      )}

      {/* Webhook Dialog */}
      <WebhookDialog
        open={dialogOpen}
        onClose={() => setDialogOpen(false)}
        onSubmit={handleSubmit}
        webhook={editingWebhook}
      />
    </div>
  )
}
