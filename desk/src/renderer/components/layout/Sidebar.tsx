import { useState, useRef, useEffect, type ReactElement } from 'react'
import { NavLink } from 'react-router-dom'
import {
  Database,
  Plus,
  Server,
  Settings,
  MoreVertical,
  Pencil,
  Trash2,
  RefreshCw,
  Unplug,
  Loader2,
  FolderKanban,
  TableProperties,
  Eye,
  Webhook,
  Building2
} from 'lucide-react'
import { Button } from '@/components/ui/Button'
import { useConnectionStore } from '@/stores/connectionStore'
import type { Connection } from '@/types/connection'
import { cn } from '@/lib/utils'

interface SidebarProps {
  onNewConnection: () => void
  onEditConnection: (connection: Connection) => void
}

interface ContextMenuState {
  open: boolean
  connectionId: string | null
  x: number
  y: number
}

function ConnectionStatusIndicator({ status }: { status: Connection['status'] }): ReactElement {
  const statusStyles = {
    disconnected: 'bg-muted-foreground',
    connecting: 'bg-warning animate-pulse',
    connected: 'bg-success',
    error: 'bg-destructive'
  }

  return (
    <span
      className={cn('h-2 w-2 rounded-full flex-shrink-0', statusStyles[status])}
      title={status.charAt(0).toUpperCase() + status.slice(1)}
    />
  )
}

export function Sidebar({ onNewConnection, onEditConnection }: SidebarProps): ReactElement {
  const {
    activeConnectionId,
    setActiveConnection,
    getRecentConnections,
    removeConnection,
    testConnection,
    connectToServer,
    disconnectFromServer
  } = useConnectionStore()

  const [contextMenu, setContextMenu] = useState<ContextMenuState>({
    open: false,
    connectionId: null,
    x: 0,
    y: 0
  })
  const [actionLoading, setActionLoading] = useState<string | null>(null)
  const [confirmDelete, setConfirmDelete] = useState<string | null>(null)

  const contextMenuRef = useRef<HTMLDivElement>(null)

  // Sort connections by recent usage
  const connections = getRecentConnections()

  // Close context menu when clicking outside
  useEffect(() => {
    const handleClickOutside = (e: MouseEvent): void => {
      if (contextMenuRef.current && !contextMenuRef.current.contains(e.target as Node)) {
        setContextMenu({ open: false, connectionId: null, x: 0, y: 0 })
        setConfirmDelete(null)
      }
    }

    document.addEventListener('mousedown', handleClickOutside)
    return () => document.removeEventListener('mousedown', handleClickOutside)
  }, [])

  const handleContextMenu = (e: React.MouseEvent, connectionId: string): void => {
    e.preventDefault()
    e.stopPropagation()
    setContextMenu({
      open: true,
      connectionId,
      x: e.clientX,
      y: e.clientY
    })
    setConfirmDelete(null)
  }

  const handleMenuButtonClick = (e: React.MouseEvent, connectionId: string): void => {
    e.stopPropagation()
    const rect = (e.target as HTMLElement).getBoundingClientRect()
    setContextMenu({
      open: true,
      connectionId,
      x: rect.right,
      y: rect.top
    })
    setConfirmDelete(null)
  }

  const closeContextMenu = (): void => {
    setContextMenu({ open: false, connectionId: null, x: 0, y: 0 })
    setConfirmDelete(null)
  }

  const handleEdit = (): void => {
    const connection = connections.find((c) => c.id === contextMenu.connectionId)
    if (connection) {
      onEditConnection(connection)
    }
    closeContextMenu()
  }

  const handleDelete = async (): Promise<void> => {
    if (!contextMenu.connectionId) return

    if (confirmDelete !== contextMenu.connectionId) {
      setConfirmDelete(contextMenu.connectionId)
      return
    }

    setActionLoading(contextMenu.connectionId)
    await removeConnection(contextMenu.connectionId)
    setActionLoading(null)
    closeContextMenu()
  }

  const handleTest = async (): Promise<void> => {
    if (!contextMenu.connectionId) return
    setActionLoading(contextMenu.connectionId)
    await testConnection(contextMenu.connectionId)
    setActionLoading(null)
    closeContextMenu()
  }

  const handleConnect = async (): Promise<void> => {
    if (!contextMenu.connectionId) return
    setActionLoading(contextMenu.connectionId)
    await connectToServer(contextMenu.connectionId)
    setActionLoading(null)
    closeContextMenu()
  }

  const handleDisconnect = (): void => {
    if (!contextMenu.connectionId) return
    disconnectFromServer(contextMenu.connectionId)
    closeContextMenu()
  }

  const getConnectionForMenu = (): Connection | undefined => {
    return connections.find((c) => c.id === contextMenu.connectionId)
  }

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
              <div
                key={conn.id}
                className={cn(
                  'group flex w-full items-center gap-2 rounded-md px-2 py-1.5 text-left text-sm',
                  'hover:bg-sidebar-hover transition-colors',
                  activeConnectionId === conn.id && 'bg-sidebar-active text-primary'
                )}
                onContextMenu={(e) => handleContextMenu(e, conn.id)}
              >
                <button
                  onClick={() => setActiveConnection(conn.id)}
                  className="flex flex-1 items-center gap-2 min-w-0"
                >
                  <ConnectionStatusIndicator status={conn.status} />
                  <Server className="h-4 w-4 flex-shrink-0" />
                  <span className="truncate">{conn.name}</span>
                </button>
                <Button
                  variant="ghost"
                  size="icon"
                  className="h-6 w-6 opacity-0 group-hover:opacity-100 transition-opacity flex-shrink-0"
                  onClick={(e) => handleMenuButtonClick(e, conn.id)}
                >
                  <MoreVertical className="h-3 w-3" />
                </Button>
              </div>
            ))
          )}
        </div>
      </div>

      {/* Navigation */}
      <div className="border-t border-sidebar-border p-2 space-y-1">
        <NavLink
          to="/explorer"
          className={({ isActive }) =>
            cn(
              'flex w-full items-center gap-2 rounded-md px-2 py-1.5 text-sm transition-colors',
              'hover:bg-sidebar-hover',
              isActive && 'bg-sidebar-active text-primary'
            )
          }
        >
          <TableProperties className="h-4 w-4" />
          Explorer
        </NavLink>
        <NavLink
          to="/projects"
          className={({ isActive }) =>
            cn(
              'flex w-full items-center gap-2 rounded-md px-2 py-1.5 text-sm transition-colors',
              'hover:bg-sidebar-hover',
              isActive && 'bg-sidebar-active text-primary'
            )
          }
        >
          <FolderKanban className="h-4 w-4" />
          Projects
        </NavLink>
        <NavLink
          to="/views"
          className={({ isActive }) =>
            cn(
              'flex w-full items-center gap-2 rounded-md px-2 py-1.5 text-sm transition-colors',
              'hover:bg-sidebar-hover',
              isActive && 'bg-sidebar-active text-primary'
            )
          }
        >
          <Eye className="h-4 w-4" />
          Views
        </NavLink>
        <NavLink
          to="/webhooks"
          className={({ isActive }) =>
            cn(
              'flex w-full items-center gap-2 rounded-md px-2 py-1.5 text-sm transition-colors',
              'hover:bg-sidebar-hover',
              isActive && 'bg-sidebar-active text-primary'
            )
          }
        >
          <Webhook className="h-4 w-4" />
          Webhooks
        </NavLink>
        <NavLink
          to="/organizations"
          className={({ isActive }) =>
            cn(
              'flex w-full items-center gap-2 rounded-md px-2 py-1.5 text-sm transition-colors',
              'hover:bg-sidebar-hover',
              isActive && 'bg-sidebar-active text-primary'
            )
          }
        >
          <Building2 className="h-4 w-4" />
          Organizations
        </NavLink>
        <NavLink
          to="/settings"
          className={({ isActive }) =>
            cn(
              'flex w-full items-center gap-2 rounded-md px-2 py-1.5 text-sm transition-colors',
              'hover:bg-sidebar-hover',
              isActive && 'bg-sidebar-active text-primary'
            )
          }
        >
          <Settings className="h-4 w-4" />
          Settings
        </NavLink>
      </div>

      {/* Context Menu */}
      {contextMenu.open && (
        <div
          ref={contextMenuRef}
          className="fixed z-50 min-w-[160px] rounded-md border bg-popover p-1 shadow-md"
          style={{
            left: Math.min(contextMenu.x, window.innerWidth - 180),
            top: Math.min(contextMenu.y, window.innerHeight - 200)
          }}
        >
          {actionLoading === contextMenu.connectionId ? (
            <div className="flex items-center justify-center py-4">
              <Loader2 className="h-4 w-4 animate-spin" />
            </div>
          ) : (
            <>
              {getConnectionForMenu()?.status === 'connected' ? (
                <button
                  onClick={handleDisconnect}
                  className="flex w-full items-center gap-2 rounded-sm px-2 py-1.5 text-sm hover:bg-accent"
                >
                  <Unplug className="h-4 w-4" />
                  Disconnect
                </button>
              ) : (
                <button
                  onClick={handleConnect}
                  className="flex w-full items-center gap-2 rounded-sm px-2 py-1.5 text-sm hover:bg-accent"
                >
                  <RefreshCw className="h-4 w-4" />
                  Connect
                </button>
              )}
              <button
                onClick={handleTest}
                className="flex w-full items-center gap-2 rounded-sm px-2 py-1.5 text-sm hover:bg-accent"
              >
                <RefreshCw className="h-4 w-4" />
                Test Connection
              </button>
              <div className="my-1 h-px bg-border" />
              <button
                onClick={handleEdit}
                className="flex w-full items-center gap-2 rounded-sm px-2 py-1.5 text-sm hover:bg-accent"
              >
                <Pencil className="h-4 w-4" />
                Edit
              </button>
              <button
                onClick={handleDelete}
                className={cn(
                  'flex w-full items-center gap-2 rounded-sm px-2 py-1.5 text-sm hover:bg-accent',
                  confirmDelete === contextMenu.connectionId && 'text-destructive hover:bg-destructive/10'
                )}
              >
                <Trash2 className="h-4 w-4" />
                {confirmDelete === contextMenu.connectionId ? 'Click again to confirm' : 'Delete'}
              </button>
            </>
          )}
        </div>
      )}
    </aside>
  )
}
