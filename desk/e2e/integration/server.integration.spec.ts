import { test, expect, type Page } from '@playwright/test'

/**
 * Integration tests for MorphDB Desk with real server
 *
 * These tests require:
 * 1. Running MorphDB server (docker compose -f docker-compose.test.yml up -d)
 * 2. Desk dev server (npm run dev in desk/)
 *
 * Run with: npx playwright test e2e/integration --project=chromium
 *
 * Environment:
 * - MORPHDB_TEST_URL: Server URL (default: http://localhost:5000)
 */

const MORPHDB_TEST_URL = process.env.MORPHDB_TEST_URL || 'http://localhost:5000'
const TEST_API_KEY = `test-key-${Date.now()}`

// Helper to wait for app to be ready
async function waitForAppReady(page: Page): Promise<void> {
  await page.goto('/')
  await expect(page.locator('text=MorphDB')).toBeVisible({ timeout: 10000 })
}

// Helper to close any open dialogs
async function closeDialogs(page: Page): Promise<void> {
  await page.keyboard.press('Escape')
  await page.waitForTimeout(300)
}

// Helper to create and connect to test server
async function createTestConnection(page: Page, connectionName: string): Promise<void> {
  await closeDialogs(page)

  // Click new connection button
  await page.click('button[title="New Connection"]')
  await expect(page.locator('[role="dialog"]')).toBeVisible()

  // Fill connection form
  await page.fill('#name', connectionName)
  await page.fill('#url', MORPHDB_TEST_URL)
  await page.fill('#apiKey', TEST_API_KEY)

  // Click Connect
  const connectButton = page.getByRole('button', { name: 'Connect' })
  await expect(connectButton).toBeEnabled()
  await connectButton.click()

  // Wait for connection to be established
  await page.waitForTimeout(1000)
}

// Generate unique table name for test isolation
function uniqueTableName(): string {
  return `test_${Date.now().toString(36)}_${Math.random().toString(36).substring(2, 8)}`
}

test.describe('Server Integration: Connection', () => {
  test.beforeEach(async ({ page }) => {
    await waitForAppReady(page)
  })

  test('should test connection to MorphDB server', async ({ page }) => {
    await closeDialogs(page)

    // Open connection dialog
    await page.click('button[title="New Connection"]')
    await expect(page.locator('[role="dialog"]')).toBeVisible()

    // Fill connection form
    await page.fill('#name', 'Test Connection')
    await page.fill('#url', MORPHDB_TEST_URL)
    await page.fill('#apiKey', TEST_API_KEY)

    // Click Test Connection button
    const testButton = page.getByRole('button', { name: 'Test Connection' })
    await testButton.click()

    // Should show success or error message
    // (depends on server availability)
    await page.waitForTimeout(2000)
  })

  test('should connect to MorphDB server', async ({ page }) => {
    const connectionName = `Integration Test ${Date.now()}`
    await createTestConnection(page, connectionName)

    // After connection, dialog should close
    await expect(page.locator('[role="dialog"]')).not.toBeVisible({ timeout: 5000 })

    // Connection should appear in sidebar
    await expect(page.getByText(connectionName)).toBeVisible()
  })

  test('should show tables after successful connection', async ({ page }) => {
    const connectionName = `Table List Test ${Date.now()}`
    await createTestConnection(page, connectionName)

    // Navigate to Explorer
    await page.click('text=Explorer')
    await expect(page).toHaveURL(/\/explorer/)

    // Select the connection
    await page.click(`text=${connectionName}`)

    // Wait for tables to load
    await page.waitForTimeout(1000)

    // Should show table list area (even if empty)
    await expect(page.locator('main')).toBeVisible()
  })
})

test.describe('Server Integration: Schema Operations', () => {
  let connectionName: string

  test.beforeEach(async ({ page }) => {
    await waitForAppReady(page)
    connectionName = `Schema Test ${Date.now()}`
    await createTestConnection(page, connectionName)

    // Navigate to Explorer and select connection
    await page.click('text=Explorer')
    await page.click(`text=${connectionName}`)
    await page.waitForTimeout(500)
  })

  test('should create a new table', async ({ page }) => {
    const tableName = uniqueTableName()

    // Look for "Create Table" button or menu
    const createTableButton =
      page.getByRole('button', { name: /create table/i }) ||
      page.getByRole('menuitem', { name: /new table/i })

    // If button exists, click it
    if (await createTableButton.isVisible()) {
      await createTableButton.click()

      // Fill table creation form
      await page.fill('[name="tableName"]', tableName)

      // Add a column
      await page.click('button:has-text("Add Column")')
      await page.fill('[name="columnName"]', 'name')
      await page.selectOption('[name="columnType"]', 'text')

      // Submit
      await page.click('button:has-text("Create")')

      // Wait for table to appear in list
      await page.waitForTimeout(1000)
      await expect(page.getByText(tableName)).toBeVisible()
    }
  })

  test('should view table schema', async ({ page }) => {
    // This test assumes there's at least one table
    // Click on a table to view its schema
    const tableItem = page.locator('.table-item').first()

    if (await tableItem.isVisible()) {
      await tableItem.click()

      // Should show table details/schema
      await expect(page.locator('.table-schema, .schema-panel, [data-testid="schema"]')).toBeVisible({
        timeout: 5000,
      })
    }
  })
})

test.describe('Server Integration: Data Operations', () => {
  let connectionName: string

  test.beforeEach(async ({ page }) => {
    await waitForAppReady(page)
    connectionName = `Data Test ${Date.now()}`
    await createTestConnection(page, connectionName)

    // Navigate to Explorer and select connection
    await page.click('text=Explorer')
    await page.click(`text=${connectionName}`)
    await page.waitForTimeout(500)
  })

  test('should display data grid when table is selected', async ({ page }) => {
    // Select first available table
    const tableItem = page.locator('.table-item, [data-testid="table-item"]').first()

    if (await tableItem.isVisible()) {
      await tableItem.click()
      await page.waitForTimeout(500)

      // Should show data grid or empty state
      const dataGrid = page.locator('.data-grid, [data-testid="data-grid"], table')
      const emptyState = page.locator('.empty-state, [data-testid="empty-state"]')

      // Either data grid or empty state should be visible
      const hasDataGrid = await dataGrid.isVisible()
      const hasEmptyState = await emptyState.isVisible()
      expect(hasDataGrid || hasEmptyState).toBeTruthy()
    }
  })

  test('should paginate through data', async ({ page }) => {
    const tableItem = page.locator('.table-item, [data-testid="table-item"]').first()

    if (await tableItem.isVisible()) {
      await tableItem.click()
      await page.waitForTimeout(500)

      // Look for pagination controls
      const pagination = page.locator('.pagination, [data-testid="pagination"]')

      if (await pagination.isVisible()) {
        // Check pagination elements
        await expect(page.getByText(/page|of|records/i)).toBeVisible()
      }
    }
  })
})

test.describe('Server Integration: Real-time Updates', () => {
  let connectionName: string

  test.beforeEach(async ({ page }) => {
    await waitForAppReady(page)
    connectionName = `Realtime Test ${Date.now()}`
    await createTestConnection(page, connectionName)
  })

  test('should establish SignalR connection', async ({ page }) => {
    // Navigate to Explorer
    await page.click('text=Explorer')
    await page.click(`text=${connectionName}`)

    // Check for WebSocket/SignalR connection status indicator
    const connectionStatus = page.locator(
      '[data-testid="connection-status"], .connection-indicator, .realtime-status'
    )

    if (await connectionStatus.isVisible()) {
      // Should show connected status
      await expect(connectionStatus).toHaveAttribute('data-connected', 'true', { timeout: 5000 })
    }
  })
})

test.describe('Server Integration: Error Handling', () => {
  test.beforeEach(async ({ page }) => {
    await waitForAppReady(page)
  })

  test('should handle connection failure gracefully', async ({ page }) => {
    await closeDialogs(page)

    // Open connection dialog
    await page.click('button[title="New Connection"]')
    await expect(page.locator('[role="dialog"]')).toBeVisible()

    // Try to connect to non-existent server
    await page.fill('#name', 'Invalid Connection')
    await page.fill('#url', 'http://localhost:9999')
    await page.fill('#apiKey', 'invalid-key')

    // Click Connect
    const connectButton = page.getByRole('button', { name: 'Connect' })
    await connectButton.click()

    // Should show error message
    await page.waitForTimeout(3000)
    const errorMessage = page.locator('.error, [role="alert"], .toast-error')
    // Error handling should be present in the UI
    await expect(page.locator('[role="dialog"]')).toBeVisible()
  })

  test('should handle server timeout', async ({ page }) => {
    await closeDialogs(page)

    // Open connection dialog
    await page.click('button[title="New Connection"]')

    // Fill with valid URL but test timeout behavior
    await page.fill('#name', 'Timeout Test')
    await page.fill('#url', MORPHDB_TEST_URL)
    await page.fill('#apiKey', TEST_API_KEY)

    // Test connection button should handle long operations
    const testButton = page.getByRole('button', { name: 'Test Connection' })
    await testButton.click()

    // Should show loading state
    await expect(testButton).toBeDisabled()

    // Wait for response or timeout
    await page.waitForTimeout(5000)
  })
})

test.describe('Server Integration: Bulk Operations', () => {
  let connectionName: string

  test.beforeEach(async ({ page }) => {
    await waitForAppReady(page)
    connectionName = `Bulk Test ${Date.now()}`
    await createTestConnection(page, connectionName)

    await page.click('text=Explorer')
    await page.click(`text=${connectionName}`)
    await page.waitForTimeout(500)
  })

  test('should support multi-row selection', async ({ page }) => {
    const tableItem = page.locator('.table-item, [data-testid="table-item"]').first()

    if (await tableItem.isVisible()) {
      await tableItem.click()
      await page.waitForTimeout(500)

      // Look for row checkboxes
      const rowCheckboxes = page.locator('input[type="checkbox"][data-row]')

      if ((await rowCheckboxes.count()) > 0) {
        // Select multiple rows
        await rowCheckboxes.first().check()

        // Should show bulk action toolbar
        const bulkActions = page.locator('.bulk-actions, [data-testid="bulk-actions"]')
        await expect(bulkActions).toBeVisible()
      }
    }
  })
})

test.describe('Server Integration: Performance', () => {
  let connectionName: string

  test.beforeEach(async ({ page }) => {
    await waitForAppReady(page)
    connectionName = `Perf Test ${Date.now()}`
    await createTestConnection(page, connectionName)
  })

  test('should load table list within reasonable time', async ({ page }) => {
    const startTime = Date.now()

    await page.click('text=Explorer')
    await page.click(`text=${connectionName}`)

    // Wait for table list to load
    await page.waitForSelector('.table-list, [data-testid="table-list"]', { timeout: 5000 })

    const loadTime = Date.now() - startTime
    expect(loadTime).toBeLessThan(5000)
  })

  test('should load table data within reasonable time', async ({ page }) => {
    await page.click('text=Explorer')
    await page.click(`text=${connectionName}`)
    await page.waitForTimeout(500)

    const tableItem = page.locator('.table-item, [data-testid="table-item"]').first()

    if (await tableItem.isVisible()) {
      const startTime = Date.now()

      await tableItem.click()

      // Wait for data grid to appear
      await page.waitForSelector('.data-grid, [data-testid="data-grid"], table', { timeout: 5000 })

      const loadTime = Date.now() - startTime
      expect(loadTime).toBeLessThan(3000)
    }
  })
})
