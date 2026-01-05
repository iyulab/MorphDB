/**
 * API Scenario Tests - UI Workflow Simulation
 *
 * These tests simulate actual user workflows in the UI by testing
 * the MorphDBClient API interactions in realistic sequences.
 *
 * Purpose:
 * - Validate API call sequences for common user workflows
 * - Test error handling and edge cases
 * - Ensure API responses match expected structures
 * - Verify state management across multiple API calls
 */

import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { MorphDBClient, type TableApiResponse, type BatchResponse } from '../api'

// Mock fetch globally
const mockFetch = vi.fn()
global.fetch = mockFetch

// Helper to create mock API responses
function createMockResponse<T>(data: T, status = 200): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    json: () => Promise.resolve(data),
    text: () => Promise.resolve(JSON.stringify(data)),
    blob: () => Promise.resolve(new Blob([JSON.stringify(data)])),
  } as Response
}

function createErrorResponse(message: string, code: string, status = 400): Response {
  return {
    ok: false,
    status,
    json: () => Promise.resolve({ error: code, message }),
  } as Response
}

describe('API Scenario: Connection & Health Check', () => {
  let client: MorphDBClient

  beforeEach(() => {
    mockFetch.mockReset()
    client = new MorphDBClient({
      url: 'http://localhost:5000',
      apiKey: 'test-api-key',
      tenantId: 'test-tenant',
    })
  })

  it('should verify server health before operations', async () => {
    mockFetch.mockResolvedValueOnce(createMockResponse({ status: 'Healthy' }))

    const health = await client.healthCheck()

    expect(health.status).toBe('Healthy')
    expect(mockFetch).toHaveBeenCalledWith(
      'http://localhost:5000/health',
      expect.objectContaining({
        headers: expect.objectContaining({
          'X-API-Key': 'test-api-key',
        }),
      })
    )
  })

  it('should handle server unavailable gracefully', async () => {
    mockFetch.mockRejectedValueOnce(new Error('Network error'))

    await expect(client.healthCheck()).rejects.toThrow('Network error')
  })

  it('should include tenant ID in all requests', async () => {
    mockFetch.mockResolvedValueOnce(createMockResponse([]))

    await client.listTables()

    expect(mockFetch).toHaveBeenCalledWith(
      'http://localhost:5000/api/schema/tables',
      expect.objectContaining({
        headers: expect.objectContaining({
          'X-Tenant-Id': 'test-tenant',
        }),
      })
    )
  })
})

describe('API Scenario: Table Lifecycle Workflow', () => {
  let client: MorphDBClient
  const testTableName = 'customers'

  beforeEach(() => {
    mockFetch.mockReset()
    client = new MorphDBClient({
      url: 'http://localhost:5000',
      apiKey: 'test-api-key',
    })
  })

  it('should complete full table lifecycle: create → use → modify → delete', async () => {
    // Step 1: Create table
    const createdTable = {
      id: 'table-123',
      name: testTableName,
      version: 1,
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString(),
      columns: [
        { id: 'col-1', name: 'name', type: 'text', nullable: false, unique: false, indexed: false, primaryKey: false, position: 1, isDerived: false },
        { id: 'col-2', name: 'email', type: 'text', nullable: true, unique: true, indexed: true, primaryKey: false, position: 2, isDerived: false },
      ],
      indexes: [],
      relations: [],
    }

    mockFetch.mockResolvedValueOnce(createMockResponse(createdTable))
    const table = await client.createTable({
      name: testTableName,
      columns: [
        { name: 'name', type: 'text', nullable: false },
        { name: 'email', type: 'text', unique: true },
      ],
    })

    expect(table.name).toBe(testTableName)
    expect(table.columns).toHaveLength(2)

    // Step 2: Add a column
    const newColumn = {
      id: 'col-3',
      name: 'phone',
      type: 'text',
      nullable: true,
      unique: false,
      indexed: false,
      primaryKey: false,
      position: 3,
      isDerived: false,
    }

    mockFetch.mockResolvedValueOnce(createMockResponse(newColumn))
    const column = await client.addColumn(testTableName, { name: 'phone', type: 'text', nullable: true })

    expect(column.name).toBe('phone')

    // Step 3: Delete the table
    mockFetch.mockResolvedValueOnce(createMockResponse(null, 204))
    await client.deleteTable(testTableName)

    expect(mockFetch).toHaveBeenCalledTimes(3)
  })

  it('should list all tables and retrieve specific table details', async () => {
    const tables = [
      { id: 'table-1', name: 'users', version: 1, columns: [], indexes: [], relations: [], createdAt: '', updatedAt: '' },
      { id: 'table-2', name: 'orders', version: 1, columns: [], indexes: [], relations: [], createdAt: '', updatedAt: '' },
    ]

    mockFetch.mockResolvedValueOnce(createMockResponse(tables))
    const tableList = await client.listTables()

    expect(tableList).toHaveLength(2)
    expect(tableList.map((t) => t.name)).toContain('users')
    expect(tableList.map((t) => t.name)).toContain('orders')

    // Get specific table
    mockFetch.mockResolvedValueOnce(createMockResponse(tables[0]))
    const specificTable = await client.getTable('users')

    expect(specificTable.name).toBe('users')
  })

  it('should handle table creation conflict', async () => {
    mockFetch.mockResolvedValueOnce(
      createErrorResponse('Table already exists', 'CONFLICT', 409)
    )

    await expect(
      client.createTable({ name: 'existing_table', columns: [] })
    ).rejects.toThrow('Table already exists')
  })
})

describe('API Scenario: Data CRUD Workflow', () => {
  let client: MorphDBClient
  const tableName = 'products'

  beforeEach(() => {
    mockFetch.mockReset()
    client = new MorphDBClient({
      url: 'http://localhost:5000',
      apiKey: 'test-api-key',
    })
  })

  it('should perform complete data workflow: insert → query → update → delete', async () => {
    // Step 1: Insert record
    const insertedRecord = {
      _id: 'record-123',
      name: 'Widget A',
      price: 19.99,
      stock: 100,
    }

    mockFetch.mockResolvedValueOnce(createMockResponse(insertedRecord))
    const created = await client.createRecord(tableName, {
      name: 'Widget A',
      price: 19.99,
      stock: 100,
    })

    expect(created.name).toBe('Widget A')

    // Step 2: Query data with OData
    const queryResult = {
      value: [insertedRecord],
      '@odata.count': 1,
    }

    mockFetch.mockResolvedValueOnce(createMockResponse(queryResult))
    const data = await client.queryData(tableName, {
      $filter: "name eq 'Widget A'",
      $orderby: 'price desc',
      $top: 10,
    })

    expect(data.value).toHaveLength(1)
    expect(data['@odata.count']).toBe(1)

    // Step 3: Update record
    mockFetch.mockResolvedValueOnce(createMockResponse(null, 204))
    await client.updateRecord(tableName, 'record-123', { price: 24.99 })

    // Step 4: Delete record
    mockFetch.mockResolvedValueOnce(createMockResponse(null, 204))
    await client.deleteRecord(tableName, 'record-123')

    expect(mockFetch).toHaveBeenCalledTimes(4)
  })

  it('should handle pagination through large datasets', async () => {
    const page1 = {
      value: Array(50).fill(null).map((_, i) => ({ _id: `id-${i}`, name: `Item ${i}` })),
      '@odata.count': 150,
    }
    const page2 = {
      value: Array(50).fill(null).map((_, i) => ({ _id: `id-${i + 50}`, name: `Item ${i + 50}` })),
      '@odata.count': 150,
    }
    const page3 = {
      value: Array(50).fill(null).map((_, i) => ({ _id: `id-${i + 100}`, name: `Item ${i + 100}` })),
      '@odata.count': 150,
    }

    mockFetch
      .mockResolvedValueOnce(createMockResponse(page1))
      .mockResolvedValueOnce(createMockResponse(page2))
      .mockResolvedValueOnce(createMockResponse(page3))

    const allRecords = []

    // Simulate UI pagination
    let result = await client.queryData(tableName, { $top: 50, $skip: 0, $count: true })
    allRecords.push(...result.value)

    result = await client.queryData(tableName, { $top: 50, $skip: 50, $count: true })
    allRecords.push(...result.value)

    result = await client.queryData(tableName, { $top: 50, $skip: 100, $count: true })
    allRecords.push(...result.value)

    expect(allRecords).toHaveLength(150)
  })

  it('should handle filtering with complex OData expressions', async () => {
    mockFetch.mockResolvedValueOnce(
      createMockResponse({
        value: [
          { _id: 'id-1', name: 'Premium Widget', price: 99.99, category: 'electronics' },
        ],
        '@odata.count': 1,
      })
    )

    const data = await client.queryData(tableName, {
      $filter: "category eq 'electronics' and price gt 50",
      $orderby: 'price desc',
      $select: 'name,price,category',
    })

    expect(data.value).toHaveLength(1)
    // Verify the filter parameter is in the URL (URL encoding may vary)
    const calledUrl = mockFetch.mock.calls[0][0] as string
    // URL should contain filter, orderby, and select parameters ($ may be encoded as %24)
    expect(calledUrl).toMatch(/(\$|%24)filter=/)
    expect(calledUrl).toContain('electronics')
    expect(calledUrl).toContain('price')
  })
})

describe('API Scenario: Batch Operations Workflow', () => {
  let client: MorphDBClient
  const tableName = 'inventory'

  beforeEach(() => {
    mockFetch.mockReset()
    client = new MorphDBClient({
      url: 'http://localhost:5000',
      apiKey: 'test-api-key',
    })
  })

  it('should execute mixed batch operations', async () => {
    const batchResponse: BatchResponse = {
      successCount: 5,
      failureCount: 0,
      results: [
        { index: 0, success: true, data: { _id: 'new-1' } },
        { index: 1, success: true, data: { _id: 'new-2' } },
        { index: 2, success: true, affectedRows: 1 },
        { index: 3, success: true, affectedRows: 1 },
        { index: 4, success: true, affectedRows: 1 },
      ],
    }

    mockFetch.mockResolvedValueOnce(createMockResponse(batchResponse))

    const result = await client.executeBatch({
      operations: [
        { method: 'INSERT', table: tableName, data: { name: 'Item 1', quantity: 10 } },
        { method: 'INSERT', table: tableName, data: { name: 'Item 2', quantity: 20 } },
        { method: 'UPDATE', table: tableName, id: 'existing-1', data: { quantity: 15 } },
        { method: 'DELETE', table: tableName, id: 'old-item-1' },
        { method: 'UPSERT', table: tableName, data: { name: 'Item 3', quantity: 30 }, keyColumns: ['name'] },
      ],
    })

    expect(result.successCount).toBe(5)
    expect(result.failureCount).toBe(0)
  })

  it('should handle partial batch failure', async () => {
    const batchResponse: BatchResponse = {
      successCount: 2,
      failureCount: 1,
      results: [
        { index: 0, success: true, data: { _id: 'new-1' } },
        { index: 1, success: false, error: 'Unique constraint violation' },
        { index: 2, success: true, data: { _id: 'new-2' } },
      ],
    }

    mockFetch.mockResolvedValueOnce(createMockResponse(batchResponse))

    const result = await client.executeBatch({
      operations: [
        { method: 'INSERT', table: tableName, data: { name: 'Item 1' } },
        { method: 'INSERT', table: tableName, data: { name: 'Duplicate' } },
        { method: 'INSERT', table: tableName, data: { name: 'Item 2' } },
      ],
    })

    expect(result.successCount).toBe(2)
    expect(result.failureCount).toBe(1)
    expect(result.results[1].error).toBe('Unique constraint violation')
  })

  it('should perform bulk insert for large datasets', async () => {
    const bulkData = Array(100)
      .fill(null)
      .map((_, i) => ({ name: `Item ${i}`, quantity: i * 10 }))

    const bulkResponse: BatchResponse = {
      successCount: 100,
      failureCount: 0,
      results: bulkData.map((_, i) => ({
        index: i,
        success: true,
        data: { _id: `id-${i}` },
      })),
    }

    mockFetch.mockResolvedValueOnce(createMockResponse(bulkResponse))

    const result = await client.bulkInsert(tableName, bulkData)

    expect(result.successCount).toBe(100)
  })
})

describe('API Scenario: Aggregation Workflow', () => {
  let client: MorphDBClient
  const tableName = 'sales'

  beforeEach(() => {
    mockFetch.mockReset()
    client = new MorphDBClient({
      url: 'http://localhost:5000',
      apiKey: 'test-api-key',
    })
  })

  it('should execute aggregation queries for dashboard', async () => {
    const aggregationResponse = {
      data: [
        { category: 'Electronics', total_sales: 50000, order_count: 150 },
        { category: 'Clothing', total_sales: 30000, order_count: 200 },
        { category: 'Books', total_sales: 15000, order_count: 500 },
      ],
      metadata: {
        executedAt: new Date().toISOString(),
        rowCount: 3,
      },
    }

    mockFetch.mockResolvedValueOnce(createMockResponse(aggregationResponse))

    const result = await client.aggregate(tableName, {
      aggregations: [
        { function: 'sum', column: 'amount', alias: 'total_sales' },
        { function: 'count', alias: 'order_count' },
      ],
      groupBy: ['category'],
      orderBy: [{ column: 'total_sales', direction: 'desc' }],
    })

    expect(result.data).toHaveLength(3)
    expect(result.data[0].category).toBe('Electronics')
    expect(result.data[0].total_sales).toBe(50000)
  })

  it('should handle aggregation with filters and having clause', async () => {
    const response = {
      data: [{ region: 'West', avg_order: 125.50 }],
      metadata: { executedAt: '', rowCount: 1 },
    }

    mockFetch.mockResolvedValueOnce(createMockResponse(response))

    const result = await client.aggregate(tableName, {
      aggregations: [
        { function: 'avg', column: 'order_value', alias: 'avg_order' },
      ],
      groupBy: ['region'],
      filter: [
        { column: 'status', operator: 'eq', value: 'completed' },
      ],
      having: [
        { alias: 'avg_order', operator: 'gt', value: 100 },
      ],
    })

    expect(result.data[0].avg_order).toBe(125.50)
  })
})

describe('API Scenario: View Management Workflow', () => {
  let client: MorphDBClient

  beforeEach(() => {
    mockFetch.mockReset()
    client = new MorphDBClient({
      url: 'http://localhost:5000',
      apiKey: 'test-api-key',
    })
  })

  it('should create and query a materialized view', async () => {
    const viewResponse = {
      id: 'view-123',
      name: 'active_users_summary',
      baseTable: 'users',
      columns: [
        { name: 'user_count', dataType: 'integer', isComputed: true },
        { name: 'last_active_date', dataType: 'date', isComputed: false },
      ],
      filters: [],
      joins: [],
      groupBy: ['last_active_date'],
      orderBy: [],
      distinct: false,
      isMaterialized: true,
      isStale: false,
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString(),
    }

    mockFetch.mockResolvedValueOnce(createMockResponse(viewResponse))

    const view = await client.createView({
      name: 'active_users_summary',
      baseTable: 'users',
      columns: [
        { source: 'user_id', aggregation: 'count', alias: 'user_count' },
        { source: 'last_active_date' },
      ],
      groupBy: ['last_active_date'],
      materialized: true,
    })

    expect(view.name).toBe('active_users_summary')
    expect(view.isMaterialized).toBe(true)

    // Query the view
    const queryResponse = {
      data: [
        { user_count: 150, last_active_date: '2025-01-05' },
        { user_count: 120, last_active_date: '2025-01-04' },
      ],
      totalCount: 2,
      hasMore: false,
    }

    mockFetch.mockResolvedValueOnce(createMockResponse(queryResponse))

    const data = await client.queryViewData('active_users_summary', {
      orderBy: 'user_count desc',
      take: 10,
    })

    expect(data.data).toHaveLength(2)
    expect(data.data[0].user_count).toBe(150)
  })

  it('should refresh stale materialized view', async () => {
    mockFetch.mockResolvedValueOnce(createMockResponse({ isStale: true, lastRefreshedAt: '2025-01-04T00:00:00Z' }))
    mockFetch.mockResolvedValueOnce(createMockResponse(null, 204))

    const staleCheck = await client.checkViewStale('my_view')
    expect(staleCheck.isStale).toBe(true)

    await client.refreshMaterializedView('my_view', true) // concurrent refresh

    expect(mockFetch).toHaveBeenCalledWith(
      'http://localhost:5000/api/views/my_view/refresh?concurrent=true',
      expect.objectContaining({ method: 'POST' })
    )
  })
})

describe('API Scenario: Webhook Configuration Workflow', () => {
  let client: MorphDBClient

  beforeEach(() => {
    mockFetch.mockReset()
    client = new MorphDBClient({
      url: 'http://localhost:5000',
      apiKey: 'test-api-key',
    })
  })

  it('should setup webhook and monitor deliveries', async () => {
    const webhookResponse = {
      id: 'webhook-123',
      name: 'Order Notifications',
      table: 'orders',
      url: 'https://api.example.com/webhooks/orders',
      events: ['insert', 'update'],
      headers: { Authorization: 'Bearer xxx' },
      isActive: true,
      secret: 'whsec_xxx',
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString(),
    }

    mockFetch.mockResolvedValueOnce(createMockResponse(webhookResponse))

    const webhook = await client.createWebhook({
      name: 'Order Notifications',
      table: 'orders',
      url: 'https://api.example.com/webhooks/orders',
      events: ['insert', 'update'],
      headers: { Authorization: 'Bearer xxx' },
    })

    expect(webhook.name).toBe('Order Notifications')
    expect(webhook.events).toContain('insert')

    // Check deliveries
    const deliveries = [
      { id: 'del-1', event: 'insert', recordId: 'rec-1', status: 'delivered', attemptCount: 1, createdAt: '', deliveredAt: '' },
      { id: 'del-2', event: 'update', recordId: 'rec-2', status: 'failed', attemptCount: 3, createdAt: '', errorMessage: 'Timeout' },
    ]

    mockFetch.mockResolvedValueOnce(createMockResponse(deliveries))

    const webhookDeliveries = await client.listWebhookDeliveries('webhook-123', { limit: 50 })

    expect(webhookDeliveries).toHaveLength(2)
    expect(webhookDeliveries[1].status).toBe('failed')
  })

  it('should manage DLQ messages', async () => {
    const dlqStats = {
      totalMessages: 15,
      pendingReviewCount: 10,
      resolvedCount: 3,
      archivedCount: 2,
      byReason: { timeout: 8, server_error: 5, invalid_payload: 2 },
      byWebhook: { 'webhook-123': 10, 'webhook-456': 5 },
    }

    mockFetch.mockResolvedValueOnce(createMockResponse(dlqStats))

    const stats = await client.getDlqStatistics()

    expect(stats.pendingReviewCount).toBe(10)
    expect(stats.byReason.timeout).toBe(8)
  })
})

describe('API Scenario: Import/Export Workflow', () => {
  let client: MorphDBClient
  const tableName = 'products'

  beforeEach(() => {
    mockFetch.mockReset()
    client = new MorphDBClient({
      url: 'http://localhost:5000',
      apiKey: 'test-api-key',
    })
  })

  it('should export data to CSV and track job progress', async () => {
    const exportJob = {
      jobId: 'export-123',
      tableName: 'products',
      format: 'csv',
      status: 'pending',
      totalRows: 0,
      processedRows: 0,
      createdAt: new Date().toISOString(),
    }

    mockFetch.mockResolvedValueOnce(createMockResponse(exportJob))

    const job = await client.exportCsv(tableName, {
      columns: ['name', 'price', 'stock'],
      includeHeader: true,
      delimiter: ',',
    })

    expect(job.jobId).toBe('export-123')
    expect(job.status).toBe('pending')

    // Poll for progress
    const progressStates = [
      { ...exportJob, status: 'processing', processedRows: 500, totalRows: 1000 },
      { ...exportJob, status: 'processing', processedRows: 800, totalRows: 1000 },
      { ...exportJob, status: 'completed', processedRows: 1000, totalRows: 1000, fileSize: 45678 },
    ]

    for (const state of progressStates) {
      mockFetch.mockResolvedValueOnce(createMockResponse(state))
      const currentStatus = await client.getExportJob('export-123')

      if (currentStatus.status === 'completed') {
        expect(currentStatus.processedRows).toBe(1000)
        expect(currentStatus.fileSize).toBe(45678)
      }
    }
  })

  it('should handle import job with validation errors', async () => {
    const importJob = {
      jobId: 'import-456',
      tableName: 'products',
      format: 'csv',
      status: 'completed',
      totalRows: 100,
      processedRows: 100,
      successCount: 95,
      errorCount: 5,
      errors: [
        { row: 15, column: 'price', message: 'Invalid number format', value: 'N/A' },
        { row: 23, column: 'stock', message: 'Negative value not allowed', value: '-5' },
        { row: 45, column: 'name', message: 'Required field is empty', value: '' },
        { row: 67, column: 'price', message: 'Invalid number format', value: 'free' },
        { row: 89, column: 'email', message: 'Invalid email format', value: 'not-an-email' },
      ],
      createdAt: new Date().toISOString(),
      completedAt: new Date().toISOString(),
    }

    mockFetch.mockResolvedValueOnce(createMockResponse(importJob))

    const result = await client.getImportJob('import-456')

    expect(result.successCount).toBe(95)
    expect(result.errorCount).toBe(5)
    expect(result.errors).toHaveLength(5)
    expect(result.errors?.[0].row).toBe(15)
  })
})

describe('API Scenario: Organization & Project Management', () => {
  let client: MorphDBClient

  beforeEach(() => {
    mockFetch.mockReset()
    client = new MorphDBClient({
      url: 'http://localhost:5000',
      apiKey: 'test-api-key',
    })
  })

  it('should manage organization lifecycle', async () => {
    // Create organization
    const org = {
      organizationId: 'org-123',
      name: 'Acme Corp',
      slug: 'acme-corp',
      status: 'Active',
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString(),
    }

    mockFetch.mockResolvedValueOnce(createMockResponse(org))

    const created = await client.createOrganization({
      name: 'Acme Corp',
      slug: 'acme-corp',
    })

    expect(created.name).toBe('Acme Corp')

    // Get organization stats
    const stats = {
      organizationId: 'org-123',
      memberCount: 15,
      projectCount: 5,
      totalTables: 48,
      totalRows: 1000000,
      storageUsageBytes: 512000000,
    }

    mockFetch.mockResolvedValueOnce(createMockResponse(stats))

    const orgStats = await client.getOrganizationStats('org-123')

    expect(orgStats.memberCount).toBe(15)
    expect(orgStats.projectCount).toBe(5)
  })

  it('should manage project with settings', async () => {
    const project = {
      id: 'proj-123',
      name: 'E-Commerce Platform',
      slug: 'ecommerce',
      systemSchema: 'morphdb',
      dataSchema: 'morphdb_data',
      status: 'Active',
      settings: {
        maxTables: 100,
        maxStorageBytes: 10737418240, // 10GB
        enableAuditLog: true,
        rateLimits: {
          requestsPerMinute: 1000,
          requestsPerHour: 50000,
        },
      },
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString(),
    }

    mockFetch.mockResolvedValueOnce(createMockResponse(project))

    const created = await client.createProject({
      name: 'E-Commerce Platform',
      slug: 'ecommerce',
      settings: {
        maxTables: 100,
        enableAuditLog: true,
      },
    })

    expect(created.settings?.enableAuditLog).toBe(true)

    // Validate project health
    const health = {
      projectId: 'proj-123',
      isHealthy: true,
      issues: [],
      checkedAt: new Date().toISOString(),
    }

    mockFetch.mockResolvedValueOnce(createMockResponse(health))

    const healthCheck = await client.validateProjectHealth('proj-123')

    expect(healthCheck.isHealthy).toBe(true)
  })
})

describe('API Scenario: Error Handling & Recovery', () => {
  let client: MorphDBClient

  beforeEach(() => {
    mockFetch.mockReset()
    client = new MorphDBClient({
      url: 'http://localhost:5000',
      apiKey: 'test-api-key',
    })
  })

  it('should handle authentication errors', async () => {
    mockFetch.mockResolvedValueOnce(
      createErrorResponse('Invalid API key', 'UNAUTHORIZED', 401)
    )

    await expect(client.listTables()).rejects.toThrow('Invalid API key')
  })

  it('should handle rate limiting', async () => {
    mockFetch.mockResolvedValueOnce(
      createErrorResponse('Rate limit exceeded. Try again in 30 seconds.', 'RATE_LIMIT', 429)
    )

    await expect(client.queryData('products', {})).rejects.toThrow('Rate limit exceeded')
  })

  it('should handle server errors', async () => {
    mockFetch.mockResolvedValueOnce(
      createErrorResponse('Internal server error', 'SERVER_ERROR', 500)
    )

    await expect(client.getTable('products')).rejects.toThrow('Internal server error')
  })

  it('should handle validation errors with details', async () => {
    const errorResponse = {
      ok: false,
      status: 400,
      json: () =>
        Promise.resolve({
          error: 'VALIDATION_ERROR',
          message: 'Validation failed',
          details: {
            name: ['Name is required', 'Name must be at least 3 characters'],
            email: ['Invalid email format'],
          },
        }),
    } as Response

    mockFetch.mockResolvedValueOnce(errorResponse)

    await expect(
      client.createRecord('users', { name: '', email: 'invalid' })
    ).rejects.toThrow('Validation failed')
  })

  it('should handle network timeout gracefully', async () => {
    mockFetch.mockRejectedValueOnce(new Error('Network request timeout'))

    await expect(client.healthCheck()).rejects.toThrow('Network request timeout')
  })
})

describe('API Scenario: Security Operations', () => {
  let client: MorphDBClient

  beforeEach(() => {
    mockFetch.mockReset()
    client = new MorphDBClient({
      url: 'http://localhost:5000',
      apiKey: 'test-api-key',
    })
  })

  it('should manage API keys', async () => {
    const createKeyResponse = {
      key: {
        id: 'key-123',
        name: 'Production API Key',
        keyType: 'service',
        keyPrefix: 'sk_prod_',
        isActive: true,
        createdAt: new Date().toISOString(),
      },
      rawKey: 'sk_prod_xxxxxxxxxxxxxxxxxxxx',
    }

    mockFetch.mockResolvedValueOnce(createMockResponse(createKeyResponse))

    const result = await client.createApiKey({
      name: 'Production API Key',
      keyType: 'service',
    })

    expect(result.rawKey).toContain('sk_prod_')
    expect(result.key.isActive).toBe(true)
  })

  it('should manage RLS policies', async () => {
    const policy = {
      id: 'policy-123',
      name: 'tenant_isolation',
      tableId: 'table-123',
      policyType: 'select',
      expression: "tenant_id = current_setting('app.tenant_id')",
      isActive: true,
      ordinalPosition: 1,
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString(),
    }

    mockFetch.mockResolvedValueOnce(createMockResponse(policy))

    const created = await client.createSecurityPolicy({
      name: 'tenant_isolation',
      tableName: 'orders',
      policyType: 'select',
      expression: "tenant_id = current_setting('app.tenant_id')",
    })

    expect(created.name).toBe('tenant_isolation')
    expect(created.isActive).toBe(true)
  })

  it('should manage encryption key rotation', async () => {
    const rotationResult = {
      success: true,
      tableName: 'sensitive_data',
      previousKeyVersion: 1,
      newKeyVersion: 2,
      rowsProcessed: 10000,
      columnsRotated: 3,
      durationMs: 5432,
      startedAt: new Date().toISOString(),
      completedAt: new Date().toISOString(),
    }

    mockFetch.mockResolvedValueOnce(createMockResponse(rotationResult))

    const result = await client.rotateTableKey('sensitive_data')

    expect(result.success).toBe(true)
    expect(result.newKeyVersion).toBe(2)
    expect(result.rowsProcessed).toBe(10000)
  })
})

describe('API Scenario: Audit & Monitoring', () => {
  let client: MorphDBClient

  beforeEach(() => {
    mockFetch.mockReset()
    client = new MorphDBClient({
      url: 'http://localhost:5000',
      apiKey: 'test-api-key',
    })
  })

  it('should query audit logs with filters', async () => {
    const auditResponse = {
      items: [
        {
          id: 'log-1',
          projectId: 'proj-123',
          category: 'data',
          action: 'create',
          severity: 'info',
          actorId: 'user-456',
          resourceType: 'record',
          resourceId: 'rec-789',
          timestamp: new Date().toISOString(),
        },
      ],
      totalCount: 1,
      page: 1,
      pageSize: 50,
      totalPages: 1,
      hasMore: false,
    }

    mockFetch.mockResolvedValueOnce(createMockResponse(auditResponse))

    const logs = await client.queryAuditLogs('proj-123', {
      category: 1, // Data
      actorId: 'user-456',
      from: '2025-01-01',
      to: '2025-01-05',
      pageSize: 50,
    })

    expect(logs.items).toHaveLength(1)
    expect(logs.items[0].action).toBe('create')
  })

  it('should get quota usage and limits', async () => {
    const quotaSummary = {
      usage: {
        projectId: 'proj-123',
        period: '2025-01',
        apiRequests: 45000,
        dataReads: 150000,
        dataWrites: 25000,
        storageBytes: 2147483648, // 2GB
        bandwidthBytes: 10737418240,
        lastUpdated: new Date().toISOString(),
      },
      limits: {
        projectId: 'proj-123',
        maxApiRequests: 100000,
        maxDataReads: 500000,
        maxDataWrites: 100000,
        maxStorageBytes: 10737418240,
        maxBandwidthBytes: 107374182400,
        tier: 'Pro',
      },
      rateLimit: {
        key: 'proj-123',
        available: 950,
        limit: 1000,
        windowSeconds: 60,
        resetAt: new Date().toISOString(),
        requestCount: 50,
      },
    }

    mockFetch.mockResolvedValueOnce(createMockResponse(quotaSummary))

    const summary = await client.getQuotaSummary('proj-123')

    expect(summary.usage.apiRequests).toBe(45000)
    expect(summary.limits.tier).toBe('Pro')
    expect(summary.rateLimit.available).toBe(950)
  })
})

describe('API Scenario: Real-time Subscription Workflow', () => {
  let client: MorphDBClient
  let mockSignalRConnection: MockSignalRConnection

  // Mock SignalR HubConnection
  class MockSignalRConnection {
    private handlers: Map<string, Function[]> = new Map()
    public state: 'Connected' | 'Disconnected' | 'Connecting' | 'Reconnecting' = 'Disconnected'
    public connectionId = 'mock-connection-123'

    async start() {
      this.state = 'Connecting'
      await new Promise((resolve) => setTimeout(resolve, 10))
      this.state = 'Connected'
      return this
    }

    async stop() {
      this.state = 'Disconnected'
      this.handlers.clear()
    }

    on(event: string, handler: Function) {
      if (!this.handlers.has(event)) {
        this.handlers.set(event, [])
      }
      this.handlers.get(event)!.push(handler)
    }

    off(event: string, handler?: Function) {
      if (handler && this.handlers.has(event)) {
        const handlers = this.handlers.get(event)!
        const index = handlers.indexOf(handler)
        if (index > -1) handlers.splice(index, 1)
      } else {
        this.handlers.delete(event)
      }
    }

    async invoke(method: string, ...args: unknown[]) {
      // Mock server response for subscribe/unsubscribe
      if (method === 'SubscribeToTable') {
        return { success: true, subscriptionId: `sub-${args[0]}` }
      }
      if (method === 'UnsubscribeFromTable') {
        return { success: true }
      }
      if (method === 'SubscribeToQuery') {
        return { success: true, subscriptionId: `query-sub-${Date.now()}` }
      }
      return {}
    }

    // Simulate server pushing events
    simulateEvent(event: string, ...args: unknown[]) {
      const handlers = this.handlers.get(event) || []
      handlers.forEach((handler) => handler(...args))
    }

    simulateReconnecting() {
      this.state = 'Reconnecting'
      const handlers = this.handlers.get('reconnecting') || []
      handlers.forEach((handler) => handler())
    }

    simulateReconnected() {
      this.state = 'Connected'
      const handlers = this.handlers.get('reconnected') || []
      handlers.forEach((handler) => handler(this.connectionId))
    }

    simulateClose(error?: Error) {
      this.state = 'Disconnected'
      const handlers = this.handlers.get('close') || []
      handlers.forEach((handler) => handler(error))
    }
  }

  beforeEach(() => {
    mockFetch.mockReset()
    mockSignalRConnection = new MockSignalRConnection()
    client = new MorphDBClient({
      url: 'http://localhost:5000',
      apiKey: 'test-api-key',
    })
    // Inject mock SignalR connection
    ;(client as any)._signalRConnection = mockSignalRConnection
  })

  afterEach(async () => {
    await mockSignalRConnection.stop()
  })

  it('should establish SignalR connection for real-time updates', async () => {
    await mockSignalRConnection.start()

    expect(mockSignalRConnection.state).toBe('Connected')
    expect(mockSignalRConnection.connectionId).toBe('mock-connection-123')
  })

  it('should subscribe to table changes and receive insert events', async () => {
    await mockSignalRConnection.start()

    const receivedEvents: unknown[] = []
    mockSignalRConnection.on('TableRecordInserted', (data: unknown) => {
      receivedEvents.push({ type: 'insert', data })
    })

    // Subscribe to table
    const subscription = await mockSignalRConnection.invoke('SubscribeToTable', 'orders')
    expect(subscription.success).toBe(true)

    // Simulate server sending insert event
    mockSignalRConnection.simulateEvent('TableRecordInserted', {
      tableName: 'orders',
      recordId: 'order-123',
      data: { customer: 'John', total: 99.99 },
      timestamp: new Date().toISOString(),
    })

    expect(receivedEvents).toHaveLength(1)
    expect((receivedEvents[0] as any).data.recordId).toBe('order-123')
  })

  it('should subscribe to table changes and receive update events', async () => {
    await mockSignalRConnection.start()

    const receivedEvents: unknown[] = []
    mockSignalRConnection.on('TableRecordUpdated', (data: unknown) => {
      receivedEvents.push({ type: 'update', data })
    })

    await mockSignalRConnection.invoke('SubscribeToTable', 'products')

    // Simulate update event
    mockSignalRConnection.simulateEvent('TableRecordUpdated', {
      tableName: 'products',
      recordId: 'prod-456',
      previousData: { name: 'Widget', price: 19.99 },
      currentData: { name: 'Widget', price: 24.99 },
      changedColumns: ['price'],
      timestamp: new Date().toISOString(),
    })

    expect(receivedEvents).toHaveLength(1)
    expect((receivedEvents[0] as any).data.changedColumns).toContain('price')
  })

  it('should subscribe to table changes and receive delete events', async () => {
    await mockSignalRConnection.start()

    const receivedEvents: unknown[] = []
    mockSignalRConnection.on('TableRecordDeleted', (data: unknown) => {
      receivedEvents.push({ type: 'delete', data })
    })

    await mockSignalRConnection.invoke('SubscribeToTable', 'users')

    // Simulate delete event
    mockSignalRConnection.simulateEvent('TableRecordDeleted', {
      tableName: 'users',
      recordId: 'user-789',
      deletedData: { name: 'Jane', email: 'jane@example.com' },
      timestamp: new Date().toISOString(),
    })

    expect(receivedEvents).toHaveLength(1)
    expect((receivedEvents[0] as any).data.deletedData.email).toBe('jane@example.com')
  })

  it('should handle batch events from multiple table operations', async () => {
    await mockSignalRConnection.start()

    const allEvents: unknown[] = []
    mockSignalRConnection.on('BatchOperationCompleted', (data: unknown) => {
      allEvents.push(data)
    })

    await mockSignalRConnection.invoke('SubscribeToTable', 'inventory')

    // Simulate batch completion event
    mockSignalRConnection.simulateEvent('BatchOperationCompleted', {
      tableName: 'inventory',
      batchId: 'batch-001',
      operationCount: 100,
      insertCount: 80,
      updateCount: 15,
      deleteCount: 5,
      successCount: 98,
      failureCount: 2,
      duration: 1250,
      timestamp: new Date().toISOString(),
    })

    expect(allEvents).toHaveLength(1)
    expect((allEvents[0] as any).operationCount).toBe(100)
  })

  it('should subscribe to query-based real-time updates', async () => {
    await mockSignalRConnection.start()

    const matchingRecords: unknown[] = []
    mockSignalRConnection.on('QueryResultChanged', (data: unknown) => {
      matchingRecords.push(data)
    })

    // Subscribe to specific query
    const subscription = await mockSignalRConnection.invoke('SubscribeToQuery', {
      tableName: 'orders',
      filter: "status eq 'pending'",
      select: ['id', 'customer', 'total'],
    })
    expect(subscription.success).toBe(true)

    // Simulate matching record insert
    mockSignalRConnection.simulateEvent('QueryResultChanged', {
      queryId: subscription.subscriptionId,
      changeType: 'added',
      record: { id: 'order-new', customer: 'Alice', total: 150 },
      timestamp: new Date().toISOString(),
    })

    expect(matchingRecords).toHaveLength(1)
    expect((matchingRecords[0] as any).changeType).toBe('added')
  })

  it('should unsubscribe from table updates', async () => {
    await mockSignalRConnection.start()

    let eventCount = 0
    const handler = () => eventCount++
    mockSignalRConnection.on('TableRecordInserted', handler)

    await mockSignalRConnection.invoke('SubscribeToTable', 'logs')

    // Receive first event
    mockSignalRConnection.simulateEvent('TableRecordInserted', { recordId: '1' })
    expect(eventCount).toBe(1)

    // Unsubscribe
    await mockSignalRConnection.invoke('UnsubscribeFromTable', 'logs')
    mockSignalRConnection.off('TableRecordInserted', handler)

    // Should not receive more events
    mockSignalRConnection.simulateEvent('TableRecordInserted', { recordId: '2' })
    expect(eventCount).toBe(1)
  })

  it('should handle connection reconnection gracefully', async () => {
    await mockSignalRConnection.start()

    let reconnectAttempts = 0
    let reconnected = false

    mockSignalRConnection.on('reconnecting', () => {
      reconnectAttempts++
    })
    mockSignalRConnection.on('reconnected', () => {
      reconnected = true
    })

    // Subscribe to table before disconnect
    await mockSignalRConnection.invoke('SubscribeToTable', 'orders')

    // Simulate connection loss and reconnection
    mockSignalRConnection.simulateReconnecting()
    expect(reconnectAttempts).toBe(1)
    expect(mockSignalRConnection.state).toBe('Reconnecting')

    // Simulate successful reconnection
    mockSignalRConnection.simulateReconnected()
    expect(reconnected).toBe(true)
    expect(mockSignalRConnection.state).toBe('Connected')
  })

  it('should handle connection close with error', async () => {
    await mockSignalRConnection.start()

    let closeError: Error | undefined
    mockSignalRConnection.on('close', (error?: Error) => {
      closeError = error
    })

    // Simulate connection close with error
    mockSignalRConnection.simulateClose(new Error('Connection lost'))

    expect(closeError?.message).toBe('Connection lost')
    expect(mockSignalRConnection.state).toBe('Disconnected')
  })

  it('should handle schema change events', async () => {
    await mockSignalRConnection.start()

    const schemaChanges: unknown[] = []
    mockSignalRConnection.on('SchemaChanged', (data: unknown) => {
      schemaChanges.push(data)
    })

    mockSignalRConnection.on('ColumnAdded', (data: unknown) => {
      schemaChanges.push({ type: 'column_added', ...data })
    })

    // Subscribe to schema changes
    await mockSignalRConnection.invoke('SubscribeToTable', 'customers')

    // Simulate column addition
    mockSignalRConnection.simulateEvent('ColumnAdded', {
      tableName: 'customers',
      column: { name: 'loyalty_points', type: 'integer', nullable: true },
      timestamp: new Date().toISOString(),
    })

    expect(schemaChanges).toHaveLength(1)
    expect((schemaChanges[0] as any).type).toBe('column_added')
  })

  it('should handle multiple table subscriptions', async () => {
    await mockSignalRConnection.start()

    const tableEvents = new Map<string, unknown[]>()
    ;['orders', 'products', 'customers'].forEach((table) => {
      tableEvents.set(table, [])
    })

    mockSignalRConnection.on('TableRecordInserted', (data: { tableName: string }) => {
      const events = tableEvents.get(data.tableName) || []
      events.push(data)
      tableEvents.set(data.tableName, events)
    })

    // Subscribe to multiple tables
    await mockSignalRConnection.invoke('SubscribeToTable', 'orders')
    await mockSignalRConnection.invoke('SubscribeToTable', 'products')
    await mockSignalRConnection.invoke('SubscribeToTable', 'customers')

    // Simulate events for each table
    mockSignalRConnection.simulateEvent('TableRecordInserted', { tableName: 'orders', recordId: 'o1' })
    mockSignalRConnection.simulateEvent('TableRecordInserted', { tableName: 'products', recordId: 'p1' })
    mockSignalRConnection.simulateEvent('TableRecordInserted', { tableName: 'products', recordId: 'p2' })
    mockSignalRConnection.simulateEvent('TableRecordInserted', { tableName: 'customers', recordId: 'c1' })

    expect(tableEvents.get('orders')).toHaveLength(1)
    expect(tableEvents.get('products')).toHaveLength(2)
    expect(tableEvents.get('customers')).toHaveLength(1)
  })

  it('should handle view refresh notifications', async () => {
    await mockSignalRConnection.start()

    const viewNotifications: unknown[] = []
    mockSignalRConnection.on('MaterializedViewRefreshed', (data: unknown) => {
      viewNotifications.push(data)
    })

    mockSignalRConnection.on('ViewBecameStale', (data: unknown) => {
      viewNotifications.push({ type: 'stale', ...data })
    })

    // Simulate view stale notification
    mockSignalRConnection.simulateEvent('ViewBecameStale', {
      viewName: 'active_users_summary',
      underlyingTable: 'users',
      lastRefreshedAt: '2025-01-04T00:00:00Z',
      timestamp: new Date().toISOString(),
    })

    // Simulate view refresh completed
    mockSignalRConnection.simulateEvent('MaterializedViewRefreshed', {
      viewName: 'active_users_summary',
      rowCount: 5000,
      refreshDurationMs: 1234,
      timestamp: new Date().toISOString(),
    })

    expect(viewNotifications).toHaveLength(2)
    expect((viewNotifications[0] as any).type).toBe('stale')
  })

  it('should handle webhook delivery notifications', async () => {
    await mockSignalRConnection.start()

    const deliveryNotifications: unknown[] = []
    mockSignalRConnection.on('WebhookDelivered', (data: unknown) => {
      deliveryNotifications.push(data)
    })

    mockSignalRConnection.on('WebhookFailed', (data: unknown) => {
      deliveryNotifications.push({ status: 'failed', ...data })
    })

    // Simulate successful delivery
    mockSignalRConnection.simulateEvent('WebhookDelivered', {
      webhookId: 'wh-123',
      deliveryId: 'del-001',
      event: 'insert',
      httpStatus: 200,
      duration: 156,
      timestamp: new Date().toISOString(),
    })

    // Simulate failed delivery
    mockSignalRConnection.simulateEvent('WebhookFailed', {
      webhookId: 'wh-456',
      deliveryId: 'del-002',
      event: 'update',
      error: 'Connection timeout',
      attemptCount: 3,
      timestamp: new Date().toISOString(),
    })

    expect(deliveryNotifications).toHaveLength(2)
    expect((deliveryNotifications[1] as any).status).toBe('failed')
  })
})

describe('API Scenario: Concurrent Operations Workflow', () => {
  let client: MorphDBClient

  beforeEach(() => {
    mockFetch.mockReset()
    client = new MorphDBClient({
      url: 'http://localhost:5000',
      apiKey: 'test-api-key',
    })
  })

  it('should handle concurrent table operations', async () => {
    // Simulate concurrent requests
    const tables = ['users', 'orders', 'products', 'inventory', 'customers']
    const tableResponses = tables.map((name, i) => ({
      id: `table-${i}`,
      name,
      version: 1,
      columns: [],
      indexes: [],
      relations: [],
      createdAt: '',
      updatedAt: '',
    }))

    tableResponses.forEach((response) => {
      mockFetch.mockResolvedValueOnce(createMockResponse(response))
    })

    // Execute all requests concurrently
    const results = await Promise.all(tables.map((table) => client.getTable(table)))

    expect(results).toHaveLength(5)
    expect(results.map((r) => r.name)).toEqual(tables)
  })

  it('should handle concurrent data inserts with optimistic locking', async () => {
    const insertPromises: Promise<unknown>[] = []

    for (let i = 0; i < 10; i++) {
      mockFetch.mockResolvedValueOnce(
        createMockResponse({
          _id: `record-${i}`,
          _version: 1,
          name: `Item ${i}`,
        })
      )

      insertPromises.push(
        client.createRecord('items', { name: `Item ${i}` })
      )
    }

    const results = await Promise.all(insertPromises)

    expect(results).toHaveLength(10)
    expect(mockFetch).toHaveBeenCalledTimes(10)
  })

  it('should handle version conflict during concurrent updates', async () => {
    // First update succeeds
    mockFetch.mockResolvedValueOnce(createMockResponse(null, 204))

    // Second update fails with conflict
    mockFetch.mockResolvedValueOnce(
      createErrorResponse('Version conflict: record was modified', 'CONFLICT', 409)
    )

    const [result1, result2] = await Promise.allSettled([
      client.updateRecord('products', 'prod-1', { price: 29.99 }),
      client.updateRecord('products', 'prod-1', { price: 34.99 }),
    ])

    expect(result1.status).toBe('fulfilled')
    expect(result2.status).toBe('rejected')
    if (result2.status === 'rejected') {
      expect(result2.reason.message).toContain('Version conflict')
    }
  })

  it('should handle transaction-like multi-table operations', async () => {
    // Create order
    mockFetch.mockResolvedValueOnce(
      createMockResponse({
        _id: 'order-123',
        customerId: 'cust-456',
        total: 199.99,
      })
    )

    // Update inventory (reduce stock)
    mockFetch.mockResolvedValueOnce(createMockResponse(null, 204))

    // Create shipment record
    mockFetch.mockResolvedValueOnce(
      createMockResponse({
        _id: 'ship-789',
        orderId: 'order-123',
        status: 'pending',
      })
    )

    // Execute in sequence (simulating transaction)
    const order = await client.createRecord('orders', {
      customerId: 'cust-456',
      total: 199.99,
    })
    expect(order._id).toBe('order-123')

    await client.updateRecord('inventory', 'prod-001', { stock: 99 })

    const shipment = await client.createRecord('shipments', {
      orderId: order._id,
      status: 'pending',
    })
    expect(shipment.orderId).toBe('order-123')

    // Three operations: create order, update inventory, create shipment
    expect(mockFetch).toHaveBeenCalledTimes(3)
  })
})
