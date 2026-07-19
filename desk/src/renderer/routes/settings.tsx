import { type ReactElement } from 'react'
import { Settings, Palette, Globe, Database, Shield } from 'lucide-react'
import { ThemeToggle } from '@/components/ui/ThemeToggle'
import { useThemeStore } from '@/stores/themeStore'

export function SettingsPage(): ReactElement {
  const { theme } = useThemeStore()

  return (
    <div className="flex flex-col h-full bg-background">
      {/* Header */}
      <div className="flex h-10 items-center border-b border-border px-4">
        <Settings className="h-4 w-4 mr-2" />
        <span className="font-medium text-sm">Settings</span>
      </div>

      {/* Content */}
      <div className="flex-1 overflow-auto p-6">
        <div className="max-w-2xl space-y-6">
          {/* Appearance Section */}
          <section className="rounded-lg border border-border p-4">
            <div className="flex items-center gap-2 mb-4">
              <Palette className="h-5 w-5 text-muted-foreground" />
              <h2 className="font-semibold">Appearance</h2>
            </div>
            <div className="space-y-4">
              <div className="flex items-center justify-between">
                <div>
                  <p className="text-sm font-medium">Theme</p>
                  <p className="text-sm text-muted-foreground">
                    Select your preferred color theme
                  </p>
                </div>
                <ThemeToggle showLabel />
              </div>
              <div className="text-xs text-muted-foreground pt-2 border-t border-border">
                Current: {theme === 'system' ? 'System preference' : theme === 'dark' ? 'Dark' : 'Light'}
              </div>
            </div>
          </section>

          {/* Connection Defaults Section */}
          <section className="rounded-lg border border-border p-4">
            <div className="flex items-center gap-2 mb-4">
              <Database className="h-5 w-5 text-muted-foreground" />
              <h2 className="font-semibold">Connection Defaults</h2>
            </div>
            <div className="space-y-4 text-sm text-muted-foreground">
              <p>Default connection settings will be configurable in a future update.</p>
            </div>
          </section>

          {/* API Configuration Section */}
          <section className="rounded-lg border border-border p-4">
            <div className="flex items-center gap-2 mb-4">
              <Globe className="h-5 w-5 text-muted-foreground" />
              <h2 className="font-semibold">API Configuration</h2>
            </div>
            <div className="space-y-4 text-sm text-muted-foreground">
              <p>API key management and configuration will be available in Phase 4 (Security).</p>
            </div>
          </section>

          {/* Security Section */}
          <section className="rounded-lg border border-border p-4">
            <div className="flex items-center gap-2 mb-4">
              <Shield className="h-5 w-5 text-muted-foreground" />
              <h2 className="font-semibold">Security</h2>
            </div>
            <div className="space-y-4 text-sm text-muted-foreground">
              <p>Security policies will be available in Phase 4 (Security).</p>
            </div>
          </section>
        </div>
      </div>
    </div>
  )
}
