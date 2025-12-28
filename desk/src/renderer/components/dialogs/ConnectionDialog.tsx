import { useState } from 'react'
import { X, Loader2, Check, AlertCircle } from 'lucide-react'
import { Button } from '@/components/ui/Button'
import { Input } from '@/components/ui/Input'
import { Label } from '@/components/ui/Label'
import { useConnectionStore } from '@/stores/connectionStore'
import type { ConnectionFormData } from '@/types/connection'
import { cn } from '@/lib/utils'

interface ConnectionDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
}

type TestStatus = 'idle' | 'testing' | 'success' | 'error'

export function ConnectionDialog({ open, onOpenChange }: ConnectionDialogProps): JSX.Element | null {
  const { addConnection, setActiveConnection } = useConnectionStore()
  const [testStatus, setTestStatus] = useState<TestStatus>('idle')
  const [testError, setTestError] = useState('')

  const [formData, setFormData] = useState<ConnectionFormData>({
    name: '',
    url: 'http://localhost:5000',
    apiKey: '',
    tenantId: ''
  })

  if (!open) return null

  const handleTest = async (): Promise<void> => {
    setTestStatus('testing')
    setTestError('')

    try {
      const response = await fetch(`${formData.url}/health`, {
        method: 'GET',
        headers: {
          'X-API-Key': formData.apiKey,
          ...(formData.tenantId && { 'X-Tenant-Id': formData.tenantId })
        }
      })

      if (response.ok) {
        setTestStatus('success')
      } else {
        setTestStatus('error')
        setTestError(`Server returned ${response.status}`)
      }
    } catch (err) {
      setTestStatus('error')
      setTestError(err instanceof Error ? err.message : 'Connection failed')
    }
  }

  const handleSubmit = (e: React.FormEvent): void => {
    e.preventDefault()

    const connection = addConnection(formData)
    setActiveConnection(connection.id)
    onOpenChange(false)

    // Reset form
    setFormData({
      name: '',
      url: 'http://localhost:5000',
      apiKey: '',
      tenantId: ''
    })
    setTestStatus('idle')
  }

  const isValid = formData.name.trim() && formData.url.trim() && formData.apiKey.trim()

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50">
      <div className="w-full max-w-md rounded-lg bg-card p-6 shadow-xl">
        {/* Header */}
        <div className="mb-4 flex items-center justify-between">
          <h2 className="text-lg font-semibold">New Connection</h2>
          <Button
            variant="ghost"
            size="icon"
            className="h-8 w-8"
            onClick={() => onOpenChange(false)}
          >
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
            <Label htmlFor="apiKey">API Key</Label>
            <Input
              id="apiKey"
              type="password"
              placeholder="Your API key"
              value={formData.apiKey}
              onChange={(e) => setFormData({ ...formData, apiKey: e.target.value })}
              required
            />
          </div>

          <div className="space-y-2">
            <Label htmlFor="tenantId">Tenant ID (optional)</Label>
            <Input
              id="tenantId"
              placeholder="default"
              value={formData.tenantId}
              onChange={(e) => setFormData({ ...formData, tenantId: e.target.value })}
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
              disabled={!isValid || testStatus === 'testing'}
            >
              Test Connection
            </Button>
            <Button type="submit" disabled={!isValid}>
              Connect
            </Button>
          </div>
        </form>
      </div>
    </div>
  )
}
