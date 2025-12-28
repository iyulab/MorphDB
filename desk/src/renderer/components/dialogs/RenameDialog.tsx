import { useState, useEffect, type ReactElement } from 'react'
import { Loader2, X } from 'lucide-react'
import { Button } from '@/components/ui/Button'
import { Input } from '@/components/ui/Input'
import { Label } from '@/components/ui/Label'

interface RenameDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  title: string
  currentName: string
  onSubmit: (newName: string) => Promise<void>
}

export function RenameDialog({
  open,
  onOpenChange,
  title,
  currentName,
  onSubmit
}: RenameDialogProps): ReactElement | null {
  const [newName, setNewName] = useState(currentName)
  const [isSubmitting, setIsSubmitting] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (open) {
      setNewName(currentName)
      setError(null)
    }
  }, [open, currentName])

  if (!open) return null

  const handleSubmit = async (e: React.FormEvent): Promise<void> => {
    e.preventDefault()
    setError(null)

    if (!newName.trim()) {
      setError('Name is required')
      return
    }

    if (newName === currentName) {
      handleClose()
      return
    }

    setIsSubmitting(true)
    try {
      await onSubmit(newName)
      handleClose()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to rename')
    } finally {
      setIsSubmitting(false)
    }
  }

  const handleClose = (): void => {
    onOpenChange(false)
    setNewName(currentName)
    setError(null)
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50">
      <div className="w-full max-w-md rounded-lg bg-card shadow-xl">
        {/* Header */}
        <div className="flex items-center justify-between border-b border-border px-6 py-4">
          <h2 className="text-lg font-semibold">{title}</h2>
          <Button variant="ghost" size="icon" className="h-8 w-8" onClick={handleClose}>
            <X className="h-4 w-4" />
          </Button>
        </div>

        {/* Form */}
        <form onSubmit={handleSubmit} className="p-6 space-y-4">
          <div className="space-y-2">
            <Label htmlFor="newName">New Name</Label>
            <Input
              id="newName"
              value={newName}
              onChange={(e) => setNewName(e.target.value)}
              placeholder="Enter new name..."
              autoFocus
              required
            />
          </div>

          {error && (
            <div className="p-3 rounded-md bg-destructive/10 text-destructive text-sm">
              {error}
            </div>
          )}

          <div className="flex justify-end gap-2 pt-2">
            <Button type="button" variant="outline" onClick={handleClose}>
              Cancel
            </Button>
            <Button type="submit" disabled={isSubmitting}>
              {isSubmitting && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}
              Rename
            </Button>
          </div>
        </form>
      </div>
    </div>
  )
}
