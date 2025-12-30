import { useState, useEffect, type ReactElement } from 'react'
import { X } from 'lucide-react'
import { Button } from '@/components/ui/Button'
import { Input } from '@/components/ui/Input'
import type { BackupType, CreateBackupApiRequest } from '@/lib/api'
import { cn } from '@/lib/utils'

interface BackupDialogProps {
  open: boolean
  onClose: () => void
  onSubmit: (data: CreateBackupApiRequest) => Promise<unknown>
}

const BACKUP_TYPE_OPTIONS: { value: BackupType; label: string; description: string }[] = [
  { value: 'Full', label: 'Full Backup', description: 'Schema and data' },
  { value: 'SchemaOnly', label: 'Schema Only', description: 'DDL statements only' },
  { value: 'DataOnly', label: 'Data Only', description: 'Data without schema' }
]

const EXPIRATION_OPTIONS = [
  { value: undefined, label: 'Never' },
  { value: 7, label: '7 days' },
  { value: 30, label: '30 days' },
  { value: 90, label: '90 days' },
  { value: 365, label: '1 year' }
]

export function BackupDialog({
  open,
  onClose,
  onSubmit
}: BackupDialogProps): ReactElement | null {
  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [type, setType] = useState<BackupType>('Full')
  const [expiresInDays, setExpiresInDays] = useState<number | undefined>(undefined)
  const [isSubmitting, setIsSubmitting] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (open) {
      // Generate default backup name with timestamp
      const now = new Date()
      const timestamp = now.toISOString().slice(0, 19).replace(/[T:]/g, '-')
      setName(`backup-${timestamp}`)
      setDescription('')
      setType('Full')
      setExpiresInDays(undefined)
      setError(null)
    }
  }, [open])

  const handleSubmit = async (e: React.FormEvent): Promise<void> => {
    e.preventDefault()
    setError(null)
    setIsSubmitting(true)

    try {
      if (!name.trim()) {
        throw new Error('Name is required')
      }

      const data: CreateBackupApiRequest = {
        name: name.trim(),
        description: description.trim() || undefined,
        type,
        expiresInDays
      }

      await onSubmit(data)
      onClose()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to create backup')
    } finally {
      setIsSubmitting(false)
    }
  }

  if (!open) return null

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center">
      <div className="fixed inset-0 bg-black/50" onClick={onClose} />
      <div className="relative z-10 w-full max-w-md rounded-lg border bg-background p-6 shadow-lg">
        <div className="flex items-center justify-between mb-4">
          <h2 className="text-lg font-semibold">Create Backup</h2>
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
              placeholder="backup-2024-01-01"
            />
          </div>

          {/* Description */}
          <div className="space-y-2">
            <label className="text-sm font-medium">Description</label>
            <textarea
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              placeholder="Optional description..."
              rows={2}
              className="w-full rounded-md border border-input bg-background px-3 py-2 text-sm resize-none"
            />
          </div>

          {/* Backup Type */}
          <div className="space-y-2">
            <label className="text-sm font-medium">Backup Type</label>
            <div className="space-y-2">
              {BACKUP_TYPE_OPTIONS.map((option) => (
                <button
                  key={option.value}
                  type="button"
                  onClick={() => setType(option.value)}
                  className={cn(
                    'w-full flex items-center justify-between px-3 py-2 rounded-md border text-left transition-colors',
                    type === option.value
                      ? 'bg-primary/10 border-primary'
                      : 'bg-background border-input hover:bg-muted'
                  )}
                >
                  <span className="font-medium text-sm">{option.label}</span>
                  <span className="text-xs text-muted-foreground">{option.description}</span>
                </button>
              ))}
            </div>
          </div>

          {/* Expiration */}
          <div className="space-y-2">
            <label className="text-sm font-medium">Expiration</label>
            <select
              value={expiresInDays ?? ''}
              onChange={(e) =>
                setExpiresInDays(e.target.value ? parseInt(e.target.value) : undefined)
              }
              className="w-full h-9 rounded-md border border-input bg-background px-3 text-sm"
            >
              {EXPIRATION_OPTIONS.map((option) => (
                <option key={option.label} value={option.value ?? ''}>
                  {option.label}
                </option>
              ))}
            </select>
            <p className="text-xs text-muted-foreground">
              Backup will be automatically deleted after expiration
            </p>
          </div>

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
              {isSubmitting ? 'Creating...' : 'Create Backup'}
            </Button>
          </div>
        </form>
      </div>
    </div>
  )
}
