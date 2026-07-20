import { type ReactElement } from 'react'
import {
  FolderKanban,
  MoreVertical,
  Play,
  Pause,
  Archive,
  Trash2,
  Pencil,
  Activity,
  CheckCircle,
  XCircle,
  AlertTriangle
} from 'lucide-react'
import { Button } from '@/components/ui/Button'
import type { ProjectApiResponse, ProjectStatus } from '@/lib/api'
import { cn } from '@/lib/utils'

interface ProjectCardProps {
  project: ProjectApiResponse
  onEdit?: () => void
  onSuspend?: () => void
  onReactivate?: () => void
  onArchive?: () => void
  onDelete?: () => void
  onViewHealth?: () => void
}

function getStatusIcon(status: ProjectStatus): ReactElement {
  switch (status) {
    case 'Active':
      return <CheckCircle className="h-4 w-4 text-success" />
    case 'Suspended':
      return <Pause className="h-4 w-4 text-warning" />
    case 'Archived':
      return <Archive className="h-4 w-4 text-muted-foreground" />
    default:
      return <AlertTriangle className="h-4 w-4 text-muted-foreground" />
  }
}

function getStatusColor(status: ProjectStatus): string {
  switch (status) {
    case 'Active':
      return 'text-success'
    case 'Suspended':
      return 'text-warning'
    case 'Archived':
      return 'text-muted-foreground'
    default:
      return 'text-muted-foreground'
  }
}

function formatDate(dateString: string): string {
  return new Date(dateString).toLocaleDateString('en-US', {
    year: 'numeric',
    month: 'short',
    day: 'numeric'
  })
}

export function ProjectCard({
  project,
  onEdit,
  onSuspend,
  onReactivate,
  onArchive,
  onDelete,
  onViewHealth
}: ProjectCardProps): ReactElement {
  return (
    <div className="rounded-lg border bg-card p-4 hover:border-primary/50 transition-colors">
      {/* Header */}
      <div className="flex items-start justify-between mb-3">
        <div className="flex items-center gap-3">
          <div className="p-2 rounded-lg bg-primary/10">
            <FolderKanban className="h-5 w-5 text-primary" />
          </div>
          <div>
            <h3 className="font-semibold">{project.name}</h3>
            <p className="text-xs text-muted-foreground">{project.slug}</p>
          </div>
        </div>

        {/* Actions Menu */}
        <div className="relative group">
          <Button variant="ghost" size="icon" className="h-8 w-8">
            <MoreVertical className="h-4 w-4" />
          </Button>
          <div className="absolute right-0 top-8 z-10 hidden group-focus-within:block min-w-[140px] rounded-md border bg-popover p-1 shadow-md">
            {onEdit && (
              <button
                onClick={onEdit}
                className="flex w-full items-center gap-2 rounded-sm px-2 py-1.5 text-sm hover:bg-accent"
              >
                <Pencil className="h-4 w-4" />
                Edit
              </button>
            )}
            {onViewHealth && (
              <button
                onClick={onViewHealth}
                className="flex w-full items-center gap-2 rounded-sm px-2 py-1.5 text-sm hover:bg-accent"
              >
                <Activity className="h-4 w-4" />
                View Health
              </button>
            )}
            <div className="my-1 h-px bg-border" />
            {project.status === 'Active' && onSuspend && (
              <button
                onClick={onSuspend}
                className="flex w-full items-center gap-2 rounded-sm px-2 py-1.5 text-sm hover:bg-accent text-warning"
              >
                <Pause className="h-4 w-4" />
                Suspend
              </button>
            )}
            {project.status === 'Suspended' && onReactivate && (
              <button
                onClick={onReactivate}
                className="flex w-full items-center gap-2 rounded-sm px-2 py-1.5 text-sm hover:bg-accent text-success"
              >
                <Play className="h-4 w-4" />
                Reactivate
              </button>
            )}
            {project.status !== 'Archived' && onArchive && (
              <button
                onClick={onArchive}
                className="flex w-full items-center gap-2 rounded-sm px-2 py-1.5 text-sm hover:bg-accent"
              >
                <Archive className="h-4 w-4" />
                Archive
              </button>
            )}
            {onDelete && (
              <button
                onClick={onDelete}
                className="flex w-full items-center gap-2 rounded-sm px-2 py-1.5 text-sm hover:bg-accent text-destructive"
              >
                <Trash2 className="h-4 w-4" />
                Delete
              </button>
            )}
          </div>
        </div>
      </div>

      {/* Status & Info */}
      <div className="flex items-center gap-4 text-sm">
        <div className="flex items-center gap-1.5">
          {getStatusIcon(project.status as ProjectStatus)}
          <span className={cn('text-xs', getStatusColor(project.status as ProjectStatus))}>
            {project.status}
          </span>
        </div>
        <span className="text-xs text-muted-foreground">
          Created {formatDate(project.createdAt)}
        </span>
      </div>

      {/* Schema */}
      <div className="mt-3 flex items-center gap-2 text-xs text-muted-foreground">
        <span className="px-2 py-0.5 rounded bg-muted">
          {project.dataSchema}
        </span>
      </div>

      {/* Settings Summary */}
      {project.settings && (
        <div className="mt-3 pt-3 border-t border-border text-xs text-muted-foreground">
          <div className="flex items-center gap-4">
            {project.settings.enableAuditLog && (
              <span className="flex items-center gap-1">
                <Activity className="h-3 w-3" />
                Audit On
              </span>
            )}
          </div>
        </div>
      )}
    </div>
  )
}
