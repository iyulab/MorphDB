import { useState, useEffect, type ReactElement } from 'react'
import { X, AlertTriangle } from 'lucide-react'
import { Button } from '@/components/ui/Button'
import type { BackupApiResponse, ProjectApiResponse, RestoreBackupApiRequest } from '@/lib/api'
import { cn } from '@/lib/utils'

interface RestoreDialogProps {
  open: boolean
  onClose: () => void
  onSubmit: (data: RestoreBackupApiRequest) => Promise<unknown>
  backup: BackupApiResponse | null
  projects: ProjectApiResponse[]
  currentProjectId: string
}

export function RestoreDialog({
  open,
  onClose,
  onSubmit,
  backup,
  projects,
  currentProjectId
}: RestoreDialogProps): ReactElement | null {
  const [targetProjectId, setTargetProjectId] = useState<string>(currentProjectId)
  const [dropExisting, setDropExisting] = useState(false)
  const [isSubmitting, setIsSubmitting] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [confirmText, setConfirmText] = useState('')

  useEffect(() => {
    if (open) {
      setTargetProjectId(currentProjectId)
      setDropExisting(false)
      setConfirmText('')
      setError(null)
    }
  }, [open, currentProjectId])

  const handleSubmit = async (e: React.FormEvent): Promise<void> => {
    e.preventDefault()
    setError(null)

    if (dropExisting && confirmText !== 'DROP') {
      setError('Please type DROP to confirm dropping existing data')
      return
    }

    setIsSubmitting(true)

    try {
      const data: RestoreBackupApiRequest = {
        targetProjectId: targetProjectId !== currentProjectId ? targetProjectId : undefined,
        dropExisting
      }

      await onSubmit(data)
      onClose()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to restore backup')
    } finally {
      setIsSubmitting(false)
    }
  }

  if (!open || !backup) return null

  const formatBytes = (bytes: number): string => {
    if (bytes === 0) return '0 B'
    const k = 1024
    const sizes = ['B', 'KB', 'MB', 'GB']
    const i = Math.floor(Math.log(bytes) / Math.log(k))
    return `${parseFloat((bytes / Math.pow(k, i)).toFixed(1))} ${sizes[i]}`
  }

  const formatDate = (date: string): string => {
    return new Date(date).toLocaleString()
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center">
      <div className="fixed inset-0 bg-black/50" onClick={onClose} />
      <div className="relative z-10 w-full max-w-md rounded-lg border bg-background p-6 shadow-lg">
        <div className="flex items-center justify-between mb-4">
          <h2 className="text-lg font-semibold">Restore Backup</h2>
          <Button variant="ghost" size="icon" onClick={onClose}>
            <X className="h-4 w-4" />
          </Button>
        </div>

        {/* Backup Info */}
        <div className="mb-4 p-3 rounded-md bg-muted/50 border space-y-1">
          <div className="font-medium">{backup.name}</div>
          <div className="text-sm text-muted-foreground">
            Type: {backup.type} • Size: {formatBytes(backup.sizeBytes)}
          </div>
          <div className="text-sm text-muted-foreground">
            Created: {formatDate(backup.startedAt)}
          </div>
          {backup.metadata && (
            <div className="text-sm text-muted-foreground">
              {backup.metadata.tableCount} tables • ~{backup.metadata.estimatedRowCount.toLocaleString()} rows
            </div>
          )}
        </div>

        <form onSubmit={handleSubmit} className="space-y-4">
          {/* Target Project */}
          <div className="space-y-2">
            <label className="text-sm font-medium">Target Project</label>
            <select
              value={targetProjectId}
              onChange={(e) => setTargetProjectId(e.target.value)}
              className="w-full h-9 rounded-md border border-input bg-background px-3 text-sm"
            >
              {projects.map((project) => (
                <option key={project.projectId} value={project.projectId}>
                  {project.name}
                  {project.projectId === currentProjectId ? ' (current)' : ''}
                </option>
              ))}
            </select>
            <p className="text-xs text-muted-foreground">
              Restore to the same project or a different one
            </p>
          </div>

          {/* Drop Existing */}
          <div className="space-y-2">
            <div className="flex items-center gap-2">
              <input
                type="checkbox"
                id="dropExisting"
                checked={dropExisting}
                onChange={(e) => setDropExisting(e.target.checked)}
                className="h-4 w-4 rounded border-input"
              />
              <label htmlFor="dropExisting" className="text-sm font-medium">
                Drop existing objects before restore
              </label>
            </div>
          </div>

          {/* Warning for drop existing */}
          {dropExisting && (
            <div className="rounded-md bg-warning/10 border border-warning/30 p-3 space-y-3">
              <div className="flex items-center gap-2 text-warning">
                <AlertTriangle className="h-4 w-4" />
                <span className="text-sm font-medium">Destructive Operation</span>
              </div>
              <p className="text-sm text-muted-foreground">
                This will delete all existing tables and data in the target project before restoring.
                This action cannot be undone.
              </p>
              <div className="space-y-2">
                <label className="text-sm">
                  Type <strong>DROP</strong> to confirm:
                </label>
                <input
                  type="text"
                  value={confirmText}
                  onChange={(e) => setConfirmText(e.target.value)}
                  placeholder="DROP"
                  className={cn(
                    'w-full h-9 rounded-md border bg-background px-3 text-sm',
                    confirmText === 'DROP' ? 'border-success' : 'border-input'
                  )}
                />
              </div>
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
            <Button
              type="submit"
              disabled={isSubmitting || (dropExisting && confirmText !== 'DROP')}
              variant={dropExisting ? 'destructive' : 'default'}
            >
              {isSubmitting ? 'Restoring...' : 'Restore Backup'}
            </Button>
          </div>
        </form>
      </div>
    </div>
  )
}
