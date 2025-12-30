import { useState, useEffect, type ReactElement } from 'react'
import { Outlet } from 'react-router-dom'
import { Sidebar } from '@/components/layout/Sidebar'
import { ConnectionDialog } from '@/components/dialogs/ConnectionDialog'
import { CommandPalette, useCommandPalette } from '@/components/CommandPalette'
import { useConnectionStore } from '@/stores/connectionStore'
import type { Connection } from '@/types/connection'

export function RootLayout(): ReactElement {
  const [showConnectionDialog, setShowConnectionDialog] = useState(false)
  const [editConnection, setEditConnection] = useState<Connection | null>(null)
  const [appVersion, setAppVersion] = useState('')
  const { connections } = useConnectionStore()
  const { open: commandPaletteOpen, setOpen: setCommandPaletteOpen } = useCommandPalette()

  useEffect(() => {
    // Safety check for non-Electron environment
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
      <main className="flex-1 overflow-hidden">
        <Outlet />
      </main>

      <ConnectionDialog
        open={showConnectionDialog}
        onOpenChange={handleDialogClose}
        editConnection={editConnection}
      />

      <CommandPalette
        open={commandPaletteOpen}
        onOpenChange={setCommandPaletteOpen}
      />
    </div>
  )
}
