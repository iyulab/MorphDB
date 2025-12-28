import { type ReactElement } from 'react'
import { Database } from 'lucide-react'
import { useConnectionStore } from '@/stores/connectionStore'
import { useExplorerStore } from '@/stores/explorerStore'
import { TableExplorer } from '@/components/explorer/TableExplorer'
import { TableView } from '@/components/grid/TableView'

export function MainContent(): ReactElement {
  const { activeConnection } = useConnectionStore()
  const { selectedTable } = useExplorerStore()

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
        {activeConnection.status === 'connected' && (
          <span className="ml-auto inline-flex items-center gap-1.5 text-xs text-success">
            <span className="h-1.5 w-1.5 rounded-full bg-success" />
            Connected
          </span>
        )}
        {activeConnection.status === 'connecting' && (
          <span className="ml-auto text-xs text-warning">Connecting...</span>
        )}
        {activeConnection.status === 'error' && (
          <span className="ml-auto text-xs text-destructive">
            Error: {activeConnection.errorMessage}
          </span>
        )}
      </div>

      {/* Content area */}
      <div className="flex flex-1 overflow-hidden">
        {/* Left panel - Table explorer */}
        <div className="w-64 border-r border-border overflow-hidden flex flex-col">
          <TableExplorer />
        </div>

        {/* Right panel - Data grid */}
        <div className="flex-1 overflow-hidden">
          {selectedTable ? (
            <div className="h-full bg-card">
              <TableView tableName={selectedTable} />
            </div>
          ) : (
            <div className="flex h-full items-center justify-center">
              <p className="text-muted-foreground">Select a table to view data</p>
            </div>
          )}
        </div>
      </div>
    </main>
  )
}
