import { create } from 'zustand'
import { persist } from 'zustand/middleware'

export type Theme = 'light' | 'dark' | 'system'

interface ThemeState {
  theme: Theme
  resolvedTheme: 'light' | 'dark'

  // Actions
  setTheme: (theme: Theme) => void
  toggleTheme: () => void
}

function getSystemTheme(): 'light' | 'dark' {
  if (typeof window !== 'undefined' && window.matchMedia) {
    return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light'
  }
  return 'dark'
}

function applyTheme(theme: 'light' | 'dark'): void {
  const root = document.documentElement
  if (theme === 'dark') {
    root.classList.add('dark')
  } else {
    root.classList.remove('dark')
  }
}

export const useThemeStore = create<ThemeState>()(
  persist(
    (set, get) => ({
      theme: 'system',
      resolvedTheme: getSystemTheme(),

      setTheme: (theme) => {
        const resolvedTheme = theme === 'system' ? getSystemTheme() : theme
        applyTheme(resolvedTheme)
        set({ theme, resolvedTheme })
      },

      toggleTheme: () => {
        const { resolvedTheme: currentTheme } = get()
        const newTheme: 'light' | 'dark' = currentTheme === 'dark' ? 'light' : 'dark'
        applyTheme(newTheme)
        set({ theme: newTheme, resolvedTheme: newTheme })
      }
    }),
    {
      name: 'morphdb-theme',
      onRehydrateStorage: () => (state) => {
        if (state) {
          const resolvedTheme = state.theme === 'system' ? getSystemTheme() : state.theme
          applyTheme(resolvedTheme)
          state.resolvedTheme = resolvedTheme
        }
      }
    }
  )
)

// Listen for system theme changes
if (typeof window !== 'undefined' && window.matchMedia) {
  window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', (e) => {
    const state = useThemeStore.getState()
    if (state.theme === 'system') {
      const resolvedTheme = e.matches ? 'dark' : 'light'
      applyTheme(resolvedTheme)
      useThemeStore.setState({ resolvedTheme })
    }
  })
}
