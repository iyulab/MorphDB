import { useState, useEffect, type ReactElement } from 'react'
import { Keyboard, X } from 'lucide-react'
import { getShortcutsList } from '@/hooks/useKeyboardShortcuts'
import './KeyboardShortcutsHelp.css'

interface KeyboardShortcutsHelpProps {
  open: boolean
  onOpenChange: (open: boolean) => void
}

export function KeyboardShortcutsHelp({ open, onOpenChange }: KeyboardShortcutsHelpProps): ReactElement | null {
  const shortcuts = getShortcutsList()

  useEffect(() => {
    if (!open) return

    const handleKeyDown = (e: KeyboardEvent): void => {
      if (e.key === 'Escape') {
        e.preventDefault()
        onOpenChange(false)
      }
    }

    document.addEventListener('keydown', handleKeyDown)
    return () => document.removeEventListener('keydown', handleKeyDown)
  }, [open, onOpenChange])

  if (!open) return null

  return (
    <div className="shortcuts-overlay" onClick={() => onOpenChange(false)}>
      <div className="shortcuts-dialog" onClick={(e) => e.stopPropagation()}>
        <div className="shortcuts-header">
          <div className="shortcuts-title">
            <Keyboard className="shortcuts-title-icon" />
            <span>Keyboard Shortcuts</span>
          </div>
          <button
            className="shortcuts-close"
            onClick={() => onOpenChange(false)}
            aria-label="Close"
          >
            <X className="h-4 w-4" />
          </button>
        </div>

        <div className="shortcuts-content">
          {shortcuts.map((category) => (
            <div key={category.category} className="shortcuts-category">
              <h3 className="shortcuts-category-title">{category.category}</h3>
              <div className="shortcuts-list">
                {category.shortcuts.map((shortcut) => (
                  <div key={shortcut.key} className="shortcuts-item">
                    <span className="shortcuts-description">{shortcut.description}</span>
                    <kbd className="shortcuts-key">{shortcut.key}</kbd>
                  </div>
                ))}
              </div>
            </div>
          ))}
        </div>

        <div className="shortcuts-footer">
          <span className="shortcuts-hint">
            Press <kbd>?</kbd> to toggle this help
          </span>
        </div>
      </div>
    </div>
  )
}

export function useKeyboardShortcutsHelp(): {
  open: boolean
  setOpen: (open: boolean) => void
} {
  const [open, setOpen] = useState(false)

  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent): void => {
      // Only trigger on '?' key when not in input
      const target = e.target as HTMLElement
      if (
        target.tagName === 'INPUT' ||
        target.tagName === 'TEXTAREA' ||
        target.isContentEditable
      ) {
        return
      }

      if (e.key === '?' || (e.shiftKey && e.key === '/')) {
        e.preventDefault()
        setOpen((prev) => !prev)
      }
    }

    document.addEventListener('keydown', handleKeyDown)
    return () => document.removeEventListener('keydown', handleKeyDown)
  }, [])

  return { open, setOpen }
}
