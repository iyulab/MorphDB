import { ElectronAPI } from '@electron-toolkit/preload'

interface ConnectionTestResult {
  success: boolean
  error?: string
  data?: Record<string, unknown>
}

declare global {
  interface Window {
    electron: ElectronAPI
    api: {
      getVersion: () => Promise<string>
      minimize: () => Promise<void>
      maximize: () => Promise<void>
      close: () => Promise<void>
      testConnection: (url: string) => Promise<ConnectionTestResult>
      onMenuNewConnection: (callback: () => void) => () => void
      onMenuSettings: (callback: () => void) => () => void
      onMenuAbout: (callback: () => void) => () => void
    }
  }
}
