import { ElectronAPI } from '@electron-toolkit/preload'

declare global {
  interface Window {
    electron: ElectronAPI
    api: {
      getVersion: () => Promise<string>
      minimize: () => Promise<void>
      maximize: () => Promise<void>
      close: () => Promise<void>
      onMenuNewConnection: (callback: () => void) => () => void
      onMenuSettings: (callback: () => void) => () => void
      onMenuAbout: (callback: () => void) => () => void
    }
  }
}
