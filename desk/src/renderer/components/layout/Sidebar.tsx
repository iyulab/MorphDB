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
  Building2,
  HardDrive,
  FileText,
  Gauge,
  Shield,
  KeyRound,
  PanelLeftClose,
  PanelLeft
} from 'lucide-react'
import { Button } from '@/components/ui/Button'
import { ThemeToggleButton } from '@/components/ui/ThemeToggle'
import { Tooltip, TooltipContent, TooltipProvider, TooltipTrigger } from '@/components/ui/Tooltip'
import { useConnectionStore } from '@/stores/connectionStore'
import { useLayoutStore } from '@/stores/layoutStore'
import type { Connection } from '@/types/connection'
import { cn } from '@/lib/utils'

interface SidebarProps {
  onNewConnection: () => void
  onEditConnection: (connection: Connection) => void
}

interface NavItemProps {
  to: string
  icon: React.ReactNode
  label: string
  collapsed: boolean
}

function NavItem({ to, icon, label, collapsed }: NavItemProps): ReactElement {
  const content = (
    <NavLink
      to={to}
      className={({ isActive }) =>
        cn(
          'flex w-full items-center gap-2 rounded-md px-2 py-1.5 text-sm transition-colors',
          'hover:bg-sidebar-hover',
          isActive && 'bg-sidebar-active text-primary',
          collapsed && 'justify-center px-0'
        )
      }
    >
      {icon}
      {!collapsed && label}
    </NavLink>
  )

  if (collapsed) {
    return (
      <Tooltip>
        <TooltipTrigger asChild>{content}</TooltipTrigger>
        <TooltipContent side="right">{label}</TooltipContent>
      </Tooltip>
    )
  }

  return content
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

  const { sidebarCollapsed, toggleSidebar } = useLayoutStore()

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
    <TooltipProvider delayDuration={0}>
      <aside
        className={cn(
          'flex h-full flex-col border-r border-sidebar-border bg-sidebar transition-all duration-200',
          sidebarCollapsed ? 'w-14' : 'w-56'
        )}
      >
        {/* Header */}
        <div className="flex h-12 items-center justify-between border-b border-sidebar-border px-3">
          <div className="flex items-center gap-2">
            <Database className="h-5 w-5 text-primary flex-shrink-0" />
            {!sidebarCollapsed && <span className="font-semibold text-sm">MorphDB</span>}
          </div>
          <div className="flex items-center gap-1">
            {!sidebarCollapsed && <ThemeToggleButton />}
            <Button
              variant="ghost"
              size="icon"
              className="h-6 w-6"
              onClick={toggleSidebar}
              title={sidebarCollapsed ? 'Expand sidebar' : 'Collapse sidebar'}
            >
              {sidebarCollapsed ? (
                <PanelLeft className="h-4 w-4" />
              ) : (
                <PanelLeftClose className="h-4 w-4" />
              )}
            </Button>
          </div>
        </div>

      {/* Connections */}
      <div className="flex-1 overflow-y-auto p-2">
        {!sidebarCollapsed && (
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
        )}

        {sidebarCollapsed && (
          <div className="mb-2 flex justify-center">
            <Tooltip>
              <TooltipTrigger asChild>
                <Button
                  variant="ghost"
                  size="icon"
                  className="h-8 w-8"
                  onClick={onNewConnection}
                  title="New Connection"
                >
                  <Plus className="h-4 w-4" />
                </Button>
              </TooltipTrigger>
              <TooltipContent side="right">New Connection</TooltipContent>
            </Tooltip>
          </div>
        )}

        <div className="space-y-1">
          {connections.length === 0 ? (
            !sidebarCollapsed && (
              <div className="px-2 py-4 text-center text-sm text-muted-foreground">
                No connections yet.
                <br />
                Click + to add one.
              </div>
            )
          ) : (
            connections.map((conn) => (
              <Tooltip key={conn.id}>
                <TooltipTrigger asChild>
                  <div
                    className={cn(
                      'group flex w-full items-center gap-2 rounded-md px-2 py-1.5 text-left text-sm',
                      'hover:bg-sidebar-hover transition-colors',
                      activeConnectionId === conn.id && 'bg-sidebar-active text-primary',
                      sidebarCollapsed && 'justify-center px-0'
                    )}
                    onContextMenu={(e) => handleContextMenu(e, conn.id)}
                  >
                    <button
                      onClick={() => setActiveConnection(conn.id)}
                      className={cn(
                        'flex flex-1 items-center gap-2 min-w-0',
                        sidebarCollapsed && 'flex-none justify-center'
                      )}
                    >
                      <ConnectionStatusIndicator status={conn.status} />
                      {!sidebarCollapsed && (
                        <>
                          <Server className="h-4 w-4 flex-shrink-0" />
                          <span className="truncate">{conn.name}</span>
                        </>
                      )}
                    </button>
                    {!sidebarCollapsed && (
                      <Button
                        variant="ghost"
                        size="icon"
                        className="h-6 w-6 opacity-0 group-hover:opacity-100 transition-opacity flex-shrink-0"
                        onClick={(e) => handleMenuButtonClick(e, conn.id)}
                      >
                        <MoreVertical className="h-3 w-3" />
                      </Button>
                    )}
                  </div>
                </TooltipTrigger>
                {sidebarCollapsed && (
                  <TooltipContent side="right">{conn.name}</TooltipContent>
                )}
              </Tooltip>
            ))
          )}
        </div>
      </div>

      {/* Navigation */}
      <div className="border-t border-sidebar-border p-2 space-y-1">
        <NavItem to="/explorer" icon={<TableProperties className="h-4 w-4 flex-shrink-0" />} label="Explorer" collapsed={sidebarCollapsed} />
        <NavItem to="/projects" icon={<FolderKanban className="h-4 w-4 flex-shrink-0" />} label="Projects" collapsed={sidebarCollapsed} />
        <NavItem to="/views" icon={<Eye className="h-4 w-4 flex-shrink-0" />} label="Views" collapsed={sidebarCollapsed} />
        <NavItem to="/webhooks" icon={<Webhook className="h-4 w-4 flex-shrink-0" />} label="Webhooks" collapsed={sidebarCollapsed} />
        <NavItem to="/organizations" icon={<Building2 className="h-4 w-4 flex-shrink-0" />} label="Organizations" collapsed={sidebarCollapsed} />
        <NavItem to="/backups" icon={<HardDrive className="h-4 w-4 flex-shrink-0" />} label="Backups" collapsed={sidebarCollapsed} />
        <NavItem to="/audit" icon={<FileText className="h-4 w-4 flex-shrink-0" />} label="Audit Logs" collapsed={sidebarCollapsed} />
        <NavItem to="/quota" icon={<Gauge className="h-4 w-4 flex-shrink-0" />} label="Usage & Quota" collapsed={sidebarCollapsed} />
        <NavItem to="/security" icon={<Shield className="h-4 w-4 flex-shrink-0" />} label="Security" collapsed={sidebarCollapsed} />
        <NavItem to="/sso" icon={<KeyRound className="h-4 w-4 flex-shrink-0" />} label="SSO" collapsed={sidebarCollapsed} />
        <NavItem to="/settings" icon={<Settings className="h-4 w-4 flex-shrink-0" />} label="Settings" collapsed={sidebarCollapsed} />
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
    </TooltipProvider>
  )
}
