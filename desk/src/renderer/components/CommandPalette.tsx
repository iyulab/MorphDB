import { useState, useEffect, useCallback, type ReactElement } from 'react'
import { Command } from 'cmdk'
import { useNavigate } from 'react-router-dom'
import {
  TableProperties,
  FolderKanban,
  Eye,
  Webhook,
  FileText,
  Shield,
  Settings,
  Plus,
  Search,
  Moon,
  Sun,
  RefreshCw
} from 'lucide-react'
import { useThemeStore } from '@/stores/themeStore'
import { useConnectionStore } from '@/stores/connectionStore'
import './CommandPalette.css'

interface CommandPaletteProps {
  open: boolean
  onOpenChange: (open: boolean) => void
}

export function CommandPalette({ open, onOpenChange }: CommandPaletteProps): ReactElement | null {
  const navigate = useNavigate()
  const [search, setSearch] = useState('')
  const { resolvedTheme, toggleTheme, setTheme } = useThemeStore()
  const { connections, activeConnectionId, setActiveConnection } = useConnectionStore()

  const runCommand = useCallback((command: () => void) => {
    onOpenChange(false)
    command()
  }, [onOpenChange])

  // Reset search when closing
  useEffect(() => {
    if (!open) {
      setSearch('')
    }
  }, [open])

  if (!open) return null

  return (
    <div className="command-palette-overlay" onClick={() => onOpenChange(false)}>
      <Command
        className="command-palette"
        onClick={(e) => e.stopPropagation()}
        loop
      >
        <div className="command-input-wrapper">
          <Search className="command-input-icon" />
          <Command.Input
            value={search}
            onValueChange={setSearch}
            placeholder="Type a command or search..."
            className="command-input"
            autoFocus
          />
          <kbd className="command-shortcut">ESC</kbd>
        </div>

        <Command.List className="command-list">
          <Command.Empty className="command-empty">No results found.</Command.Empty>

          {/* Navigation */}
          <Command.Group heading="Navigation" className="command-group">
            <Command.Item
              onSelect={() => runCommand(() => navigate('/explorer'))}
              className="command-item"
            >
              <TableProperties className="command-item-icon" />
              <span>Explorer</span>
            </Command.Item>
            <Command.Item
              onSelect={() => runCommand(() => navigate('/projects'))}
              className="command-item"
            >
              <FolderKanban className="command-item-icon" />
              <span>Projects</span>
            </Command.Item>
            <Command.Item
              onSelect={() => runCommand(() => navigate('/views'))}
              className="command-item"
            >
              <Eye className="command-item-icon" />
              <span>Views</span>
            </Command.Item>
            <Command.Item
              onSelect={() => runCommand(() => navigate('/webhooks'))}
              className="command-item"
            >
              <Webhook className="command-item-icon" />
              <span>Webhooks</span>
            </Command.Item>
            <Command.Item
              onSelect={() => runCommand(() => navigate('/audit'))}
              className="command-item"
            >
              <FileText className="command-item-icon" />
              <span>Audit Logs</span>
            </Command.Item>
            <Command.Item
              onSelect={() => runCommand(() => navigate('/security'))}
              className="command-item"
            >
              <Shield className="command-item-icon" />
              <span>Security</span>
            </Command.Item>
            <Command.Item
              onSelect={() => runCommand(() => navigate('/settings'))}
              className="command-item"
            >
              <Settings className="command-item-icon" />
              <span>Settings</span>
            </Command.Item>
          </Command.Group>

          {/* Theme */}
          <Command.Group heading="Theme" className="command-group">
            <Command.Item
              onSelect={() => runCommand(toggleTheme)}
              className="command-item"
            >
              {resolvedTheme === 'dark' ? (
                <Sun className="command-item-icon" />
              ) : (
                <Moon className="command-item-icon" />
              )}
              <span>Toggle theme</span>
              <span className="command-item-hint">
                {resolvedTheme === 'dark' ? 'Switch to light' : 'Switch to dark'}
              </span>
            </Command.Item>
            <Command.Item
              onSelect={() => runCommand(() => setTheme('light'))}
              className="command-item"
            >
              <Sun className="command-item-icon" />
              <span>Light theme</span>
            </Command.Item>
            <Command.Item
              onSelect={() => runCommand(() => setTheme('dark'))}
              className="command-item"
            >
              <Moon className="command-item-icon" />
              <span>Dark theme</span>
            </Command.Item>
            <Command.Item
              onSelect={() => runCommand(() => setTheme('system'))}
              className="command-item"
            >
              <Settings className="command-item-icon" />
              <span>System theme</span>
            </Command.Item>
          </Command.Group>

          {/* Connections */}
          {connections.length > 0 && (
            <Command.Group heading="Connections" className="command-group">
              {connections.map((conn) => (
                <Command.Item
                  key={conn.id}
                  onSelect={() => runCommand(() => setActiveConnection(conn.id))}
                  className="command-item"
                >
                  <RefreshCw className="command-item-icon" />
                  <span>{conn.name}</span>
                  {activeConnectionId === conn.id && (
                    <span className="command-item-badge">Active</span>
                  )}
                </Command.Item>
              ))}
            </Command.Group>
          )}

          {/* Quick Actions */}
          <Command.Group heading="Quick Actions" className="command-group">
            <Command.Item
              onSelect={() => runCommand(() => window.location.reload())}
              className="command-item"
            >
              <RefreshCw className="command-item-icon" />
              <span>Reload window</span>
            </Command.Item>
          </Command.Group>
        </Command.List>
      </Command>
    </div>
  )
}

export function useCommandPalette(): {
  open: boolean
  setOpen: (open: boolean) => void
} {
  const [open, setOpen] = useState(false)

  useEffect(() => {
    const down = (e: KeyboardEvent): void => {
      if (e.key === 'k' && (e.metaKey || e.ctrlKey)) {
        e.preventDefault()
        setOpen((prev) => !prev)
      }
      if (e.key === 'Escape' && open) {
        e.preventDefault()
        setOpen(false)
      }
    }

    document.addEventListener('keydown', down)
    return () => document.removeEventListener('keydown', down)
  }, [open])

  return { open, setOpen }
}
