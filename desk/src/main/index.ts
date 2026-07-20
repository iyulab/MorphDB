import { app, shell, BrowserWindow, ipcMain, Menu, safeStorage, dialog } from 'electron'
import { join } from 'path'
import { electronApp, optimizer, is } from '@electron-toolkit/utils'
import electronUpdater from 'electron-updater'
const { autoUpdater } = electronUpdater
import Store from 'electron-store'
import log from 'electron-log'

// Configure logging for auto-updater
log.transports.file.level = 'info'
autoUpdater.logger = log

// Auto-update configuration
autoUpdater.autoDownload = false
autoUpdater.autoInstallOnAppQuit = true

// Initialize secure store for encrypted credentials
const credentialStore = new Store<Record<string, string>>({
  name: 'secure-credentials',
  encryptionKey: 'morphdb-studio-credential-key'
})

let mainWindow: BrowserWindow | null = null

function createWindow(): void {
  mainWindow = new BrowserWindow({
    width: 1400,
    height: 900,
    minWidth: 1024,
    minHeight: 600,
    show: false,
    autoHideMenuBar: false,
    frame: true,
    titleBarStyle: 'default',
    webPreferences: {
      preload: join(__dirname, '../preload/index.mjs'),
      sandbox: false,
      contextIsolation: true,
      nodeIntegration: false
    }
  })

  mainWindow.on('ready-to-show', () => {
    mainWindow?.show()
  })

  mainWindow.webContents.setWindowOpenHandler((details) => {
    shell.openExternal(details.url)
    return { action: 'deny' }
  })

  // Load renderer
  if (is.dev && process.env['ELECTRON_RENDERER_URL']) {
    mainWindow.loadURL(process.env['ELECTRON_RENDERER_URL'])
  } else {
    mainWindow.loadFile(join(__dirname, '../renderer/index.html'))
  }
}

function createMenu(): void {
  const template: Electron.MenuItemConstructorOptions[] = [
    {
      label: 'File',
      submenu: [
        {
          label: 'New Connection',
          accelerator: 'CmdOrCtrl+N',
          click: () => mainWindow?.webContents.send('menu:new-connection')
        },
        { type: 'separator' },
        {
          label: 'Settings',
          accelerator: 'CmdOrCtrl+,',
          click: () => mainWindow?.webContents.send('menu:settings')
        },
        { type: 'separator' },
        { role: 'quit' }
      ]
    },
    {
      label: 'Edit',
      submenu: [
        { role: 'undo' },
        { role: 'redo' },
        { type: 'separator' },
        { role: 'cut' },
        { role: 'copy' },
        { role: 'paste' },
        { role: 'selectAll' }
      ]
    },
    {
      label: 'View',
      submenu: [
        { role: 'reload' },
        { role: 'forceReload' },
        { role: 'toggleDevTools' },
        { type: 'separator' },
        { role: 'resetZoom' },
        { role: 'zoomIn' },
        { role: 'zoomOut' },
        { type: 'separator' },
        { role: 'togglefullscreen' }
      ]
    },
    {
      label: 'Help',
      submenu: [
        {
          label: 'Documentation',
          click: () => shell.openExternal('https://github.com/iyulab/MorphDB')
        },
        {
          label: 'Report Issue',
          click: () => shell.openExternal('https://github.com/iyulab/MorphDB/issues')
        },
        { type: 'separator' },
        {
          label: 'Check for Updates...',
          click: () => {
            if (is.dev) {
              dialog.showMessageBox(mainWindow!, {
                type: 'info',
                title: 'Development Mode',
                message: 'Auto-update is disabled in development mode.'
              })
            } else {
              autoUpdater.checkForUpdates()
            }
          }
        },
        { type: 'separator' },
        {
          label: 'About MorphDB Studio',
          click: () => mainWindow?.webContents.send('menu:about')
        }
      ]
    }
  ]

  const menu = Menu.buildFromTemplate(template)
  Menu.setApplicationMenu(menu)
}

// Auto-updater setup
function setupAutoUpdater(): void {
  // Only check for updates in production
  if (is.dev) {
    log.info('Skipping auto-update in development mode')
    return
  }

  // Check for updates
  autoUpdater.checkForUpdates().catch((err) => {
    log.error('Error checking for updates:', err)
  })

  // Update available
  autoUpdater.on('update-available', (info) => {
    log.info('Update available:', info.version)
    mainWindow?.webContents.send('update:available', {
      version: info.version,
      releaseDate: info.releaseDate,
      releaseNotes: info.releaseNotes
    })

    dialog
      .showMessageBox(mainWindow!, {
        type: 'info',
        title: 'Update Available',
        message: `A new version (${info.version}) is available.`,
        detail: 'Would you like to download it now?',
        buttons: ['Download', 'Later'],
        defaultId: 0,
        cancelId: 1
      })
      .then((result) => {
        if (result.response === 0) {
          autoUpdater.downloadUpdate()
        }
      })
  })

  // No update available
  autoUpdater.on('update-not-available', (info) => {
    log.info('No update available. Current version:', info.version)
  })

  // Download progress
  autoUpdater.on('download-progress', (progress) => {
    log.info(`Download progress: ${progress.percent.toFixed(1)}%`)
    mainWindow?.webContents.send('update:progress', {
      percent: progress.percent,
      bytesPerSecond: progress.bytesPerSecond,
      transferred: progress.transferred,
      total: progress.total
    })
  })

  // Update downloaded
  autoUpdater.on('update-downloaded', (info) => {
    log.info('Update downloaded:', info.version)
    mainWindow?.webContents.send('update:downloaded', {
      version: info.version
    })

    dialog
      .showMessageBox(mainWindow!, {
        type: 'info',
        title: 'Update Ready',
        message: `Version ${info.version} has been downloaded.`,
        detail: 'The application will restart to install the update.',
        buttons: ['Install Now', 'Install on Quit'],
        defaultId: 0,
        cancelId: 1
      })
      .then((result) => {
        if (result.response === 0) {
          autoUpdater.quitAndInstall(false, true)
        }
      })
  })

  // Error handling
  autoUpdater.on('error', (error) => {
    log.error('Auto-updater error:', error)
    mainWindow?.webContents.send('update:error', {
      message: error.message
    })
  })
}

// App lifecycle
app.whenReady().then(() => {
  electronApp.setAppUserModelId('com.iyulab.morphdb-studio')

  app.on('browser-window-created', (_, window) => {
    optimizer.watchWindowShortcuts(window)
  })

  createMenu()
  createWindow()

  // Initialize auto-updater after window is ready
  mainWindow?.once('ready-to-show', () => {
    setupAutoUpdater()
  })

  app.on('activate', () => {
    if (BrowserWindow.getAllWindows().length === 0) {
      createWindow()
    }
  })
})

app.on('window-all-closed', () => {
  if (process.platform !== 'darwin') {
    app.quit()
  }
})

// IPC Handlers
ipcMain.handle('app:version', () => app.getVersion())

ipcMain.handle('window:minimize', () => mainWindow?.minimize())
ipcMain.handle('window:maximize', () => {
  if (mainWindow?.isMaximized()) {
    mainWindow.unmaximize()
  } else {
    mainWindow?.maximize()
  }
})
ipcMain.handle('window:close', () => mainWindow?.close())

// Secure credential storage handlers
ipcMain.handle('credentials:save', (_event, connectionId: string, apiKey: string) => {
  try {
    if (safeStorage.isEncryptionAvailable()) {
      const encrypted = safeStorage.encryptString(apiKey)
      credentialStore.set(connectionId, encrypted.toString('base64'))
    } else {
      // Fallback to electron-store encryption
      credentialStore.set(connectionId, apiKey)
    }
    return { success: true }
  } catch (error) {
    console.error('Failed to save credential:', error)
    return { success: false, error: (error as Error).message }
  }
})

ipcMain.handle('credentials:get', (_event, connectionId: string) => {
  try {
    const stored = credentialStore.get(connectionId)
    if (!stored) return null

    if (safeStorage.isEncryptionAvailable()) {
      const buffer = Buffer.from(stored, 'base64')
      return safeStorage.decryptString(buffer)
    } else {
      return stored
    }
  } catch (error) {
    console.error('Failed to get credential:', error)
    return null
  }
})

ipcMain.handle('credentials:delete', (_event, connectionId: string) => {
  try {
    credentialStore.delete(connectionId)
    return { success: true }
  } catch (error) {
    console.error('Failed to delete credential:', error)
    return { success: false, error: (error as Error).message }
  }
})

ipcMain.handle('credentials:has', (_event, connectionId: string) => {
  return credentialStore.has(connectionId)
})

// Connection test handler
ipcMain.handle('connection:test', async (_event, url: string, apiKey: string) => {
  try {
    const controller = new AbortController()
    const timeoutId = setTimeout(() => controller.abort(), 10000) // 10 second timeout

    // Project ID is automatically resolved from API key on the server side
    const response = await fetch(`${url}/health`, {
      method: 'GET',
      headers: {
        'X-API-Key': apiKey
      },
      signal: controller.signal
    })

    clearTimeout(timeoutId)

    if (response.ok) {
      const data = await response.json().catch(() => ({}))
      return { success: true, data }
    } else {
      return { success: false, error: `Server returned ${response.status}` }
    }
  } catch (error) {
    if ((error as Error).name === 'AbortError') {
      return { success: false, error: 'Connection timeout' }
    }
    return { success: false, error: (error as Error).message }
  }
})

// Auto-update IPC handlers
ipcMain.handle('update:check', async () => {
  if (is.dev) {
    return { available: false, message: 'Auto-update disabled in development' }
  }
  try {
    const result = await autoUpdater.checkForUpdates()
    return {
      available: result?.updateInfo?.version !== app.getVersion(),
      version: result?.updateInfo?.version,
      releaseDate: result?.updateInfo?.releaseDate
    }
  } catch (error) {
    return { available: false, error: (error as Error).message }
  }
})

ipcMain.handle('update:download', async () => {
  if (is.dev) return { success: false, message: 'Auto-update disabled in development' }
  try {
    await autoUpdater.downloadUpdate()
    return { success: true }
  } catch (error) {
    return { success: false, error: (error as Error).message }
  }
})

ipcMain.handle('update:install', () => {
  autoUpdater.quitAndInstall(false, true)
})
