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

  // Connection testing
  testConnection: (
    url: string,
    apiKey: string,
    tenantId?: string
  ): Promise<ConnectionTestResult> =>
    ipcRenderer.invoke('connection:test', url, apiKey, tenantId),

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
