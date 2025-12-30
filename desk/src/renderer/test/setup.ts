import '@testing-library/jest-dom'
import { afterEach, vi } from 'vitest'
import { cleanup } from '@testing-library/react'

// Cleanup after each test
afterEach(() => {
  cleanup()
})

// Mock window.matchMedia for theme tests
Object.defineProperty(window, 'matchMedia', {
  writable: true,
  value: vi.fn().mockImplementation((query: string) => ({
    matches: query === '(prefers-color-scheme: dark)',
    media: query,
    onchange: null,
    addListener: vi.fn(),
    removeListener: vi.fn(),
    addEventListener: vi.fn(),
    removeEventListener: vi.fn(),
    dispatchEvent: vi.fn()
  }))
})

// Mock localStorage
const localStorageMock = {
  getItem: vi.fn(),
  setItem: vi.fn(),
  removeItem: vi.fn(),
  clear: vi.fn()
}
Object.defineProperty(window, 'localStorage', {
  value: localStorageMock
})

// Mock navigator.platform
Object.defineProperty(navigator, 'platform', {
  value: 'Win32',
  writable: true
})

// Mock window.api for Electron
Object.defineProperty(window, 'api', {
  value: {
    getVersion: vi.fn().mockResolvedValue('0.1.0'),
    onMenuNewConnection: vi.fn().mockReturnValue(vi.fn()),
    onMenuAbout: vi.fn().mockReturnValue(vi.fn())
  },
  writable: true
})
