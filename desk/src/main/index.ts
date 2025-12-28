import { app, shell, BrowserWindow, ipcMain, Menu, safeStorage } from 'electron'
import { join } from 'path'
import { electronApp, optimizer, is } from '@electron-toolkit/utils'
import Store from 'electron-store'

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
      preload: join(__dirname, '../preload/index.js'),
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
          label: 'About MorphDB Studio',
          click: () => mainWindow?.webContents.send('menu:about')
        }
      ]
    }
  ]

  const menu = Menu.buildFromTemplate(template)
  Menu.setApplicationMenu(menu)
}

// App lifecycle
app.whenReady().then(() => {
  electronApp.setAppUserModelId('com.iyulab.morphdb-studio')

  app.on('browser-window-created', (_, window) => {
    optimizer.watchWindowShortcuts(window)
  })

  createMenu()
  createWindow()

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
ipcMain.handle('connection:test', async (_event, url: string, apiKey: string, tenantId?: string) => {
  try {
    const controller = new AbortController()
    const timeoutId = setTimeout(() => controller.abort(), 10000) // 10 second timeout

    const response = await fetch(`${url}/health`, {
      method: 'GET',
      headers: {
        'X-API-Key': apiKey,
        ...(tenantId && { 'X-Tenant-Id': tenantId })
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
