import { describe, it, expect, beforeEach, vi } from 'vitest'
import { useThemeStore } from './themeStore'

describe('themeStore', () => {
  beforeEach(() => {
    // Reset store state
    useThemeStore.setState({
      theme: 'system',
      resolvedTheme: 'dark'
    })
    // Clear classList
    document.documentElement.classList.remove('dark')
  })

  describe('initial state', () => {
    it('should have default theme as system', () => {
      const { theme } = useThemeStore.getState()
      expect(theme).toBe('system')
    })
  })

  describe('setTheme', () => {
    it('should set theme to light', () => {
      const { setTheme } = useThemeStore.getState()
      setTheme('light')

      const state = useThemeStore.getState()
      expect(state.theme).toBe('light')
      expect(state.resolvedTheme).toBe('light')
      expect(document.documentElement.classList.contains('dark')).toBe(false)
    })

    it('should set theme to dark', () => {
      const { setTheme } = useThemeStore.getState()
      setTheme('dark')

      const state = useThemeStore.getState()
      expect(state.theme).toBe('dark')
      expect(state.resolvedTheme).toBe('dark')
      expect(document.documentElement.classList.contains('dark')).toBe(true)
    })

    it('should resolve system theme based on media query', () => {
      const { setTheme } = useThemeStore.getState()
      setTheme('system')

      const state = useThemeStore.getState()
      expect(state.theme).toBe('system')
      // Media query mock returns dark for prefers-color-scheme: dark
      expect(state.resolvedTheme).toBe('dark')
    })
  })

  describe('toggleTheme', () => {
    it('should toggle from dark to light', () => {
      useThemeStore.setState({ theme: 'dark', resolvedTheme: 'dark' })
      document.documentElement.classList.add('dark')

      const { toggleTheme } = useThemeStore.getState()
      toggleTheme()

      const state = useThemeStore.getState()
      expect(state.theme).toBe('light')
      expect(state.resolvedTheme).toBe('light')
      expect(document.documentElement.classList.contains('dark')).toBe(false)
    })

    it('should toggle from light to dark', () => {
      useThemeStore.setState({ theme: 'light', resolvedTheme: 'light' })

      const { toggleTheme } = useThemeStore.getState()
      toggleTheme()

      const state = useThemeStore.getState()
      expect(state.theme).toBe('dark')
      expect(state.resolvedTheme).toBe('dark')
      expect(document.documentElement.classList.contains('dark')).toBe(true)
    })
  })
})
