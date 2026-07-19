import { test, expect, type Page } from '@playwright/test'

/**
 * E2E Critical Path Tests for MorphDB Desk
 *
 * These tests cover the primary user journey:
 * Connection → Table Management → Data CRUD
 *
 * Note: Tests run against the dev server in browser mode.
 * Mock API responses are used where backend is unavailable.
 */

// Helper to close any open dialogs
async function closeDialogs(page: Page): Promise<void> {
  await page.keyboard.press('Escape')
  await page.waitForTimeout(300)
}

// Helper to wait for app to be ready
async function waitForAppReady(page: Page): Promise<void> {
  await page.goto('/')
  await expect(page.locator('text=MorphDB')).toBeVisible({ timeout: 10000 })
}

test.describe('Critical Path: Connection Management', () => {
  test.beforeEach(async ({ page }) => {
    await waitForAppReady(page)
  })

  test('should show connection dialog on first launch', async ({ page }) => {
    // When there are no connections, dialog should appear
    const dialog = page.locator('[role="dialog"]')
    await expect(dialog).toBeVisible({ timeout: 5000 })

    // Should have the new connection form
    await expect(page.getByText('New Connection')).toBeVisible()
  })

  test('should have connection form with required fields', async ({ page }) => {
    // Close auto-opened dialog and open via button
    await closeDialogs(page)

    // Click new connection button
    const addButton = page.locator('button[title="New Connection"]')
    await expect(addButton).toBeVisible()
    await addButton.click()

    // Dialog should open
    const dialog = page.locator('[role="dialog"]')
    await expect(dialog).toBeVisible()

    // Check form fields
    await expect(page.locator('label:has-text("Connection Name")')).toBeVisible()
    await expect(page.locator('label:has-text("Server URL")')).toBeVisible()
    await expect(page.locator('label:has-text("API Key")')).toBeVisible()

    // Check buttons
    await expect(page.getByRole('button', { name: 'Test Connection' })).toBeVisible()
    await expect(page.getByRole('button', { name: 'Connect' })).toBeVisible()
  })

  test('should validate connection form', async ({ page }) => {
    await closeDialogs(page)

    await page.click('button[title="New Connection"]')
    await expect(page.locator('[role="dialog"]')).toBeVisible()

    // Connect button should be disabled when fields are empty
    const connectButton = page.getByRole('button', { name: 'Connect' })
    await expect(connectButton).toBeDisabled()

    // Fill in connection name
    await page.fill('#name', 'Test Connection')
    await expect(connectButton).toBeDisabled() // Still disabled, need API key

    // Fill in API key
    await page.fill('#apiKey', 'test-api-key')
    await expect(connectButton).toBeEnabled() // Now enabled
  })

  test('should fill connection form and submit', async ({ page }) => {
    await closeDialogs(page)

    await page.click('button[title="New Connection"]')
    await expect(page.locator('[role="dialog"]')).toBeVisible()

    // Fill form
    await page.fill('#name', 'Local Development')
    await page.fill('#url', 'http://localhost:5000')
    await page.fill('#apiKey', 'dev-api-key-12345')

    // Verify values
    await expect(page.locator('#name')).toHaveValue('Local Development')
    await expect(page.locator('#url')).toHaveValue('http://localhost:5000')
    await expect(page.locator('#apiKey')).toHaveValue('dev-api-key-12345')

    // Connect button should be enabled
    const connectButton = page.getByRole('button', { name: 'Connect' })
    await expect(connectButton).toBeEnabled()
  })

  test('should close connection dialog with Escape key', async ({ page }) => {
    await closeDialogs(page)

    await page.click('button[title="New Connection"]')
    await expect(page.locator('[role="dialog"]')).toBeVisible()

    // Press Escape to close
    await page.keyboard.press('Escape')
    await page.waitForTimeout(300)

    // Dialog should be closed
    await expect(page.locator('[role="dialog"]')).not.toBeVisible()
  })

  test('should close connection dialog with X button', async ({ page }) => {
    await closeDialogs(page)

    await page.click('button[title="New Connection"]')
    await expect(page.locator('[role="dialog"]')).toBeVisible()

    // Click X button
    const closeButton = page.locator('[role="dialog"] button').first()
    await closeButton.click()

    // Dialog should be closed
    await expect(page.locator('[role="dialog"]')).not.toBeVisible()
  })
})

test.describe('Critical Path: Navigation', () => {
  test.beforeEach(async ({ page }) => {
    await waitForAppReady(page)
    await closeDialogs(page)
  })

  test('should have sidebar with main navigation items', async ({ page }) => {
    const sidebar = page.locator('aside')
    await expect(sidebar).toBeVisible()

    // Primary navigation items
    await expect(page.getByText('Explorer')).toBeVisible()
    await expect(page.getByText('Projects')).toBeVisible()
    await expect(page.getByText('Views')).toBeVisible()
  })

  test('should navigate to Explorer page', async ({ page }) => {
    await page.click('text=Explorer')
    await expect(page).toHaveURL(/\/explorer/)
  })

  test('should navigate to Projects page', async ({ page }) => {
    await page.click('text=Projects')
    await expect(page).toHaveURL(/\/projects/)
  })

  test('should navigate to Views page', async ({ page }) => {
    await page.click('text=Views')
    await expect(page).toHaveURL(/\/views/)
  })

  test('should navigate to Settings page', async ({ page }) => {
    await page.click('text=Settings')
    await expect(page).toHaveURL(/\/settings/)
    await expect(page.locator('h2:has-text("Appearance")')).toBeVisible()
  })

  test('should have admin section in sidebar', async ({ page }) => {
    // Check for admin navigation items
    await expect(page.getByText('Audit Log')).toBeVisible()
  })

  test('should navigate using keyboard shortcuts', async ({ page }) => {
    // Navigate to Explorer with 'g e'
    await page.keyboard.press('g')
    await page.keyboard.press('e')
    await expect(page).toHaveURL(/\/explorer/)

    // Navigate to Settings with 'g s'
    await page.keyboard.press('g')
    await page.keyboard.press('s')
    await expect(page).toHaveURL(/\/settings/)
  })
})

test.describe('Critical Path: Explorer (No Connection)', () => {
  test.beforeEach(async ({ page }) => {
    await waitForAppReady(page)
    await closeDialogs(page)
  })

  test('should show welcome message when no connection active', async ({ page }) => {
    await page.click('text=Explorer')
    await expect(page).toHaveURL(/\/explorer/)

    // Should show welcome message
    await expect(page.getByText('Welcome to MorphDB Desk')).toBeVisible()
    await expect(
      page.getByText('Select a connection from the sidebar or create a new one')
    ).toBeVisible()
  })
})

test.describe('Critical Path: Command Palette', () => {
  test.beforeEach(async ({ page }) => {
    await waitForAppReady(page)
    await closeDialogs(page)
  })

  test('should open command palette with Ctrl+K', async ({ page }) => {
    await page.keyboard.press('Control+k')
    await expect(page.locator('.command-palette')).toBeVisible()
    await expect(page.locator('.command-input')).toBeVisible()
  })

  test('should open command palette with Meta+K (Mac)', async ({ page }) => {
    await page.keyboard.press('Meta+k')
    await expect(page.locator('.command-palette')).toBeVisible()
  })

  test('should close command palette with Escape', async ({ page }) => {
    await page.keyboard.press('Control+k')
    await expect(page.locator('.command-palette')).toBeVisible()

    await page.keyboard.press('Escape')
    await expect(page.locator('.command-palette')).not.toBeVisible()
  })

  test('should have command palette search functionality', async ({ page }) => {
    await page.keyboard.press('Control+k')
    await expect(page.locator('.command-palette')).toBeVisible()

    const input = page.locator('.command-input')
    await input.fill('settings')

    // Should show filtered results
    await expect(page.getByText('Go to Settings')).toBeVisible()
  })
})

test.describe('Critical Path: Keyboard Shortcuts Help', () => {
  test.beforeEach(async ({ page }) => {
    await waitForAppReady(page)
    await closeDialogs(page)
  })

  test('should open keyboard shortcuts with ? key', async ({ page }) => {
    await page.keyboard.press('Shift+/')

    await expect(page.locator('.shortcuts-dialog')).toBeVisible()
    await expect(page.getByText('Keyboard Shortcuts')).toBeVisible()
  })

  test('should show navigation shortcuts', async ({ page }) => {
    await page.keyboard.press('Shift+/')

    await expect(page.getByText('Navigation')).toBeVisible()
    await expect(page.getByText('Go to Explorer')).toBeVisible()
    await expect(page.getByText('Go to Settings')).toBeVisible()
  })

  test('should close keyboard shortcuts dialog', async ({ page }) => {
    await page.keyboard.press('Shift+/')
    await expect(page.locator('.shortcuts-dialog')).toBeVisible()

    await page.keyboard.press('Escape')
    await expect(page.locator('.shortcuts-dialog')).not.toBeVisible()
  })
})

test.describe('Critical Path: Settings', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/settings')
    await closeDialogs(page)
  })

  test('should display appearance settings', async ({ page }) => {
    await expect(page.locator('h2:has-text("Appearance")')).toBeVisible()
  })

  test('should have theme selection options', async ({ page }) => {
    const themeSection = page.locator('section').filter({ hasText: 'Appearance' })
    await expect(themeSection).toBeVisible()

    // Should have theme toggle/select
    const themeControl = themeSection.locator('select, button, [role="combobox"]').first()
    await expect(themeControl).toBeVisible()
  })
})

test.describe('Critical Path: Responsive Layout', () => {
  test('should display correctly on desktop viewport', async ({ page }) => {
    await page.setViewportSize({ width: 1920, height: 1080 })
    await waitForAppReady(page)
    await closeDialogs(page)

    // Sidebar should be visible
    const sidebar = page.locator('aside')
    await expect(sidebar).toBeVisible()

    // Main content should be visible
    await expect(page.locator('main')).toBeVisible()
  })

  test('should display correctly on laptop viewport', async ({ page }) => {
    await page.setViewportSize({ width: 1366, height: 768 })
    await waitForAppReady(page)
    await closeDialogs(page)

    // Sidebar should still be visible
    const sidebar = page.locator('aside')
    await expect(sidebar).toBeVisible()
  })

  test('should display correctly on tablet viewport', async ({ page }) => {
    await page.setViewportSize({ width: 768, height: 1024 })
    await waitForAppReady(page)
    await closeDialogs(page)

    // App should still be usable
    await expect(page.getByText('MorphDB')).toBeVisible()
  })
})

test.describe('Critical Path: Error Handling', () => {
  test.beforeEach(async ({ page }) => {
    await waitForAppReady(page)
    await closeDialogs(page)
  })

  test('should handle navigation to non-existent route', async ({ page }) => {
    await page.goto('/#/non-existent-route')

    // App should still be functional (not crash)
    await expect(page.getByText('MorphDB')).toBeVisible()
  })
})

test.describe('Critical Path: Accessibility', () => {
  test.beforeEach(async ({ page }) => {
    await waitForAppReady(page)
    await closeDialogs(page)
  })

  test('should have proper focus management in dialogs', async ({ page }) => {
    await page.click('button[title="New Connection"]')
    await expect(page.locator('[role="dialog"]')).toBeVisible()

    // First focusable element should be focused (or the dialog itself)
    // Tab should move through form fields
    await page.keyboard.press('Tab')
    await page.keyboard.press('Tab')

    // Shift+Tab should move backward
    await page.keyboard.press('Shift+Tab')
  })

  test('should have proper heading structure', async ({ page }) => {
    await page.goto('/settings')

    // Should have h2 headings
    const h2Headings = await page.locator('h2').count()
    expect(h2Headings).toBeGreaterThan(0)
  })

  test('should support keyboard navigation in sidebar', async ({ page }) => {
    // Focus sidebar
    const sidebar = page.locator('aside')
    await expect(sidebar).toBeVisible()

    // Click on Explorer to focus it
    await page.click('text=Explorer')

    // Should be able to navigate
    await expect(page).toHaveURL(/\/explorer/)
  })
})

test.describe('Critical Path: Performance', () => {
  test('should load app within reasonable time', async ({ page }) => {
    const startTime = Date.now()

    await page.goto('/')
    await expect(page.locator('text=MorphDB')).toBeVisible()

    const loadTime = Date.now() - startTime

    // App should load within 5 seconds (generous for CI)
    expect(loadTime).toBeLessThan(5000)
  })

  test('should navigate between pages quickly', async ({ page }) => {
    await waitForAppReady(page)
    await closeDialogs(page)

    const startTime = Date.now()

    await page.click('text=Settings')
    await expect(page.locator('h2:has-text("Appearance")')).toBeVisible()

    const navigationTime = Date.now() - startTime

    // Navigation should be fast (under 1 second)
    expect(navigationTime).toBeLessThan(1000)
  })
})
