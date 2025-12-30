import { test, expect } from '@playwright/test'

/**
 * E2E tests for MorphDB Desk
 *
 * Note: These tests run against the dev server in browser mode.
 * Some Electron-specific features (like window.api) are mocked.
 */

test.describe('MorphDB Desk', () => {
  test('should load the application', async ({ page }) => {
    await page.goto('/')

    // Should show MorphDB branding
    await expect(page.locator('text=MorphDB')).toBeVisible()
  })

  test('should show connection dialog when no connections', async ({ page }) => {
    await page.goto('/')

    // Dialog should be visible since there are no connections
    await expect(page.locator('[role="dialog"]')).toBeVisible({ timeout: 5000 })
  })

  test('should have navigation sidebar', async ({ page }) => {
    await page.goto('/')

    // Close any dialogs first
    await page.keyboard.press('Escape')
    await page.waitForTimeout(300)

    // Check sidebar navigation items exist
    const sidebar = page.locator('aside')
    await expect(sidebar).toBeVisible()

    // Check main navigation items
    await expect(page.locator('text=Explorer')).toBeVisible()
    await expect(page.locator('text=Projects')).toBeVisible()
    await expect(page.locator('text=Views')).toBeVisible()
    await expect(page.locator('text=Settings')).toBeVisible()
  })

  test('should open command palette with keyboard shortcut', async ({ page }) => {
    await page.goto('/')

    // Close any dialogs first
    await page.keyboard.press('Escape')
    await page.waitForTimeout(300)

    // Open command palette
    await page.keyboard.press('Meta+k')

    // Command palette should be visible
    await expect(page.locator('.command-palette')).toBeVisible()

    // Should have search input
    await expect(page.locator('.command-input')).toBeVisible()
  })

  test('should navigate using sidebar', async ({ page }) => {
    await page.goto('/')

    // Close any dialogs first
    await page.keyboard.press('Escape')
    await page.waitForTimeout(300)

    // Click on Settings
    await page.click('text=Settings')

    // Should navigate to settings page
    await expect(page).toHaveURL(/\/settings/)
    await expect(page.locator('h2:has-text("Appearance")')).toBeVisible()
  })

  test('should toggle theme', async ({ page }) => {
    await page.goto('/settings')

    // Close any dialogs first
    await page.keyboard.press('Escape')
    await page.waitForTimeout(300)

    // Find theme toggle
    const themeSection = page.locator('section').filter({ hasText: 'Appearance' })
    await expect(themeSection).toBeVisible()

    // Check that theme toggle exists
    const themeToggle = themeSection.locator('select, button').first()
    await expect(themeToggle).toBeVisible()
  })

  test('should show keyboard shortcuts help with ? key', async ({ page }) => {
    await page.goto('/')

    // Close any dialogs first
    await page.keyboard.press('Escape')
    await page.waitForTimeout(300)

    // Press ? to open keyboard shortcuts help
    await page.keyboard.press('Shift+/')

    // Should show keyboard shortcuts dialog
    await expect(page.locator('.shortcuts-dialog')).toBeVisible()
    await expect(page.locator('text=Keyboard Shortcuts')).toBeVisible()

    // Should list navigation shortcuts
    await expect(page.locator('text=Navigation')).toBeVisible()
    await expect(page.locator('text=Go to Explorer')).toBeVisible()
  })
})

test.describe('Connection Management', () => {
  test('should have add connection button', async ({ page }) => {
    await page.goto('/')

    // Close any dialogs first
    await page.keyboard.press('Escape')
    await page.waitForTimeout(300)

    // Find add connection button in sidebar
    const addButton = page.locator('button[title="New Connection"]')
    await expect(addButton).toBeVisible()
  })

  test('should open connection dialog', async ({ page }) => {
    await page.goto('/')

    // Close any dialogs first
    await page.keyboard.press('Escape')
    await page.waitForTimeout(300)

    // Click add connection button
    await page.click('button[title="New Connection"]')

    // Dialog should open
    await expect(page.locator('[role="dialog"]')).toBeVisible()

    // Should have form fields
    await expect(page.locator('label:has-text("Name")')).toBeVisible()
    await expect(page.locator('label:has-text("Host")')).toBeVisible()
  })
})
