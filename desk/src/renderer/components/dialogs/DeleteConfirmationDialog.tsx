import { useState, type ReactElement } from 'react'
import { AlertTriangle, Loader2 } from 'lucide-react'
import { Button } from '@/components/ui/Button'
import { Input } from '@/components/ui/Input'

interface DeleteConfirmationDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  title: string
  description: string
  itemName: string
  requireTypedConfirmation?: boolean
  onConfirm: () => Promise<void>
}

export function DeleteConfirmationDialog({
  open,
  onOpenChange,
  title,
  description,
  itemName,
  requireTypedConfirmation = false,
  onConfirm
}: DeleteConfirmationDialogProps): ReactElement | null {
  const [confirmText, setConfirmText] = useState('')
  const [isDeleting, setIsDeleting] = useState(false)
  const [error, setError] = useState<string | null>(null)

  if (!open) return null

  const isConfirmEnabled = requireTypedConfirmation
    ? confirmText === itemName
    : true

  const handleConfirm = async (): Promise<void> => {
    setError(null)
    setIsDeleting(true)

    try {
      await onConfirm()
      handleClose()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to delete')
    } finally {
      setIsDeleting(false)
    }
  }

  const handleClose = (): void => {
    onOpenChange(false)
    setConfirmText('')
    setError(null)
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50">
      <div className="w-full max-w-md rounded-lg bg-card shadow-xl">
        {/* Header */}
        <div className="flex items-start gap-4 border-b border-border px-6 py-4">
          <div className="flex h-10 w-10 flex-shrink-0 items-center justify-center rounded-full bg-destructive/10">
            <AlertTriangle className="h-5 w-5 text-destructive" />
          </div>
          <div>
            <h2 className="text-lg font-semibold">{title}</h2>
            <p className="mt-1 text-sm text-muted-foreground">{description}</p>
          </div>
        </div>

        {/* Content */}
        <div className="px-6 py-4 space-y-4">
          {requireTypedConfirmation && (
            <div className="space-y-2">
              <p className="text-sm text-muted-foreground">
                Type <span className="font-mono font-semibold text-foreground">{itemName}</span> to confirm:
              </p>
              <Input
                value={confirmText}
                onChange={(e) => setConfirmText(e.target.value)}
                placeholder={itemName}
                className="font-mono"
              />
            </div>
          )}

          {error && (
            <div className="p-3 rounded-md bg-destructive/10 text-destructive text-sm">
              {error}
            </div>
          )}
        </div>

        {/* Actions */}
        <div className="flex justify-end gap-2 border-t border-border px-6 py-4">
          <Button type="button" variant="outline" onClick={handleClose} disabled={isDeleting}>
            Cancel
          </Button>
          <Button
            variant="destructive"
            onClick={handleConfirm}
            disabled={!isConfirmEnabled || isDeleting}
          >
            {isDeleting && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}
            Delete
          </Button>
        </div>
      </div>
    </div>
  )
}
