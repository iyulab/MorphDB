import { Database, Plus, Server, Settings } from 'lucide-react'
import { Button } from '@/components/ui/Button'
import { useConnectionStore } from '@/stores/connectionStore'
import { cn } from '@/lib/utils'

interface SidebarProps {
  onNewConnection: () => void
}

export function Sidebar({ onNewConnection }: SidebarProps): JSX.Element {
  const { connections, activeConnectionId, setActiveConnection } = useConnectionStore()

  return (
    <aside className="flex h-full w-56 flex-col border-r border-sidebar-border bg-sidebar">
      {/* Header */}
      <div className="flex h-12 items-center justify-between border-b border-sidebar-border px-3">
        <div className="flex items-center gap-2">
          <Database className="h-5 w-5 text-primary" />
          <span className="font-semibold text-sm">MorphDB</span>
        </div>
      </div>

      {/* Connections */}
      <div className="flex-1 overflow-y-auto p-2">
        <div className="mb-2 flex items-center justify-between px-2">
          <span className="text-xs font-medium text-muted-foreground uppercase tracking-wider">
            Connections
          </span>
          <Button
            variant="ghost"
            size="icon"
            className="h-6 w-6"
            onClick={onNewConnection}
            title="New Connection"
          >
            <Plus className="h-4 w-4" />
          </Button>
        </div>

        <div className="space-y-1">
          {connections.length === 0 ? (
            <div className="px-2 py-4 text-center text-sm text-muted-foreground">
              No connections yet.
              <br />
              Click + to add one.
            </div>
          ) : (
            connections.map((conn) => (
              <button
                key={conn.id}
                onClick={() => setActiveConnection(conn.id)}
                className={cn(
                  'flex w-full items-center gap-2 rounded-md px-2 py-1.5 text-left text-sm',
                  'hover:bg-sidebar-hover transition-colors',
                  activeConnectionId === conn.id && 'bg-sidebar-active text-primary'
                )}
              >
                <Server className="h-4 w-4 flex-shrink-0" />
                <span className="truncate">{conn.name}</span>
              </button>
            ))
          )}
        </div>
      </div>

      {/* Footer */}
      <div className="border-t border-sidebar-border p-2">
        <Button
          variant="ghost"
          size="sm"
          className="w-full justify-start gap-2"
          onClick={() => window.api.onMenuSettings(() => {})}
        >
          <Settings className="h-4 w-4" />
          Settings
        </Button>
      </div>
    </aside>
  )
}
