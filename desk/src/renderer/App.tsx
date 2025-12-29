import { useState, useEffect, type ReactElement } from 'react'
import { Sidebar } from './components/layout/Sidebar'
import { MainContent } from './components/layout/MainContent'
import { ConnectionDialog } from './components/dialogs/ConnectionDialog'
import { useConnectionStore } from './stores/connectionStore'
import type { Connection } from './types/connection'

function App(): ReactElement {
  const [showConnectionDialog, setShowConnectionDialog] = useState(false)
  const [editConnection, setEditConnection] = useState<Connection | null>(null)
  const [appVersion, setAppVersion] = useState('')
  const { connections } = useConnectionStore()

  useEffect(() => {
    // Safety check for non-Electron environment (e.g., browser dev tools)
    if (!window.api) {
      console.warn('Running outside Electron context')
      return
    }

    // Get app version
    window.api.getVersion().then(setAppVersion)

    // Menu event handlers
    const unsubNew = window.api.onMenuNewConnection(() => {
      setEditConnection(null)
      setShowConnectionDialog(true)
    })

    const unsubAbout = window.api.onMenuAbout(() => {
      alert(`MorphDB Desk v${appVersion}\n\nDatabase Management Tool\nhttps://github.com/iyulab/MorphDB`)
    })

    return () => {
      unsubNew()
      unsubAbout()
    }
  }, [appVersion])

  // Show connection dialog if no connections
  useEffect(() => {
    if (connections.length === 0) {
      setEditConnection(null)
      setShowConnectionDialog(true)
    }
  }, [connections.length])

  const handleNewConnection = (): void => {
    setEditConnection(null)
    setShowConnectionDialog(true)
  }

  const handleEditConnection = (connection: Connection): void => {
    setEditConnection(connection)
    setShowConnectionDialog(true)
  }

  const handleDialogClose = (open: boolean): void => {
    setShowConnectionDialog(open)
    if (!open) {
      setEditConnection(null)
    }
  }

  return (
    <div className="flex h-screen w-screen overflow-hidden bg-background">
      <Sidebar
        onNewConnection={handleNewConnection}
        onEditConnection={handleEditConnection}
      />
      <MainContent />

      <ConnectionDialog
        open={showConnectionDialog}
        onOpenChange={handleDialogClose}
        editConnection={editConnection}
      />
    </div>
  )
}

export default App
