import { useState, useEffect, type ReactElement } from 'react'
import { X, Loader2, Check, AlertCircle } from 'lucide-react'
import { Button } from '@/components/ui/Button'
import { Input } from '@/components/ui/Input'
import { Label } from '@/components/ui/Label'
import { useConnectionStore } from '@/stores/connectionStore'
import type { Connection, ConnectionFormData } from '@/types/connection'
import { cn } from '@/lib/utils'

interface ConnectionDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  editConnection?: Connection | null
}

type TestStatus = 'idle' | 'testing' | 'success' | 'error'

const initialFormData: ConnectionFormData = {
  name: '',
  url: 'http://localhost:5000',
  apiKey: ''
}

export function ConnectionDialog({
  open,
  onOpenChange,
  editConnection
}: ConnectionDialogProps): ReactElement | null {
  const { addConnection, updateConnection, setActiveConnection, connectToServer } =
    useConnectionStore()
  const [testStatus, setTestStatus] = useState<TestStatus>('idle')
  const [testError, setTestError] = useState('')
  const [isSubmitting, setIsSubmitting] = useState(false)

  const [formData, setFormData] = useState<ConnectionFormData>(initialFormData)

  const isEditMode = !!editConnection

  // Load connection data when editing
  useEffect(() => {
    if (editConnection) {
      setFormData({
        name: editConnection.name,
        url: editConnection.url,
        apiKey: '' // API key is stored securely, user needs to re-enter
      })
    } else {
      setFormData(initialFormData)
    }
    setTestStatus('idle')
    setTestError('')
  }, [editConnection, open])

  if (!open) return null

  const handleTest = async (): Promise<void> => {
    setTestStatus('testing')
    setTestError('')

    try {
      const result = await window.api.testConnection(
        formData.url,
        formData.apiKey
      )

      if (result.success) {
        setTestStatus('success')
      } else {
        setTestStatus('error')
        setTestError(result.error || 'Connection failed')
      }
    } catch (err) {
      setTestStatus('error')
      setTestError(err instanceof Error ? err.message : 'Connection failed')
    }
  }

  const handleSubmit = async (e: React.FormEvent): Promise<void> => {
    e.preventDefault()
    setIsSubmitting(true)

    try {
      if (isEditMode && editConnection) {
        // Update existing connection
        await updateConnection(editConnection.id, formData)
        onOpenChange(false)
      } else {
        // Create new connection
        const connection = await addConnection(formData)
        setActiveConnection(connection.id)
        // Try to connect after creating
        await connectToServer(connection.id)
        onOpenChange(false)
      }

      // Reset form
      setFormData(initialFormData)
      setTestStatus('idle')
    } finally {
      setIsSubmitting(false)
    }
  }

  const handleClose = (): void => {
    onOpenChange(false)
    setFormData(initialFormData)
    setTestStatus('idle')
    setTestError('')
  }

  const isValid =
    formData.name.trim() &&
    formData.url.trim() &&
    (isEditMode || formData.apiKey.trim()) // API key required only for new connections

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50">
      <div className="w-full max-w-md rounded-lg bg-card p-6 shadow-xl">
        {/* Header */}
        <div className="mb-4 flex items-center justify-between">
          <h2 className="text-lg font-semibold">
            {isEditMode ? 'Edit Connection' : 'New Connection'}
          </h2>
          <Button variant="ghost" size="icon" className="h-8 w-8" onClick={handleClose}>
            <X className="h-4 w-4" />
          </Button>
        </div>

        {/* Form */}
        <form onSubmit={handleSubmit} className="space-y-4">
          <div className="space-y-2">
            <Label htmlFor="name">Connection Name</Label>
            <Input
              id="name"
              placeholder="My MorphDB Server"
              value={formData.name}
              onChange={(e) => setFormData({ ...formData, name: e.target.value })}
              required
            />
          </div>

          <div className="space-y-2">
            <Label htmlFor="url">Server URL</Label>
            <Input
              id="url"
              type="url"
              placeholder="http://localhost:5000"
              value={formData.url}
              onChange={(e) => setFormData({ ...formData, url: e.target.value })}
              required
            />
          </div>

          <div className="space-y-2">
            <Label htmlFor="apiKey">
              API Key {isEditMode && <span className="text-muted-foreground">(leave empty to keep current)</span>}
            </Label>
            <Input
              id="apiKey"
              type="password"
              placeholder={isEditMode ? '••••••••' : 'Your API key'}
              value={formData.apiKey}
              onChange={(e) => setFormData({ ...formData, apiKey: e.target.value })}
              required={!isEditMode}
            />
          </div>

          {/* Test Result */}
          {testStatus !== 'idle' && (
            <div
              className={cn(
                'flex items-center gap-2 rounded-md p-3 text-sm',
                testStatus === 'testing' && 'bg-muted',
                testStatus === 'success' && 'bg-success/10 text-success',
                testStatus === 'error' && 'bg-destructive/10 text-destructive'
              )}
            >
              {testStatus === 'testing' && (
                <>
                  <Loader2 className="h-4 w-4 animate-spin" />
                  Testing connection...
                </>
              )}
              {testStatus === 'success' && (
                <>
                  <Check className="h-4 w-4" />
                  Connection successful!
                </>
              )}
              {testStatus === 'error' && (
                <>
                  <AlertCircle className="h-4 w-4" />
                  {testError || 'Connection failed'}
                </>
              )}
            </div>
          )}

          {/* Actions */}
          <div className="flex justify-end gap-2 pt-2">
            <Button
              type="button"
              variant="outline"
              onClick={handleTest}
              disabled={!formData.url.trim() || !formData.apiKey.trim() || testStatus === 'testing'}
            >
              Test Connection
            </Button>
            <Button type="submit" disabled={!isValid || isSubmitting}>
              {isSubmitting && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}
              {isEditMode ? 'Save Changes' : 'Connect'}
            </Button>
          </div>
        </form>
      </div>
    </div>
  )
}
