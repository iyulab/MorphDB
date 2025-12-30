import { defineConfig, devices } from '@playwright/test'
import { resolve } from 'path'

/**
 * Playwright configuration for MorphDB Desk E2E tests
 *
 * For Electron apps, there are two testing approaches:
 * 1. Web-based testing (testing the renderer in a browser) - simpler setup
 * 2. Electron testing (full app testing) - requires electron-playwright-helpers
 *
 * This config focuses on web-based testing for CI compatibility.
 * For full Electron testing, build the app first and use _electron.launch().
 */
export default defineConfig({
  testDir: './e2e',
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 2 : 0,
  workers: process.env.CI ? 1 : undefined,
  reporter: 'html',
  use: {
    baseURL: 'http://localhost:5173',
    trace: 'on-first-retry',
    screenshot: 'only-on-failure'
  },
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] }
    }
  ],
  webServer: {
    command: 'npm run dev',
    url: 'http://localhost:5173',
    reuseExistingServer: !process.env.CI,
    timeout: 120000
  }
})
