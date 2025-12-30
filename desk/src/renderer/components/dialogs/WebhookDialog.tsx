import { useState, useEffect, type ReactElement } from 'react'
import { useQuery } from '@tanstack/react-query'
import { X, Plus, Trash2 } from 'lucide-react'
import { Button } from '@/components/ui/Button'
import { Input } from '@/components/ui/Input'
import { useConnectionStore } from '@/stores/connectionStore'
import { cn } from '@/lib/utils'
import {
  MorphDBClient,
  type WebhookApiResponse,
  type CreateWebhookApiRequest,
  type UpdateWebhookApiRequest,
  type TableApiResponse
} from '@/lib/api'

interface WebhookDialogProps {
  open: boolean
  onClose: () => void
  onSubmit: (data: CreateWebhookApiRequest | UpdateWebhookApiRequest) => Promise<void>
  webhook?: WebhookApiResponse | null
}

const EVENT_OPTIONS = [
  { value: 'insert', label: 'Insert' },
  { value: 'update', label: 'Update' },
  { value: 'delete', label: 'Delete' }
]

export function WebhookDialog({
  open,
  onClose,
  onSubmit,
  webhook
}: WebhookDialogProps): ReactElement | null {
  const { activeConnection, getApiKey } = useConnectionStore()
  const isEditing = !!webhook

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

  const [name, setName] = useState('')
  const [table, setTable] = useState('')
  const [url, setUrl] = useState('')
  const [events, setEvents] = useState<string[]>(['insert', 'update', 'delete'])
  const [headers, setHeaders] = useState<{ key: string; value: string }[]>([])
  const [filter, setFilter] = useState('')
  const [isActive, setIsActive] = useState(true)
  const [isSubmitting, setIsSubmitting] = useState(false)
  const [error, setError] = useState<string | null>(null)

  // Fetch tables for dropdown
  const { data: tables = [] } = useQuery<TableApiResponse[]>({
    queryKey: ['tables', activeConnection?.id],
    queryFn: async () => {
      const client = await createClient()
      if (!client) return []
      return client.listTables()
    },
    enabled: open && !!activeConnection
  })

  // Reset form when dialog opens/closes or webhook changes
  useEffect(() => {
    if (open) {
      if (webhook) {
        setName(webhook.name)
        setTable(webhook.table)
        setUrl(webhook.url)
        setEvents(webhook.events)
        setHeaders(
          webhook.headers
            ? Object.entries(webhook.headers).map(([key, value]) => ({ key, value }))
            : []
        )
        setFilter(webhook.filter ? JSON.stringify(webhook.filter) : '')
        setIsActive(webhook.isActive)
      } else {
        setName('')
        setTable('')
        setUrl('')
        setEvents(['insert', 'update', 'delete'])
        setHeaders([])
        setFilter('')
        setIsActive(true)
      }
      setError(null)
    }
  }, [open, webhook])

  const handleSubmit = async (e: React.FormEvent): Promise<void> => {
    e.preventDefault()
    setError(null)
    setIsSubmitting(true)

    try {
      // Validate required fields
      if (!isEditing && !name.trim()) {
        throw new Error('Name is required')
      }
      if (!isEditing && !table) {
        throw new Error('Table is required')
      }
      if (!url.trim()) {
        throw new Error('URL is required')
      }
      if (events.length === 0) {
        throw new Error('At least one event must be selected')
      }

      // Parse filter if provided
      let parsedFilter: Record<string, unknown> | undefined
      if (filter.trim()) {
        try {
          parsedFilter = JSON.parse(filter)
        } catch {
          throw new Error('Invalid filter JSON')
        }
      }

      // Build headers object
      const headersObj: Record<string, string> = {}
      for (const h of headers) {
        if (h.key.trim() && h.value.trim()) {
          headersObj[h.key.trim()] = h.value.trim()
        }
      }

      if (isEditing) {
        const updateData: UpdateWebhookApiRequest = {
          url: url.trim(),
          events,
          filter: parsedFilter,
          headers: Object.keys(headersObj).length > 0 ? headersObj : undefined,
          isActive
        }
        await onSubmit(updateData)
      } else {
        const createData: CreateWebhookApiRequest = {
          name: name.trim(),
          table,
          url: url.trim(),
          events,
          filter: parsedFilter,
          headers: Object.keys(headersObj).length > 0 ? headersObj : undefined
        }
        await onSubmit(createData)
      }

      onClose()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to save webhook')
    } finally {
      setIsSubmitting(false)
    }
  }

  const toggleEvent = (event: string): void => {
    setEvents((prev) =>
      prev.includes(event) ? prev.filter((e) => e !== event) : [...prev, event]
    )
  }

  const addHeader = (): void => {
    setHeaders([...headers, { key: '', value: '' }])
  }

  const removeHeader = (index: number): void => {
    setHeaders(headers.filter((_, i) => i !== index))
  }

  const updateHeader = (index: number, field: 'key' | 'value', value: string): void => {
    setHeaders(headers.map((h, i) => (i === index ? { ...h, [field]: value } : h)))
  }

  if (!open) return null

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center">
      <div className="fixed inset-0 bg-black/50" onClick={onClose} />
      <div className="relative z-10 w-full max-w-lg rounded-lg border bg-background p-6 shadow-lg max-h-[90vh] overflow-y-auto">
        <div className="flex items-center justify-between mb-4">
          <h2 className="text-lg font-semibold">
            {isEditing ? 'Edit Webhook' : 'New Webhook'}
          </h2>
          <Button variant="ghost" size="icon" onClick={onClose}>
            <X className="h-4 w-4" />
          </Button>
        </div>

        <form onSubmit={handleSubmit} className="space-y-4">
          {/* Name */}
          <div className="space-y-2">
            <label className="text-sm font-medium">Name</label>
            <Input
              value={name}
              onChange={(e) => setName(e.target.value)}
              placeholder="my-webhook"
              disabled={isEditing}
              className={isEditing ? 'bg-muted' : ''}
            />
          </div>

          {/* Table */}
          <div className="space-y-2">
            <label className="text-sm font-medium">Table</label>
            <select
              value={table}
              onChange={(e) => setTable(e.target.value)}
              disabled={isEditing}
              className={cn(
                'w-full h-9 rounded-md border border-input bg-background px-3 text-sm',
                isEditing && 'bg-muted'
              )}
            >
              <option value="">Select a table...</option>
              {tables.map((t) => (
                <option key={t.id} value={t.name}>
                  {t.name}
                </option>
              ))}
            </select>
          </div>

          {/* URL */}
          <div className="space-y-2">
            <label className="text-sm font-medium">Webhook URL</label>
            <Input
              type="url"
              value={url}
              onChange={(e) => setUrl(e.target.value)}
              placeholder="https://example.com/webhook"
            />
          </div>

          {/* Events */}
          <div className="space-y-2">
            <label className="text-sm font-medium">Events</label>
            <div className="flex gap-2 flex-wrap">
              {EVENT_OPTIONS.map((option) => (
                <button
                  key={option.value}
                  type="button"
                  onClick={() => toggleEvent(option.value)}
                  className={cn(
                    'px-3 py-1.5 text-sm rounded-md border transition-colors',
                    events.includes(option.value)
                      ? 'bg-primary text-primary-foreground border-primary'
                      : 'bg-background border-input hover:bg-muted'
                  )}
                >
                  {option.label}
                </button>
              ))}
            </div>
          </div>

          {/* Headers */}
          <div className="space-y-2">
            <div className="flex items-center justify-between">
              <label className="text-sm font-medium">Headers</label>
              <Button type="button" variant="ghost" size="sm" onClick={addHeader}>
                <Plus className="h-4 w-4 mr-1" />
                Add Header
              </Button>
            </div>
            {headers.length > 0 && (
              <div className="space-y-2">
                {headers.map((header, index) => (
                  <div key={index} className="flex gap-2 items-center">
                    <Input
                      value={header.key}
                      onChange={(e) => updateHeader(index, 'key', e.target.value)}
                      placeholder="Header name"
                      className="flex-1"
                    />
                    <Input
                      value={header.value}
                      onChange={(e) => updateHeader(index, 'value', e.target.value)}
                      placeholder="Value"
                      className="flex-1"
                    />
                    <Button
                      type="button"
                      variant="ghost"
                      size="icon"
                      onClick={() => removeHeader(index)}
                    >
                      <Trash2 className="h-4 w-4 text-destructive" />
                    </Button>
                  </div>
                ))}
              </div>
            )}
          </div>

          {/* Filter */}
          <div className="space-y-2">
            <label className="text-sm font-medium">Filter (JSON)</label>
            <textarea
              value={filter}
              onChange={(e) => setFilter(e.target.value)}
              placeholder='{"field": "value"}'
              rows={3}
              className="w-full rounded-md border border-input bg-background px-3 py-2 text-sm font-mono resize-none"
            />
            <p className="text-xs text-muted-foreground">
              Optional filter to only trigger webhook for matching records
            </p>
          </div>

          {/* Active toggle (only for editing) */}
          {isEditing && (
            <div className="flex items-center gap-2">
              <input
                type="checkbox"
                id="isActive"
                checked={isActive}
                onChange={(e) => setIsActive(e.target.checked)}
                className="h-4 w-4 rounded border-input"
              />
              <label htmlFor="isActive" className="text-sm font-medium">
                Active
              </label>
            </div>
          )}

          {/* Error */}
          {error && (
            <div className="rounded-md bg-destructive/10 p-3 text-sm text-destructive">
              {error}
            </div>
          )}

          {/* Actions */}
          <div className="flex justify-end gap-2 pt-4">
            <Button type="button" variant="ghost" onClick={onClose}>
              Cancel
            </Button>
            <Button type="submit" disabled={isSubmitting}>
              {isSubmitting ? 'Saving...' : isEditing ? 'Save Changes' : 'Create Webhook'}
            </Button>
          </div>
        </form>
      </div>
    </div>
  )
}
