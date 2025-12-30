import { useState, useEffect, type ReactElement } from 'react'
import { X } from 'lucide-react'
import { Button } from '@/components/ui/Button'
import { Input } from '@/components/ui/Input'
import type {
  OrganizationApiResponse,
  CreateOrganizationApiRequest,
  UpdateOrganizationApiRequest
} from '@/lib/api'

interface OrganizationDialogProps {
  open: boolean
  onClose: () => void
  onSubmit: (data: CreateOrganizationApiRequest | UpdateOrganizationApiRequest) => Promise<void>
  organization?: OrganizationApiResponse | null
}

export function OrganizationDialog({
  open,
  onClose,
  onSubmit,
  organization
}: OrganizationDialogProps): ReactElement | null {
  const isEditing = !!organization

  const [name, setName] = useState('')
  const [slug, setSlug] = useState('')
  const [description, setDescription] = useState('')
  const [maxProjects, setMaxProjects] = useState<number | undefined>()
  const [enableAuditLog, setEnableAuditLog] = useState(false)
  const [isSubmitting, setIsSubmitting] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (open) {
      if (organization) {
        setName(organization.name)
        setSlug(organization.slug)
        setDescription(organization.description || '')
        setMaxProjects(organization.settings?.maxProjects)
        setEnableAuditLog(organization.settings?.enableAuditLog || false)
      } else {
        setName('')
        setSlug('')
        setDescription('')
        setMaxProjects(undefined)
        setEnableAuditLog(false)
      }
      setError(null)
    }
  }, [open, organization])

  // Auto-generate slug from name
  const handleNameChange = (value: string): void => {
    setName(value)
    if (!isEditing && !slug) {
      const generatedSlug = value
        .toLowerCase()
        .replace(/[^a-z0-9]+/g, '-')
        .replace(/^-|-$/g, '')
      setSlug(generatedSlug)
    }
  }

  const handleSubmit = async (e: React.FormEvent): Promise<void> => {
    e.preventDefault()
    setError(null)
    setIsSubmitting(true)

    try {
      if (!name.trim()) {
        throw new Error('Name is required')
      }

      const settings = {
        maxProjects,
        enableAuditLog
      }

      if (isEditing) {
        const updateData: UpdateOrganizationApiRequest = {
          name: name.trim(),
          description: description.trim() || undefined,
          settings
        }
        await onSubmit(updateData)
      } else {
        const createData: CreateOrganizationApiRequest = {
          name: name.trim(),
          slug: slug.trim() || undefined,
          description: description.trim() || undefined,
          settings
        }
        await onSubmit(createData)
      }

      onClose()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to save organization')
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
          <h2 className="text-lg font-semibold">
            {isEditing ? 'Edit Organization' : 'New Organization'}
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
              onChange={(e) => handleNameChange(e.target.value)}
              placeholder="My Organization"
            />
          </div>

          {/* Slug */}
          <div className="space-y-2">
            <label className="text-sm font-medium">Slug</label>
            <Input
              value={slug}
              onChange={(e) => setSlug(e.target.value)}
              placeholder="my-organization"
              disabled={isEditing}
              className={isEditing ? 'bg-muted' : ''}
            />
            <p className="text-xs text-muted-foreground">
              URL-friendly identifier (auto-generated from name)
            </p>
          </div>

          {/* Description */}
          <div className="space-y-2">
            <label className="text-sm font-medium">Description</label>
            <textarea
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              placeholder="Organization description..."
              rows={2}
              className="w-full rounded-md border border-input bg-background px-3 py-2 text-sm resize-none"
            />
          </div>

          {/* Settings */}
          <div className="space-y-4 pt-2 border-t">
            <h3 className="text-sm font-medium">Settings</h3>

            <div className="space-y-2">
              <label className="text-sm font-medium">Max Projects</label>
              <Input
                type="number"
                value={maxProjects || ''}
                onChange={(e) =>
                  setMaxProjects(e.target.value ? parseInt(e.target.value) : undefined)
                }
                placeholder="Unlimited"
                min={1}
              />
            </div>

            <div className="flex items-center gap-2">
              <input
                type="checkbox"
                id="enableAuditLog"
                checked={enableAuditLog}
                onChange={(e) => setEnableAuditLog(e.target.checked)}
                className="h-4 w-4 rounded border-input"
              />
              <label htmlFor="enableAuditLog" className="text-sm font-medium">
                Enable Audit Log
              </label>
            </div>
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
              {isSubmitting ? 'Saving...' : isEditing ? 'Save Changes' : 'Create Organization'}
            </Button>
          </div>
        </form>
      </div>
    </div>
  )
}
