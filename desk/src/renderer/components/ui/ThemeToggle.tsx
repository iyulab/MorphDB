import { type ReactElement } from 'react'
import { Moon, Sun, Monitor } from 'lucide-react'
import { useThemeStore, type Theme } from '@/stores/themeStore'
import { cn } from '@/lib/utils'

interface ThemeToggleProps {
  showLabel?: boolean
  className?: string
}

export function ThemeToggle({ showLabel = false, className }: ThemeToggleProps): ReactElement {
  const { theme, setTheme } = useThemeStore()

  const themes: { value: Theme; icon: typeof Sun; label: string }[] = [
    { value: 'light', icon: Sun, label: 'Light' },
    { value: 'dark', icon: Moon, label: 'Dark' },
    { value: 'system', icon: Monitor, label: 'System' }
  ]

  return (
    <div className={cn('flex items-center gap-1 rounded-lg bg-muted p-1', className)}>
      {themes.map(({ value, icon: Icon, label }) => (
        <button
          key={value}
          onClick={() => setTheme(value)}
          className={cn(
            'flex items-center gap-2 rounded-md px-3 py-1.5 text-sm transition-colors',
            theme === value
              ? 'bg-background text-foreground shadow-sm'
              : 'text-muted-foreground hover:text-foreground'
          )}
          title={label}
        >
          <Icon className="h-4 w-4" />
          {showLabel && <span>{label}</span>}
        </button>
      ))}
    </div>
  )
}

interface ThemeToggleButtonProps {
  className?: string
}

export function ThemeToggleButton({ className }: ThemeToggleButtonProps): ReactElement {
  const { resolvedTheme, toggleTheme } = useThemeStore()

  return (
    <button
      onClick={toggleTheme}
      className={cn(
        'flex h-8 w-8 items-center justify-center rounded-md transition-colors',
        'hover:bg-accent text-muted-foreground hover:text-foreground',
        className
      )}
      title={`Switch to ${resolvedTheme === 'dark' ? 'light' : 'dark'} mode`}
    >
      {resolvedTheme === 'dark' ? (
        <Sun className="h-4 w-4" />
      ) : (
        <Moon className="h-4 w-4" />
      )}
    </button>
  )
}
