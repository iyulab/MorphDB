import { ElectronAPI } from '@electron-toolkit/preload'

interface CredentialResult {
  success: boolean
  error?: string
}

interface ConnectionTestResult {
  success: boolean
  error?: string
  data?: Record<string, unknown>
}

interface CredentialsAPI {
  save: (connectionId: string, apiKey: string) => Promise<CredentialResult>
  get: (connectionId: string) => Promise<string | null>
  delete: (connectionId: string) => Promise<CredentialResult>
  has: (connectionId: string) => Promise<boolean>
}

declare global {
  interface Window {
    electron: ElectronAPI
    api: {
      getVersion: () => Promise<string>
      minimize: () => Promise<void>
      maximize: () => Promise<void>
      close: () => Promise<void>
      credentials: CredentialsAPI
      testConnection: (url: string, apiKey: string) => Promise<ConnectionTestResult>
      onMenuNewConnection: (callback: () => void) => () => void
      onMenuSettings: (callback: () => void) => () => void
      onMenuAbout: (callback: () => void) => () => void
    }
  }
}
