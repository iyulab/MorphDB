import { useState, useEffect } from 'react'
import { Sidebar } from './components/layout/Sidebar'
import { MainContent } from './components/layout/MainContent'
import { ConnectionDialog } from './components/dialogs/ConnectionDialog'
import { useConnectionStore } from './stores/connectionStore'

function App(): JSX.Element {
  const [showConnectionDialog, setShowConnectionDialog] = useState(false)
  const [appVersion, setAppVersion] = useState('')
  const { connections, activeConnection } = useConnectionStore()

  useEffect(() => {
    // Get app version
    window.api.getVersion().then(setAppVersion)

    // Menu event handlers
    const unsubNew = window.api.onMenuNewConnection(() => {
      setShowConnectionDialog(true)
    })

    const unsubAbout = window.api.onMenuAbout(() => {
      alert(`MorphDB Studio v${appVersion}\n\nDatabase Management Tool\nhttps://github.com/iyulab/MorphDB`)
    })

    return () => {
      unsubNew()
      unsubAbout()
    }
  }, [appVersion])

  // Show connection dialog if no connections
  useEffect(() => {
    if (connections.length === 0) {
      setShowConnectionDialog(true)
    }
  }, [connections.length])

  return (
    <div className="flex h-screen w-screen overflow-hidden bg-background">
      <Sidebar onNewConnection={() => setShowConnectionDialog(true)} />
      <MainContent />

      <ConnectionDialog
        open={showConnectionDialog}
        onOpenChange={setShowConnectionDialog}
      />
    </div>
  )
}

export default App
