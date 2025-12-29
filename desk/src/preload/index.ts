import { contextBridge, ipcRenderer } from 'electron'
import { electronAPI } from '@electron-toolkit/preload'

interface CredentialResult {
  success: boolean
  error?: string
}

interface ConnectionTestResult {
  success: boolean
  error?: string
  data?: Record<string, unknown>
}

interface UpdateCheckResult {
  available: boolean
  version?: string
  releaseDate?: string
  message?: string
  error?: string
}

interface UpdateDownloadResult {
  success: boolean
  message?: string
  error?: string
}

interface UpdateProgressInfo {
  percent: number
  bytesPerSecond: number
  transferred: number
  total: number
}

interface UpdateAvailableInfo {
  version: string
  releaseDate?: string
  releaseNotes?: string | Array<{ version: string; note: string }>
}

interface UpdateDownloadedInfo {
  version: string
}

interface UpdateErrorInfo {
  message: string
}

// Custom APIs for renderer
const api = {
  // App info
  getVersion: (): Promise<string> => ipcRenderer.invoke('app:version'),

  // Window controls
  minimize: (): Promise<void> => ipcRenderer.invoke('window:minimize'),
  maximize: (): Promise<void> => ipcRenderer.invoke('window:maximize'),
  close: (): Promise<void> => ipcRenderer.invoke('window:close'),

  // Secure credential storage
  credentials: {
    save: (connectionId: string, apiKey: string): Promise<CredentialResult> =>
      ipcRenderer.invoke('credentials:save', connectionId, apiKey),
    get: (connectionId: string): Promise<string | null> =>
      ipcRenderer.invoke('credentials:get', connectionId),
    delete: (connectionId: string): Promise<CredentialResult> =>
      ipcRenderer.invoke('credentials:delete', connectionId),
    has: (connectionId: string): Promise<boolean> =>
      ipcRenderer.invoke('credentials:has', connectionId)
  },

  // Connection testing (tenant ID is automatically resolved from API key)
  testConnection: (url: string, apiKey: string): Promise<ConnectionTestResult> =>
    ipcRenderer.invoke('connection:test', url, apiKey),

  // Menu event listeners
  onMenuNewConnection: (callback: () => void): (() => void) => {
    const handler = (): void => callback()
    ipcRenderer.on('menu:new-connection', handler)
    return () => ipcRenderer.removeListener('menu:new-connection', handler)
  },
  onMenuSettings: (callback: () => void): (() => void) => {
    const handler = (): void => callback()
    ipcRenderer.on('menu:settings', handler)
    return () => ipcRenderer.removeListener('menu:settings', handler)
  },
  onMenuAbout: (callback: () => void): (() => void) => {
    const handler = (): void => callback()
    ipcRenderer.on('menu:about', handler)
    return () => ipcRenderer.removeListener('menu:about', handler)
  },

  // Auto-update
  update: {
    check: (): Promise<UpdateCheckResult> => ipcRenderer.invoke('update:check'),
    download: (): Promise<UpdateDownloadResult> => ipcRenderer.invoke('update:download'),
    install: (): Promise<void> => ipcRenderer.invoke('update:install'),

    // Update event listeners
    onAvailable: (callback: (info: UpdateAvailableInfo) => void): (() => void) => {
      const handler = (_event: Electron.IpcRendererEvent, info: UpdateAvailableInfo): void =>
        callback(info)
      ipcRenderer.on('update:available', handler)
      return () => ipcRenderer.removeListener('update:available', handler)
    },
    onProgress: (callback: (progress: UpdateProgressInfo) => void): (() => void) => {
      const handler = (_event: Electron.IpcRendererEvent, progress: UpdateProgressInfo): void =>
        callback(progress)
      ipcRenderer.on('update:progress', handler)
      return () => ipcRenderer.removeListener('update:progress', handler)
    },
    onDownloaded: (callback: (info: UpdateDownloadedInfo) => void): (() => void) => {
      const handler = (_event: Electron.IpcRendererEvent, info: UpdateDownloadedInfo): void =>
        callback(info)
      ipcRenderer.on('update:downloaded', handler)
      return () => ipcRenderer.removeListener('update:downloaded', handler)
    },
    onError: (callback: (error: UpdateErrorInfo) => void): (() => void) => {
      const handler = (_event: Electron.IpcRendererEvent, error: UpdateErrorInfo): void =>
        callback(error)
      ipcRenderer.on('update:error', handler)
      return () => ipcRenderer.removeListener('update:error', handler)
    }
  }
}

// Expose APIs to renderer
if (process.contextIsolated) {
  try {
    contextBridge.exposeInMainWorld('electron', electronAPI)
    contextBridge.exposeInMainWorld('api', api)
  } catch (error) {
    console.error('Failed to expose APIs:', error)
  }
} else {
  // @ts-ignore (legacy mode)
  window.electron = electronAPI
  // @ts-ignore
  window.api = api
}
