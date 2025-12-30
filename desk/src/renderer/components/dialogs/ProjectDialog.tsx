import { useState, useEffect, type ReactElement } from 'react'
import { X } from 'lucide-react'
import { Button } from '@/components/ui/Button'
import type { ProjectApiResponse, CreateProjectRequest, UpdateProjectRequest, ProjectSettings } from '@/lib/api'

interface ProjectDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  project?: ProjectApiResponse | null // null for create, project for edit
  onSubmit: (data: CreateProjectRequest | UpdateProjectRequest) => Promise<void>
}

export function ProjectDialog({
  open,
  onOpenChange,
  project,
  onSubmit
}: ProjectDialogProps): ReactElement | null {
  const isEditing = !!project

  const [formData, setFormData] = useState({
    name: '',
    slug: '',
    organizationId: '',
    enableAuditLog: false,
    maxTables: 100,
    timezone: '',
    defaultLocale: ''
  })
  const [isSubmitting, setIsSubmitting] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (open) {
      if (project) {
        setFormData({
          name: project.name,
          slug: project.slug,
          organizationId: project.organizationId ?? '',
          enableAuditLog: project.settings?.enableAuditLog ?? false,
          maxTables: project.settings?.maxTables ?? 100,
          timezone: project.settings?.timezone ?? '',
          defaultLocale: project.settings?.defaultLocale ?? ''
        })
      } else {
        setFormData({
          name: '',
          slug: '',
          organizationId: '',
          enableAuditLog: false,
          maxTables: 100,
          timezone: '',
          defaultLocale: ''
        })
      }
      setError(null)
    }
  }, [open, project])

  // Auto-generate slug from name
  const generateSlug = (name: string): string => {
    return name
      .toLowerCase()
      .replace(/[^a-z0-9]+/g, '-')
      .replace(/^-|-$/g, '')
  }

  const handleNameChange = (name: string): void => {
    setFormData(prev => ({
      ...prev,
      name,
      slug: isEditing ? prev.slug : generateSlug(name)
    }))
  }

  const handleSubmit = async (e: React.FormEvent): Promise<void> => {
    e.preventDefault()
    setError(null)

    if (!formData.name.trim()) {
      setError('Project name is required')
      return
    }

    if (!isEditing && !formData.slug.trim()) {
      setError('Project slug is required')
      return
    }

    setIsSubmitting(true)
    try {
      const settings: ProjectSettings = {
        enableAuditLog: formData.enableAuditLog,
        maxTables: formData.maxTables,
        timezone: formData.timezone || undefined,
        defaultLocale: formData.defaultLocale || undefined
      }

      if (isEditing) {
        await onSubmit({
          name: formData.name,
          settings
        } as UpdateProjectRequest)
      } else {
        await onSubmit({
          name: formData.name,
          slug: formData.slug,
          organizationId: formData.organizationId || undefined,
          settings
        } as CreateProjectRequest)
      }
      onOpenChange(false)
    } catch (err) {
      setError((err as Error).message)
    } finally {
      setIsSubmitting(false)
    }
  }

  if (!open) return null

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center">
      <div className="fixed inset-0 bg-black/50" onClick={() => onOpenChange(false)} />
      <div className="relative z-50 w-full max-w-lg rounded-lg border bg-background p-6 shadow-lg max-h-[90vh] overflow-y-auto">
        <div className="flex items-center justify-between mb-4">
          <h2 className="text-lg font-semibold">
            {isEditing ? 'Edit Project' : 'Create New Project'}
          </h2>
          <Button variant="ghost" size="icon" onClick={() => onOpenChange(false)}>
            <X className="h-4 w-4" />
          </Button>
        </div>

        <form onSubmit={handleSubmit} className="space-y-4">
          {/* Basic Info */}
          <div className="space-y-3">
            <div>
              <label className="block text-sm font-medium mb-1">Project Name *</label>
              <input
                type="text"
                value={formData.name}
                onChange={(e) => handleNameChange(e.target.value)}
                placeholder="My Project"
                className="w-full rounded-md border bg-background px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-primary"
              />
            </div>

            {!isEditing && (
              <div>
                <label className="block text-sm font-medium mb-1">Slug *</label>
                <input
                  type="text"
                  value={formData.slug}
                  onChange={(e) => setFormData(prev => ({ ...prev, slug: e.target.value }))}
                  placeholder="my-project"
                  className="w-full rounded-md border bg-background px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-primary"
                />
                <p className="text-xs text-muted-foreground mt-1">
                  Unique identifier for API access (auto-generated from name)
                </p>
              </div>
            )}

            {!isEditing && (
              <div>
                <label className="block text-sm font-medium mb-1">Organization ID</label>
                <input
                  type="text"
                  value={formData.organizationId}
                  onChange={(e) => setFormData(prev => ({ ...prev, organizationId: e.target.value }))}
                  placeholder="Optional organization UUID"
                  className="w-full rounded-md border bg-background px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-primary"
                />
              </div>
            )}
          </div>

          {/* Settings */}
          <div className="p-3 rounded-md bg-muted/50 space-y-3">
            <h3 className="text-sm font-medium">Settings</h3>

            <div className="flex items-center gap-2">
              <input
                type="checkbox"
                id="enableAuditLog"
                checked={formData.enableAuditLog}
                onChange={(e) => setFormData(prev => ({ ...prev, enableAuditLog: e.target.checked }))}
                className="rounded border-gray-300"
              />
              <label htmlFor="enableAuditLog" className="text-sm">
                Enable Audit Logging
              </label>
            </div>

            <div className="grid grid-cols-2 gap-3">
              <div>
                <label className="block text-xs text-muted-foreground mb-1">
                  Max Tables
                </label>
                <input
                  type="number"
                  value={formData.maxTables}
                  onChange={(e) => setFormData(prev => ({ ...prev, maxTables: parseInt(e.target.value) || 100 }))}
                  min={1}
                  max={1000}
                  className="w-full rounded-md border bg-background px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-primary"
                />
              </div>
              <div>
                <label className="block text-xs text-muted-foreground mb-1">
                  Timezone
                </label>
                <input
                  type="text"
                  value={formData.timezone}
                  onChange={(e) => setFormData(prev => ({ ...prev, timezone: e.target.value }))}
                  placeholder="UTC"
                  className="w-full rounded-md border bg-background px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-primary"
                />
              </div>
            </div>

            <div>
              <label className="block text-xs text-muted-foreground mb-1">
                Default Locale
              </label>
              <input
                type="text"
                value={formData.defaultLocale}
                onChange={(e) => setFormData(prev => ({ ...prev, defaultLocale: e.target.value }))}
                placeholder="en-US"
                className="w-full rounded-md border bg-background px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-primary"
              />
            </div>
          </div>

          {error && <p className="text-sm text-destructive">{error}</p>}

          <div className="flex justify-end gap-2 pt-4">
            <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>
              Cancel
            </Button>
            <Button type="submit" disabled={isSubmitting}>
              {isSubmitting ? (isEditing ? 'Saving...' : 'Creating...') : (isEditing ? 'Save Changes' : 'Create Project')}
            </Button>
          </div>
        </form>
      </div>
    </div>
  )
}
