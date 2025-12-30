import { type ReactElement } from 'react'
import { FolderKanban, Plus } from 'lucide-react'
import { Button } from '@/components/ui/Button'

export function ProjectsPage(): ReactElement {
  return (
    <div className="flex flex-col h-full bg-background">
      {/* Header */}
      <div className="flex h-10 items-center justify-between border-b border-border px-4">
        <div className="flex items-center">
          <FolderKanban className="h-4 w-4 mr-2" />
          <span className="font-medium text-sm">Projects</span>
        </div>
        <Button variant="ghost" size="sm" className="gap-1" disabled>
          <Plus className="h-4 w-4" />
          New Project
        </Button>
      </div>

      {/* Content */}
      <div className="flex-1 overflow-auto p-6">
        <div className="flex flex-col items-center justify-center h-full text-center">
          <FolderKanban className="h-16 w-16 text-muted-foreground/50 mb-4" />
          <h2 className="text-xl font-semibold text-foreground">Project Management</h2>
          <p className="mt-2 text-muted-foreground max-w-md">
            Full project management capabilities including Create, Edit, Archive, and Suspend
            will be available in Phase 1.4.
          </p>
          <div className="mt-6 p-4 rounded-lg border border-border bg-muted/50">
            <h3 className="text-sm font-medium mb-2">Planned Features:</h3>
            <ul className="text-sm text-muted-foreground text-left space-y-1">
              <li>• Create and configure new projects</li>
              <li>• Project dashboard with stats</li>
              <li>• Archive/Suspend/Reactivate projects</li>
              <li>• Project health validation</li>
            </ul>
          </div>
        </div>
      </div>
    </div>
  )
}
