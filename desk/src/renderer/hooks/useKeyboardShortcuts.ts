import { useEffect, useCallback } from 'react'
import { useNavigate } from 'react-router-dom'

export interface KeyboardShortcut {
  key: string
  ctrl?: boolean
  meta?: boolean
  shift?: boolean
  alt?: boolean
  description: string
  action: () => void
}

const isMac = typeof navigator !== 'undefined' && navigator.platform.toUpperCase().indexOf('MAC') >= 0

function matchesShortcut(e: KeyboardEvent, shortcut: KeyboardShortcut): boolean {
  const ctrlOrMeta = shortcut.ctrl || shortcut.meta
  const hasModifier = isMac ? e.metaKey : e.ctrlKey

  if (ctrlOrMeta && !hasModifier) return false
  if (shortcut.shift && !e.shiftKey) return false
  if (shortcut.alt && !e.altKey) return false

  return e.key.toLowerCase() === shortcut.key.toLowerCase()
}

export function useKeyboardShortcuts(shortcuts: KeyboardShortcut[]): void {
  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent): void => {
      // Skip if user is typing in an input
      const target = e.target as HTMLElement
      if (
        target.tagName === 'INPUT' ||
        target.tagName === 'TEXTAREA' ||
        target.isContentEditable
      ) {
        return
      }

      for (const shortcut of shortcuts) {
        if (matchesShortcut(e, shortcut)) {
          e.preventDefault()
          shortcut.action()
          return
        }
      }
    }

    document.addEventListener('keydown', handleKeyDown)
    return () => document.removeEventListener('keydown', handleKeyDown)
  }, [shortcuts])
}

export function useGlobalShortcuts(callbacks: {
  onNewConnection?: () => void
  onToggleTheme?: () => void
  onReload?: () => void
}): void {
  const navigate = useNavigate()

  const shortcuts: KeyboardShortcut[] = [
    // Navigation shortcuts (Cmd/Ctrl + number)
    {
      key: '1',
      ctrl: true,
      description: 'Go to Explorer',
      action: () => navigate('/explorer')
    },
    {
      key: '2',
      ctrl: true,
      description: 'Go to Projects',
      action: () => navigate('/projects')
    },
    {
      key: '3',
      ctrl: true,
      description: 'Go to Views',
      action: () => navigate('/views')
    },
    {
      key: '4',
      ctrl: true,
      description: 'Go to Webhooks',
      action: () => navigate('/webhooks')
    },
    {
      key: '5',
      ctrl: true,
      description: 'Go to Organizations',
      action: () => navigate('/organizations')
    },
    {
      key: '6',
      ctrl: true,
      description: 'Go to Backups',
      action: () => navigate('/backups')
    },
    {
      key: '7',
      ctrl: true,
      description: 'Go to Audit Logs',
      action: () => navigate('/audit')
    },
    {
      key: '8',
      ctrl: true,
      description: 'Go to Settings',
      action: () => navigate('/settings')
    }
  ]

  // Add optional callbacks
  if (callbacks.onNewConnection) {
    shortcuts.push({
      key: 'n',
      ctrl: true,
      shift: true,
      description: 'New Connection',
      action: callbacks.onNewConnection
    })
  }

  if (callbacks.onToggleTheme) {
    shortcuts.push({
      key: 't',
      ctrl: true,
      shift: true,
      description: 'Toggle Theme',
      action: callbacks.onToggleTheme
    })
  }

  if (callbacks.onReload) {
    shortcuts.push({
      key: 'r',
      ctrl: true,
      shift: true,
      description: 'Reload Window',
      action: callbacks.onReload
    })
  }

  useKeyboardShortcuts(shortcuts)
}

// Get a formatted key string for display
export function formatShortcut(shortcut: KeyboardShortcut): string {
  const parts: string[] = []

  if (shortcut.ctrl || shortcut.meta) {
    parts.push(isMac ? '⌘' : 'Ctrl')
  }
  if (shortcut.shift) {
    parts.push(isMac ? '⇧' : 'Shift')
  }
  if (shortcut.alt) {
    parts.push(isMac ? '⌥' : 'Alt')
  }

  parts.push(shortcut.key.toUpperCase())

  return parts.join(isMac ? '' : '+')
}

// Get all available shortcuts for display
export function getShortcutsList(): Array<{ category: string; shortcuts: Array<{ key: string; description: string }> }> {
  const mod = isMac ? '⌘' : 'Ctrl'
  const shift = isMac ? '⇧' : 'Shift'

  return [
    {
      category: 'Navigation',
      shortcuts: [
        { key: `${mod}+1`, description: 'Go to Explorer' },
        { key: `${mod}+2`, description: 'Go to Projects' },
        { key: `${mod}+3`, description: 'Go to Views' },
        { key: `${mod}+4`, description: 'Go to Webhooks' },
        { key: `${mod}+5`, description: 'Go to Organizations' },
        { key: `${mod}+6`, description: 'Go to Backups' },
        { key: `${mod}+7`, description: 'Go to Audit Logs' },
        { key: `${mod}+8`, description: 'Go to Settings' }
      ]
    },
    {
      category: 'Actions',
      shortcuts: [
        { key: `${mod}+K`, description: 'Open Command Palette' },
        { key: `${mod}+${shift}+N`, description: 'New Connection' },
        { key: `${mod}+${shift}+T`, description: 'Toggle Theme' },
        { key: `${mod}+${shift}+R`, description: 'Reload Window' }
      ]
    },
    {
      category: 'General',
      shortcuts: [
        { key: 'Esc', description: 'Close dialogs' }
      ]
    }
  ]
}
