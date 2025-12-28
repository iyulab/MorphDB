import { type ReactElement } from 'react'
import { Database } from 'lucide-react'
import { useConnectionStore } from '@/stores/connectionStore'

export function MainContent(): ReactElement {
  const { activeConnection } = useConnectionStore()

  if (!activeConnection) {
    return (
      <main className="flex flex-1 flex-col items-center justify-center bg-background p-8">
        <div className="text-center">
          <Database className="mx-auto h-16 w-16 text-muted-foreground/50" />
          <h2 className="mt-4 text-xl font-semibold text-foreground">Welcome to MorphDB Studio</h2>
          <p className="mt-2 text-muted-foreground">
            Select a connection from the sidebar or create a new one to get started.
          </p>
        </div>
      </main>
    )
  }

  return (
    <main className="flex flex-1 flex-col bg-background">
      {/* Toolbar */}
      <div className="flex h-10 items-center border-b border-border px-4">
        <span className="font-medium text-sm">{activeConnection.name}</span>
        <span className="ml-2 text-xs text-muted-foreground">{activeConnection.url}</span>
      </div>

      {/* Content area - will be replaced with table explorer and data grid */}
      <div className="flex flex-1 overflow-hidden">
        {/* Left panel - Table explorer */}
        <div className="w-64 border-r border-border p-4">
          <h3 className="mb-2 text-sm font-medium text-muted-foreground">Tables</h3>
          <p className="text-xs text-muted-foreground">
            Connect to load tables...
          </p>
        </div>

        {/* Right panel - Data grid */}
        <div className="flex-1 p-4">
          <div className="flex h-full items-center justify-center rounded-lg border border-dashed border-border">
            <p className="text-muted-foreground">
              Select a table to view data
            </p>
          </div>
        </div>
      </div>
    </main>
  )
}
