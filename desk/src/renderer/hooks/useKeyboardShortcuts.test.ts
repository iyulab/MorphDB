import { describe, it, expect } from 'vitest'
import { getShortcutsList, formatShortcut, type KeyboardShortcut } from './useKeyboardShortcuts'

describe('useKeyboardShortcuts', () => {
  describe('getShortcutsList', () => {
    it('should return categorized shortcuts', () => {
      const shortcuts = getShortcutsList()

      expect(shortcuts).toBeInstanceOf(Array)
      expect(shortcuts.length).toBeGreaterThan(0)

      // Check categories exist
      const categories = shortcuts.map(s => s.category)
      expect(categories).toContain('Navigation')
      expect(categories).toContain('Actions')
      expect(categories).toContain('General')
    })

    it('should have shortcuts in Navigation category', () => {
      const shortcuts = getShortcutsList()
      const navigation = shortcuts.find(s => s.category === 'Navigation')

      expect(navigation).toBeDefined()
      expect(navigation!.shortcuts.length).toBeGreaterThan(0)

      // Check specific navigation shortcuts
      const descriptions = navigation!.shortcuts.map(s => s.description)
      expect(descriptions).toContain('Go to Explorer')
      expect(descriptions).toContain('Go to Projects')
      expect(descriptions).toContain('Go to Settings')
    })

    it('should have shortcuts in Actions category', () => {
      const shortcuts = getShortcutsList()
      const actions = shortcuts.find(s => s.category === 'Actions')

      expect(actions).toBeDefined()
      expect(actions!.shortcuts.length).toBeGreaterThan(0)

      const descriptions = actions!.shortcuts.map(s => s.description)
      expect(descriptions).toContain('Open Command Palette')
      expect(descriptions).toContain('New Connection')
      expect(descriptions).toContain('Toggle Theme')
    })
  })

  describe('formatShortcut', () => {
    it('should format ctrl shortcut correctly on Windows', () => {
      // Navigator.platform is mocked as 'Win32'
      const shortcut: KeyboardShortcut = {
        key: 'k',
        ctrl: true,
        description: 'Test',
        action: () => {}
      }

      const formatted = formatShortcut(shortcut)
      expect(formatted).toBe('Ctrl+K')
    })

    it('should format shift+ctrl shortcut correctly', () => {
      const shortcut: KeyboardShortcut = {
        key: 'n',
        ctrl: true,
        shift: true,
        description: 'Test',
        action: () => {}
      }

      const formatted = formatShortcut(shortcut)
      expect(formatted).toBe('Ctrl+Shift+N')
    })

    it('should format alt shortcut correctly', () => {
      const shortcut: KeyboardShortcut = {
        key: 'x',
        alt: true,
        description: 'Test',
        action: () => {}
      }

      const formatted = formatShortcut(shortcut)
      expect(formatted).toBe('Alt+X')
    })
  })
})
