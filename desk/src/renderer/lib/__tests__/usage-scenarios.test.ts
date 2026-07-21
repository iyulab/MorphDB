/**
 * Usage Scenario Tests - Real-World Workflow Simulation
 *
 * These tests simulate actual usage scenarios like:
 * - E-commerce: Products, Categories, Orders, Customers
 * - Project Management: Projects, Tasks, Users
 * - CRM: Contacts, Companies, Deals
 *
 * Focus areas:
 * - Table creation with relationships
 * - Lookup/Rollup derived columns
 * - Views with joins and aggregations
 * - Schema evolution (column add/remove/modify)
 * - Complex queries across related tables
 */

import { describe, it, expect, vi, beforeEach } from 'vitest'
import { MorphDBClient } from '../api'

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

// =============================================================================
// SCENARIO 1: E-COMMERCE DATA MODEL
// =============================================================================

describe('Usage Scenario: E-Commerce Data Model', () => {
  let client: MorphDBClient

  beforeEach(() => {
    mockFetch.mockReset()
    client = new MorphDBClient({
      url: 'http://localhost:5000',
      apiKey: 'test-api-key',
    })
  })

  describe('Table Creation with Relationships', () => {
    it('should create categories table as parent', async () => {
      const categoryTable = {
        id: 'tbl-categories',
        name: 'categories',
        version: 1,
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
        columns: [
          { id: 'col-1', name: 'name', type: 'text', nullable: false, unique: true, indexed: true, primaryKey: false, position: 1, isDerived: false },
          { id: 'col-2', name: 'description', type: 'text', nullable: true, unique: false, indexed: false, primaryKey: false, position: 2, isDerived: false },
          { id: 'col-3', name: 'parent_id', type: 'uuid', nullable: true, unique: false, indexed: true, primaryKey: false, position: 3, isDerived: false },
        ],
        indexes: [],
        relations: [],
      }

      mockFetch.mockResolvedValueOnce(createMockResponse(categoryTable))

      const table = await client.createTable({
        name: 'categories',
        columns: [
          { name: 'name', type: 'text', nullable: false, unique: true, indexed: true },
          { name: 'description', type: 'text', nullable: true },
          { name: 'parent_id', type: 'uuid', nullable: true, indexed: true },
        ],
      })

      expect(table.name).toBe('categories')
      expect(table.columns).toHaveLength(3)
      expect(table.columns.find(c => c.name === 'name')?.isUnique).toBe(true)
    })

    it('should create products table with category reference', async () => {
      const productTable = {
        id: 'tbl-products',
        name: 'products',
        version: 1,
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
        columns: [
          { id: 'col-1', name: 'name', type: 'text', nullable: false, unique: false, indexed: true, primaryKey: false, position: 1, isDerived: false },
          { id: 'col-2', name: 'sku', type: 'text', nullable: false, unique: true, indexed: true, primaryKey: false, position: 2, isDerived: false },
          { id: 'col-3', name: 'price', type: 'decimal', nullable: false, unique: false, indexed: false, primaryKey: false, position: 3, isDerived: false },
          { id: 'col-4', name: 'stock', type: 'integer', nullable: false, unique: false, indexed: false, primaryKey: false, position: 4, isDerived: false, default: '0' },
          { id: 'col-5', name: 'category_id', type: 'uuid', nullable: true, unique: false, indexed: true, primaryKey: false, position: 5, isDerived: false },
        ],
        indexes: [],
        relations: [],
      }

      mockFetch.mockResolvedValueOnce(createMockResponse(productTable))

      const table = await client.createTable({
        name: 'products',
        columns: [
          { name: 'name', type: 'text', nullable: false, indexed: true },
          { name: 'sku', type: 'text', nullable: false, unique: true, indexed: true },
          { name: 'price', type: 'decimal', nullable: false },
          { name: 'stock', type: 'integer', nullable: false, default: '0' },
          { name: 'category_id', type: 'uuid', nullable: true, indexed: true },
        ],
      })

      expect(table.name).toBe('products')
      expect(table.columns.find(c => c.name === 'sku')?.isUnique).toBe(true)
    })

    it('should create relation between products and categories', async () => {
      const relation = {
        id: 'rel-1',
        name: 'product_category',
        sourceTableId: 'tbl-products',
        sourceColumnId: 'col-category_id',
        targetTableId: 'tbl-categories',
        targetColumnId: 'col-id',
        type: 'many-to-one',
        onDelete: 'set-null',
      }

      mockFetch.mockResolvedValueOnce(createMockResponse(relation))

      const result = await client.createRelation({
        name: 'product_category',
        sourceTable: 'products',
        sourceColumn: 'category_id',
        targetTable: 'categories',
        targetColumn: '_id',
        type: 'many-to-one',
        onDelete: 'set-null',
      })

      expect(result.name).toBe('product_category')
      expect(result.type).toBe('many-to-one')
    })

    it('should create customers and orders tables', async () => {
      const customersTable = {
        id: 'tbl-customers',
        name: 'customers',
        version: 1,
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
        columns: [
          { id: 'col-1', name: 'email', type: 'text', nullable: false, unique: true, indexed: true, primaryKey: false, position: 1, isDerived: false },
          { id: 'col-2', name: 'name', type: 'text', nullable: false, unique: false, indexed: true, primaryKey: false, position: 2, isDerived: false },
          { id: 'col-3', name: 'phone', type: 'text', nullable: true, unique: false, indexed: false, primaryKey: false, position: 3, isDerived: false },
        ],
        indexes: [],
        relations: [],
      }

      const ordersTable = {
        id: 'tbl-orders',
        name: 'orders',
        version: 1,
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
        columns: [
          { id: 'col-1', name: 'order_number', type: 'text', nullable: false, unique: true, indexed: true, primaryKey: false, position: 1, isDerived: false },
          { id: 'col-2', name: 'customer_id', type: 'uuid', nullable: false, unique: false, indexed: true, primaryKey: false, position: 2, isDerived: false },
          { id: 'col-3', name: 'status', type: 'text', nullable: false, unique: false, indexed: true, primaryKey: false, position: 3, isDerived: false, default: "'pending'" },
          { id: 'col-4', name: 'total', type: 'decimal', nullable: false, unique: false, indexed: false, primaryKey: false, position: 4, isDerived: false },
        ],
        indexes: [],
        relations: [],
      }

      mockFetch
        .mockResolvedValueOnce(createMockResponse(customersTable))
        .mockResolvedValueOnce(createMockResponse(ordersTable))

      const customers = await client.createTable({
        name: 'customers',
        columns: [
          { name: 'email', type: 'text', nullable: false, unique: true, indexed: true },
          { name: 'name', type: 'text', nullable: false, indexed: true },
          { name: 'phone', type: 'text', nullable: true },
        ],
      })

      const orders = await client.createTable({
        name: 'orders',
        columns: [
          { name: 'order_number', type: 'text', nullable: false, unique: true, indexed: true },
          { name: 'customer_id', type: 'uuid', nullable: false, indexed: true },
          { name: 'status', type: 'text', nullable: false, indexed: true, default: "'pending'" },
          { name: 'total', type: 'decimal', nullable: false },
        ],
      })

      expect(customers.name).toBe('customers')
      expect(orders.name).toBe('orders')
    })

    it('should create order_items junction table', async () => {
      const orderItemsTable = {
        id: 'tbl-order_items',
        name: 'order_items',
        version: 1,
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
        columns: [
          { id: 'col-1', name: 'order_id', type: 'uuid', nullable: false, unique: false, indexed: true, primaryKey: false, position: 1, isDerived: false },
          { id: 'col-2', name: 'product_id', type: 'uuid', nullable: false, unique: false, indexed: true, primaryKey: false, position: 2, isDerived: false },
          { id: 'col-3', name: 'quantity', type: 'integer', nullable: false, unique: false, indexed: false, primaryKey: false, position: 3, isDerived: false },
          { id: 'col-4', name: 'unit_price', type: 'decimal', nullable: false, unique: false, indexed: false, primaryKey: false, position: 4, isDerived: false },
          { id: 'col-5', name: 'subtotal', type: 'decimal', nullable: false, unique: false, indexed: false, primaryKey: false, position: 5, isDerived: true },
        ],
        indexes: [
          { id: 'idx-1', name: 'idx_order_items_order', columns: ['order_id'], type: 'btree', unique: false },
          { id: 'idx-2', name: 'idx_order_items_product', columns: ['product_id'], type: 'btree', unique: false },
        ],
        relations: [],
      }

      mockFetch.mockResolvedValueOnce(createMockResponse(orderItemsTable))

      const table = await client.createTable({
        name: 'order_items',
        columns: [
          { name: 'order_id', type: 'uuid', nullable: false, indexed: true },
          { name: 'product_id', type: 'uuid', nullable: false, indexed: true },
          { name: 'quantity', type: 'integer', nullable: false },
          { name: 'unit_price', type: 'decimal', nullable: false },
          { name: 'subtotal', type: 'decimal', nullable: false,
            formula: { formula: 'quantity * unit_price', returnType: 'decimal' }
          },
        ],
      })

      expect(table.name).toBe('order_items')
      const subtotalCol = table.columns.find(c => c.name === 'subtotal')
      expect(subtotalCol?.isDerived).toBe(true)
    })
  })

  describe('Lookup Columns', () => {
    it('should add lookup column to get category name in products', async () => {
      const lookupColumn = {
        id: 'col-category_name',
        name: 'category_name',
        type: 'text',
        nullable: true,
        unique: false,
        indexed: false,
        primaryKey: false,
        position: 6,
        isDerived: true,
      }

      mockFetch.mockResolvedValueOnce(createMockResponse(lookupColumn))

      const column = await client.addColumn('products', {
        name: 'category_name',
        type: 'text',
        nullable: true,
        lookup: {
          targetTable: 'categories',
          targetColumn: 'name',
          relationColumn: 'category_id',
          allowMultiple: false,
          onDelete: 'set-null',
        },
      })

      expect(column.name).toBe('category_name')
      expect(column.isDerived).toBe(true)
    })

    it('should add lookup column to get customer email in orders', async () => {
      const lookupColumn = {
        id: 'col-customer_email',
        name: 'customer_email',
        type: 'text',
        nullable: false,
        unique: false,
        indexed: false,
        primaryKey: false,
        position: 6,
        isDerived: true,
      }

      mockFetch.mockResolvedValueOnce(createMockResponse(lookupColumn))

      const column = await client.addColumn('orders', {
        name: 'customer_email',
        type: 'text',
        nullable: false,
        lookup: {
          targetTable: 'customers',
          targetColumn: 'email',
          relationColumn: 'customer_id',
        },
      })

      expect(column.isDerived).toBe(true)
    })

    it('should add lookup column to get product name in order_items', async () => {
      const lookupColumn = {
        id: 'col-product_name',
        name: 'product_name',
        type: 'text',
        nullable: false,
        unique: false,
        indexed: false,
        primaryKey: false,
        position: 6,
        isDerived: true,
      }

      mockFetch.mockResolvedValueOnce(createMockResponse(lookupColumn))

      const column = await client.addColumn('order_items', {
        name: 'product_name',
        type: 'text',
        nullable: false,
        lookup: {
          targetTable: 'products',
          targetColumn: 'name',
          relationColumn: 'product_id',
          onDelete: 'preserve',
        },
      })

      expect(column.isDerived).toBe(true)
    })
  })

  describe('Rollup Columns', () => {
    it('should add rollup column to count products per category', async () => {
      const rollupColumn = {
        id: 'col-product_count',
        name: 'product_count',
        type: 'integer',
        nullable: false,
        unique: false,
        indexed: false,
        primaryKey: false,
        position: 4,
        isDerived: true,
      }

      mockFetch.mockResolvedValueOnce(createMockResponse(rollupColumn))

      const column = await client.addColumn('categories', {
        name: 'product_count',
        type: 'integer',
        nullable: false,
        rollup: {
          targetTable: 'products',
          sourceColumn: '_id',
          foreignKeyColumn: 'category_id',
          relation: 'products_by_category',
          aggregation: 'count',
        },
      })

      expect(column.name).toBe('product_count')
      expect(column.isDerived).toBe(true)
    })

    it('should add rollup column to sum order totals per customer', async () => {
      const rollupColumn = {
        id: 'col-total_spent',
        name: 'total_spent',
        type: 'decimal',
        nullable: false,
        unique: false,
        indexed: false,
        primaryKey: false,
        position: 4,
        isDerived: true,
      }

      mockFetch.mockResolvedValueOnce(createMockResponse(rollupColumn))

      const column = await client.addColumn('customers', {
        name: 'total_spent',
        type: 'decimal',
        nullable: false,
        rollup: {
          targetTable: 'orders',
          sourceColumn: 'total',
          foreignKeyColumn: 'customer_id',
          relation: 'orders_by_customer',
          aggregation: 'sum',
          filter: { field: 'status', operator: 'eq', value: 'completed' },
        },
      })

      expect(column.isDerived).toBe(true)
    })

    it('should add rollup column to count orders per customer', async () => {
      const rollupColumn = {
        id: 'col-order_count',
        name: 'order_count',
        type: 'integer',
        nullable: false,
        unique: false,
        indexed: false,
        primaryKey: false,
        position: 5,
        isDerived: true,
      }

      mockFetch.mockResolvedValueOnce(createMockResponse(rollupColumn))

      const column = await client.addColumn('customers', {
        name: 'order_count',
        type: 'integer',
        nullable: false,
        rollup: {
          targetTable: 'orders',
          sourceColumn: '_id',
          foreignKeyColumn: 'customer_id',
          relation: 'orders_by_customer',
          aggregation: 'count',
        },
      })

      expect(column.isDerived).toBe(true)
    })

    it('should add rollup column to get average order value', async () => {
      const rollupColumn = {
        id: 'col-avg_order_value',
        name: 'avg_order_value',
        type: 'decimal',
        nullable: true,
        unique: false,
        indexed: false,
        primaryKey: false,
        position: 6,
        isDerived: true,
      }

      mockFetch.mockResolvedValueOnce(createMockResponse(rollupColumn))

      const column = await client.addColumn('customers', {
        name: 'avg_order_value',
        type: 'decimal',
        nullable: true,
        rollup: {
          targetTable: 'orders',
          sourceColumn: 'total',
          foreignKeyColumn: 'customer_id',
          relation: 'orders_by_customer',
          aggregation: 'avg',
        },
      })

      expect(column.isDerived).toBe(true)
    })

    it('should add rollup column to get last order date', async () => {
      const rollupColumn = {
        id: 'col-last_order_date',
        name: 'last_order_date',
        type: 'timestamp',
        nullable: true,
        unique: false,
        indexed: false,
        primaryKey: false,
        position: 7,
        isDerived: true,
      }

      mockFetch.mockResolvedValueOnce(createMockResponse(rollupColumn))

      const column = await client.addColumn('customers', {
        name: 'last_order_date',
        type: 'timestamp',
        nullable: true,
        rollup: {
          targetTable: 'orders',
          sourceColumn: 'ordered_at',
          foreignKeyColumn: 'customer_id',
          relation: 'orders_by_customer',
          aggregation: 'max',
        },
      })

      expect(column.isDerived).toBe(true)
    })
  })

  describe('Views with Joins', () => {
    it('should create order details view with customer and items', async () => {
      const view = {
        id: 'view-order_details',
        name: 'order_details',
        baseTable: 'orders',
        isMaterialized: false,
        createdAt: new Date().toISOString(),
      }

      mockFetch.mockResolvedValueOnce(createMockResponse(view))

      const result = await client.createView({
        name: 'order_details',
        baseTable: 'orders',
        joins: [
          { table: 'customers', alias: 'c', joinType: 'LEFT', condition: 'orders.customer_id = c._id' },
          { table: 'order_items', alias: 'oi', joinType: 'LEFT', condition: 'orders._id = oi.order_id' },
        ],
        columns: [
          { source: 'orders.order_number', alias: 'order_number' },
          { source: 'orders.status', alias: 'status' },
          { source: 'c.name', alias: 'customer_name' },
          { source: 'oi.quantity', alias: 'quantity' },
          { source: 'oi.unit_price', alias: 'unit_price' },
        ],
        orderBy: [{ column: 'order_number', descending: true }],
      })

      expect(result.name).toBe('order_details')
    })

    it('should create product catalog view with category info', async () => {
      const view = {
        id: 'view-product_catalog',
        name: 'product_catalog',
        baseTable: 'products',
        isMaterialized: false,
        createdAt: new Date().toISOString(),
      }

      mockFetch.mockResolvedValueOnce(createMockResponse(view))

      const result = await client.createView({
        name: 'product_catalog',
        baseTable: 'products',
        joins: [
          { table: 'categories', alias: 'cat', joinType: 'LEFT', condition: 'products.category_id = cat._id' },
        ],
        columns: [
          { source: 'products.name', alias: 'product_name' },
          { source: 'products.sku', alias: 'sku' },
          { source: 'products.price', alias: 'price' },
          { source: 'products.stock', alias: 'stock' },
          { source: 'cat.name', alias: 'category_name' },
        ],
        filters: [{ field: 'stock', operator: 'gt', value: 0 }],
      })

      expect(result.name).toBe('product_catalog')
    })

    it('should create materialized sales summary view', async () => {
      const view = {
        id: 'view-sales_summary',
        name: 'sales_summary',
        baseTable: 'orders',
        isMaterialized: true,
        refreshPolicy: 'on_demand',
        createdAt: new Date().toISOString(),
      }

      mockFetch.mockResolvedValueOnce(createMockResponse(view))

      const result = await client.createView({
        name: 'sales_summary',
        baseTable: 'orders',
        materialized: true,
        refreshPolicy: 'on_demand',
        joins: [
          { table: 'order_items', alias: 'oi', joinType: 'INNER', condition: 'orders._id = oi.order_id' },
          { table: 'products', alias: 'p', joinType: 'INNER', condition: 'oi.product_id = p._id' },
        ],
        columns: [
          { source: 'p.category_id', alias: 'category_id' },
          { expression: 'SUM(oi.quantity * oi.unit_price)', alias: 'total_sales' },
          { expression: 'COUNT(DISTINCT orders._id)', alias: 'order_count' },
        ],
        groupBy: ['p.category_id'],
      })

      expect(result.name).toBe('sales_summary')
      expect(result.isMaterialized).toBe(true)
    })

    it('should create top customers view', async () => {
      const view = {
        id: 'view-top_customers',
        name: 'top_customers',
        baseTable: 'customers',
        isMaterialized: false,
        createdAt: new Date().toISOString(),
      }

      mockFetch.mockResolvedValueOnce(createMockResponse(view))

      const result = await client.createView({
        name: 'top_customers',
        baseTable: 'customers',
        joins: [
          { table: 'orders', alias: 'o', joinType: 'LEFT', condition: 'customers._id = o.customer_id' },
        ],
        columns: [
          { source: 'customers.name', alias: 'name' },
          { source: 'customers.email', alias: 'email' },
          { expression: 'COUNT(o._id)', alias: 'order_count' },
          { expression: 'COALESCE(SUM(o.total), 0)', alias: 'total_spent' },
        ],
        groupBy: ['customers._id', 'customers.name', 'customers.email'],
        orderBy: [{ column: 'total_spent', descending: true }],
        limit: 100,
      })

      expect(result.name).toBe('top_customers')
    })
  })
})

// =============================================================================
// SCENARIO 2: PROJECT MANAGEMENT SYSTEM
// =============================================================================

describe('Usage Scenario: Project Management', () => {
  let client: MorphDBClient

  beforeEach(() => {
    mockFetch.mockReset()
    client = new MorphDBClient({
      url: 'http://localhost:5000',
      apiKey: 'test-api-key',
    })
  })

  describe('Tables and Relationships', () => {
    it('should create users, projects, and tasks tables', async () => {
      const usersTable = {
        id: 'tbl-users',
        name: 'users',
        version: 1,
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
        columns: [
          { id: 'col-1', name: 'username', type: 'text', nullable: false, unique: true, indexed: true, primaryKey: false, position: 1, isDerived: false },
          { id: 'col-2', name: 'email', type: 'text', nullable: false, unique: true, indexed: true, primaryKey: false, position: 2, isDerived: false },
          { id: 'col-3', name: 'role', type: 'text', nullable: false, unique: false, indexed: true, primaryKey: false, position: 3, isDerived: false },
        ],
        indexes: [],
        relations: [],
      }

      const projectsTable = {
        id: 'tbl-projects',
        name: 'projects',
        version: 1,
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
        columns: [
          { id: 'col-1', name: 'name', type: 'text', nullable: false, unique: false, indexed: true, primaryKey: false, position: 1, isDerived: false },
          { id: 'col-2', name: 'description', type: 'text', nullable: true, unique: false, indexed: false, primaryKey: false, position: 2, isDerived: false },
          { id: 'col-3', name: 'owner_id', type: 'uuid', nullable: false, unique: false, indexed: true, primaryKey: false, position: 3, isDerived: false },
          { id: 'col-4', name: 'status', type: 'text', nullable: false, unique: false, indexed: true, primaryKey: false, position: 4, isDerived: false },
        ],
        indexes: [],
        relations: [],
      }

      const tasksTable = {
        id: 'tbl-tasks',
        name: 'tasks',
        version: 1,
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
        columns: [
          { id: 'col-1', name: 'title', type: 'text', nullable: false, unique: false, indexed: true, primaryKey: false, position: 1, isDerived: false },
          { id: 'col-2', name: 'project_id', type: 'uuid', nullable: false, unique: false, indexed: true, primaryKey: false, position: 2, isDerived: false },
          { id: 'col-3', name: 'assignee_id', type: 'uuid', nullable: true, unique: false, indexed: true, primaryKey: false, position: 3, isDerived: false },
          { id: 'col-4', name: 'status', type: 'text', nullable: false, unique: false, indexed: true, primaryKey: false, position: 4, isDerived: false },
          { id: 'col-5', name: 'estimated_hours', type: 'decimal', nullable: true, unique: false, indexed: false, primaryKey: false, position: 5, isDerived: false },
        ],
        indexes: [],
        relations: [],
      }

      mockFetch
        .mockResolvedValueOnce(createMockResponse(usersTable))
        .mockResolvedValueOnce(createMockResponse(projectsTable))
        .mockResolvedValueOnce(createMockResponse(tasksTable))

      const users = await client.createTable({
        name: 'users',
        columns: [
          { name: 'username', type: 'text', nullable: false, unique: true, indexed: true },
          { name: 'email', type: 'text', nullable: false, unique: true, indexed: true },
          { name: 'role', type: 'text', nullable: false, indexed: true },
        ],
      })

      const projects = await client.createTable({
        name: 'projects',
        columns: [
          { name: 'name', type: 'text', nullable: false, indexed: true },
          { name: 'description', type: 'text', nullable: true },
          { name: 'owner_id', type: 'uuid', nullable: false, indexed: true },
          { name: 'status', type: 'text', nullable: false, indexed: true },
        ],
      })

      const tasks = await client.createTable({
        name: 'tasks',
        columns: [
          { name: 'title', type: 'text', nullable: false, indexed: true },
          { name: 'project_id', type: 'uuid', nullable: false, indexed: true },
          { name: 'assignee_id', type: 'uuid', nullable: true, indexed: true },
          { name: 'status', type: 'text', nullable: false, indexed: true },
          { name: 'estimated_hours', type: 'decimal', nullable: true },
        ],
      })

      expect(users.name).toBe('users')
      expect(projects.name).toBe('projects')
      expect(tasks.name).toBe('tasks')
    })
  })

  describe('Derived Columns', () => {
    it('should add task count rollup to projects', async () => {
      const rollupColumn = {
        id: 'col-task_count',
        name: 'task_count',
        type: 'integer',
        nullable: false,
        unique: false,
        indexed: false,
        primaryKey: false,
        position: 5,
        isDerived: true,
      }

      mockFetch.mockResolvedValueOnce(createMockResponse(rollupColumn))

      const column = await client.addColumn('projects', {
        name: 'task_count',
        type: 'integer',
        nullable: false,
        rollup: {
          targetTable: 'tasks',
          sourceColumn: '_id',
          foreignKeyColumn: 'project_id',
          relation: 'tasks_by_project',
          aggregation: 'count',
        },
      })

      expect(column.isDerived).toBe(true)
    })

    it('should add completed task count rollup to projects', async () => {
      const rollupColumn = {
        id: 'col-completed_task_count',
        name: 'completed_task_count',
        type: 'integer',
        nullable: false,
        unique: false,
        indexed: false,
        primaryKey: false,
        position: 6,
        isDerived: true,
      }

      mockFetch.mockResolvedValueOnce(createMockResponse(rollupColumn))

      const column = await client.addColumn('projects', {
        name: 'completed_task_count',
        type: 'integer',
        nullable: false,
        rollup: {
          targetTable: 'tasks',
          sourceColumn: '_id',
          foreignKeyColumn: 'project_id',
          relation: 'tasks_by_project',
          aggregation: 'count',
          filter: { field: 'status', operator: 'eq', value: 'done' },
        },
      })

      expect(column.isDerived).toBe(true)
    })

    it('should add total estimated hours rollup to projects', async () => {
      const rollupColumn = {
        id: 'col-total_estimated_hours',
        name: 'total_estimated_hours',
        type: 'decimal',
        nullable: true,
        unique: false,
        indexed: false,
        primaryKey: false,
        position: 7,
        isDerived: true,
      }

      mockFetch.mockResolvedValueOnce(createMockResponse(rollupColumn))

      const column = await client.addColumn('projects', {
        name: 'total_estimated_hours',
        type: 'decimal',
        nullable: true,
        rollup: {
          targetTable: 'tasks',
          sourceColumn: 'estimated_hours',
          foreignKeyColumn: 'project_id',
          relation: 'tasks_by_project',
          aggregation: 'sum',
        },
      })

      expect(column.isDerived).toBe(true)
    })

    it('should add lookup columns for task details', async () => {
      const projectNameColumn = {
        id: 'col-project_name',
        name: 'project_name',
        type: 'text',
        nullable: false,
        unique: false,
        indexed: false,
        primaryKey: false,
        position: 6,
        isDerived: true,
      }

      const assigneeNameColumn = {
        id: 'col-assignee_name',
        name: 'assignee_name',
        type: 'text',
        nullable: true,
        unique: false,
        indexed: false,
        primaryKey: false,
        position: 7,
        isDerived: true,
      }

      mockFetch
        .mockResolvedValueOnce(createMockResponse(projectNameColumn))
        .mockResolvedValueOnce(createMockResponse(assigneeNameColumn))

      const projectName = await client.addColumn('tasks', {
        name: 'project_name',
        type: 'text',
        nullable: false,
        lookup: {
          targetTable: 'projects',
          targetColumn: 'name',
          relationColumn: 'project_id',
        },
      })

      const assigneeName = await client.addColumn('tasks', {
        name: 'assignee_name',
        type: 'text',
        nullable: true,
        lookup: {
          targetTable: 'users',
          targetColumn: 'username',
          relationColumn: 'assignee_id',
        },
      })

      expect(projectName.isDerived).toBe(true)
      expect(assigneeName.isDerived).toBe(true)
    })
  })

  describe('Dashboard Views', () => {
    it('should create project progress view', async () => {
      const view = {
        id: 'view-project_progress',
        name: 'project_progress',
        baseTable: 'projects',
        isMaterialized: false,
        createdAt: new Date().toISOString(),
      }

      mockFetch.mockResolvedValueOnce(createMockResponse(view))

      const result = await client.createView({
        name: 'project_progress',
        baseTable: 'projects',
        joins: [
          { table: 'tasks', alias: 't', joinType: 'LEFT', condition: 'projects._id = t.project_id' },
          { table: 'users', alias: 'u', joinType: 'LEFT', condition: 'projects.owner_id = u._id' },
        ],
        columns: [
          { source: 'projects.name', alias: 'project_name' },
          { source: 'u.username', alias: 'owner' },
          { expression: 'COUNT(t._id)', alias: 'total_tasks' },
          { expression: "COUNT(CASE WHEN t.status = 'done' THEN 1 END)", alias: 'completed_tasks' },
          { expression: "ROUND(COUNT(CASE WHEN t.status = 'done' THEN 1 END)::numeric / NULLIF(COUNT(t._id), 0) * 100, 2)", alias: 'progress_pct' },
        ],
        groupBy: ['projects._id', 'projects.name', 'u.username'],
      })

      expect(result.name).toBe('project_progress')
    })

    it('should create team workload view', async () => {
      const view = {
        id: 'view-team_workload',
        name: 'team_workload',
        baseTable: 'users',
        isMaterialized: false,
        createdAt: new Date().toISOString(),
      }

      mockFetch.mockResolvedValueOnce(createMockResponse(view))

      const result = await client.createView({
        name: 'team_workload',
        baseTable: 'users',
        joins: [
          { table: 'tasks', alias: 't', joinType: 'LEFT', condition: 'users._id = t.assignee_id' },
        ],
        columns: [
          { source: 'users.username', alias: 'username' },
          { source: 'users.role', alias: 'role' },
          { expression: "COUNT(CASE WHEN t.status != 'done' THEN 1 END)", alias: 'open_tasks' },
          { expression: "COALESCE(SUM(CASE WHEN t.status != 'done' THEN t.estimated_hours END), 0)", alias: 'pending_hours' },
        ],
        groupBy: ['users._id', 'users.username', 'users.role'],
        orderBy: [{ column: 'pending_hours', descending: true }],
      })

      expect(result.name).toBe('team_workload')
    })
  })
})

// =============================================================================
// SCENARIO 3: SCHEMA EVOLUTION
// =============================================================================

describe('Usage Scenario: Schema Evolution', () => {
  let client: MorphDBClient

  beforeEach(() => {
    mockFetch.mockReset()
    client = new MorphDBClient({
      url: 'http://localhost:5000',
      apiKey: 'test-api-key',
    })
  })

  describe('Adding Columns', () => {
    it('should add new column to existing table', async () => {
      const newColumn = {
        id: 'col-new',
        name: 'country_code',
        type: 'text',
        nullable: true,
        unique: false,
        indexed: true,
        primaryKey: false,
        position: 7,
        isDerived: false,
      }

      mockFetch.mockResolvedValueOnce(createMockResponse(newColumn))

      const column = await client.addColumn('customers', {
        name: 'country_code',
        type: 'text',
        nullable: true,
        indexed: true,
      })

      expect(column.isIndexed).toBe(true)
    })

    it('should add column with default value', async () => {
      const newColumn = {
        id: 'col-new',
        name: 'is_active',
        type: 'boolean',
        nullable: false,
        unique: false,
        indexed: false,
        primaryKey: false,
        position: 8,
        isDerived: false,
        default: 'true',
      }

      mockFetch.mockResolvedValueOnce(createMockResponse(newColumn))

      const column = await client.addColumn('customers', {
        name: 'is_active',
        type: 'boolean',
        nullable: false,
        default: 'true',
      })

      expect(column.defaultValue).toBe('true')
    })

    it('should add indexed column for performance', async () => {
      const newColumn = {
        id: 'col-new',
        name: 'last_login',
        type: 'timestamp',
        nullable: true,
        unique: false,
        indexed: true,
        primaryKey: false,
        position: 9,
        isDerived: false,
      }

      mockFetch.mockResolvedValueOnce(createMockResponse(newColumn))

      const column = await client.addColumn('users', {
        name: 'last_login',
        type: 'timestamp',
        nullable: true,
        indexed: true,
      })

      expect(column.isIndexed).toBe(true)
    })
  })

  describe('Modifying Columns', () => {
    it('should rename a column', async () => {
      const updatedColumn = {
        id: 'col-1',
        name: 'full_name',
        type: 'text',
        nullable: false,
        unique: false,
        indexed: true,
        primaryKey: false,
        position: 1,
        isDerived: false,
      }

      mockFetch.mockResolvedValueOnce(createMockResponse(updatedColumn))

      const column = await client.updateColumn('customers', 'name', {
        name: 'full_name',
        version: 1,
      })

      expect(column.name).toBe('full_name')
    })

    it('should change column default value', async () => {
      const updatedColumn = {
        id: 'col-1',
        name: 'status',
        type: 'text',
        nullable: false,
        unique: false,
        indexed: true,
        primaryKey: false,
        position: 1,
        isDerived: false,
        default: "'inactive'",
      }

      mockFetch.mockResolvedValueOnce(createMockResponse(updatedColumn))

      const column = await client.updateColumn('orders', 'status', {
        default: "'inactive'",
        version: 1,
      })

      expect(column.defaultValue).toBe("'inactive'")
    })
  })

  describe('Removing Columns', () => {
    it('should remove a column', async () => {
      mockFetch.mockResolvedValueOnce(createMockResponse(null, 204))

      await client.deleteColumn('customers', 'phone')

      expect(mockFetch).toHaveBeenCalledWith(
        'http://localhost:5000/api/schema/tables/customers/columns/phone',
        expect.objectContaining({ method: 'DELETE' })
      )
    })

    it('should handle removal of column with dependencies', async () => {
      mockFetch.mockResolvedValueOnce(
        createErrorResponse(
          'Cannot delete column: it is referenced by derived columns',
          'DEPENDENCY_ERROR',
          409
        )
      )

      await expect(
        client.deleteColumn('customers', 'customer_id')
      ).rejects.toThrow('Cannot delete column')
    })
  })

  describe('Index Management', () => {
    it('should create composite index', async () => {
      const index = {
        id: 'idx-1',
        name: 'idx_orders_customer_date',
        columns: ['customer_id', 'ordered_at'],
        type: 'btree',
        unique: false,
      }

      mockFetch.mockResolvedValueOnce(createMockResponse(index))

      const result = await client.createIndex('orders', {
        name: 'idx_orders_customer_date',
        columns: ['customer_id', 'ordered_at'],
        type: 'btree',
      })

      expect(result.columns).toHaveLength(2)
    })

    it('should create unique index', async () => {
      const index = {
        id: 'idx-2',
        name: 'idx_products_sku_unique',
        columns: ['sku'],
        type: 'btree',
        unique: true,
      }

      mockFetch.mockResolvedValueOnce(createMockResponse(index))

      const result = await client.createIndex('products', {
        name: 'idx_products_sku_unique',
        columns: ['sku'],
        unique: true,
      })

      expect(result.unique).toBe(true)
    })

    it('should create partial index with where clause', async () => {
      const index = {
        id: 'idx-3',
        name: 'idx_active_products',
        columns: ['name'],
        type: 'btree',
        unique: false,
      }

      mockFetch.mockResolvedValueOnce(createMockResponse(index))

      const result = await client.createIndex('products', {
        name: 'idx_active_products',
        columns: ['name'],
        where: 'stock > 0',
      })

      expect(result.name).toBe('idx_active_products')
    })

    it('should delete an index', async () => {
      mockFetch.mockResolvedValueOnce(createMockResponse(null, 204))

      await client.deleteIndex('idx_old_index')

      expect(mockFetch).toHaveBeenCalledWith(
        'http://localhost:5000/api/schema/indexes/idx_old_index',
        expect.objectContaining({ method: 'DELETE' })
      )
    })
  })

  describe('Table Renaming', () => {
    it('should rename a table', async () => {
      const updatedTable = {
        id: 'tbl-1',
        name: 'clients',
        version: 2,
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
        columns: [],
        indexes: [],
        relations: [],
      }

      mockFetch.mockResolvedValueOnce(createMockResponse(updatedTable))

      const result = await client.renameTable('customers', 'clients')

      expect(result.name).toBe('clients')
      expect(result.version).toBe(2)
    })
  })
})

// =============================================================================
// SCENARIO 4: COMPLEX QUERY PATTERNS
// =============================================================================

describe('Usage Scenario: Complex Query Patterns', () => {
  let client: MorphDBClient

  beforeEach(() => {
    mockFetch.mockReset()
    client = new MorphDBClient({
      url: 'http://localhost:5000',
      apiKey: 'test-api-key',
    })
  })

  describe('Aggregation Queries', () => {
    it('should query sales by category', async () => {
      const response = {
        data: [
          { category: 'Electronics', total_sales: 125000, order_count: 450 },
          { category: 'Clothing', total_sales: 85000, order_count: 620 },
          { category: 'Books', total_sales: 45000, order_count: 1200 },
        ],
        totalGroups: 3,
        metadata: { rowsScanned: 5000, executionTimeMs: 125 },
      }

      mockFetch.mockResolvedValueOnce(createMockResponse(response))

      const result = await client.aggregate('order_items', {
        aggregations: [
          { function: 'sum', column: 'subtotal', alias: 'total_sales' },
          { function: 'count', alias: 'order_count' },
        ],
        groupBy: ['product_category'],
        orderBy: [{ column: 'total_sales', direction: 'desc' }],
      })

      expect(result.data).toHaveLength(3)
      expect(result.totalGroups).toBe(3)
    })

    it('should query with having clause', async () => {
      const response = {
        data: [
          { customer_id: 'cust-1', order_count: 15, total_spent: 2500 },
          { customer_id: 'cust-2', order_count: 12, total_spent: 1800 },
        ],
        totalGroups: 2,
        metadata: { rowsScanned: 1000, executionTimeMs: 50 },
      }

      mockFetch.mockResolvedValueOnce(createMockResponse(response))

      const result = await client.aggregate('orders', {
        aggregations: [
          { function: 'count', alias: 'order_count' },
          { function: 'sum', column: 'total', alias: 'total_spent' },
        ],
        groupBy: ['customer_id'],
        having: [{ alias: 'orders', operator: 'gte', value: 10 }],
      })

      expect(result.data).toHaveLength(2)
    })

    it('should query time-series aggregation', async () => {
      const response = {
        data: [
          { month: '2024-01', revenue: 50000, orders: 200 },
          { month: '2024-02', revenue: 55000, orders: 220 },
          { month: '2024-03', revenue: 62000, orders: 250 },
        ],
        totalGroups: 3,
        metadata: { rowsScanned: 10000, executionTimeMs: 200 },
      }

      mockFetch.mockResolvedValueOnce(createMockResponse(response))

      const result = await client.aggregate('orders', {
        aggregations: [
          { function: 'sum', column: 'total', alias: 'revenue' },
          { function: 'count', alias: 'orders' },
        ],
        groupBy: ["DATE_TRUNC('month', ordered_at)"],
        orderBy: [{ column: 'month', direction: 'asc' }],
      })

      expect(result.data).toHaveLength(3)
    })
  })

  describe('View Queries', () => {
    it('should query view with filters', async () => {
      const response = {
        value: [
          { product_name: 'Laptop', category_name: 'Electronics', price: 999.99, stock: 50 },
          { product_name: 'Phone', category_name: 'Electronics', price: 699.99, stock: 120 },
        ],
        '@odata.count': 2,
      }

      mockFetch.mockResolvedValueOnce(createMockResponse(response))

      const result = await client.queryData('product_catalog', {
        $filter: "category_name eq 'Electronics'",
        $orderby: 'price desc',
        $top: 20,
      })

      expect(result.value).toHaveLength(2)
    })

    it('should query materialized view for dashboard', async () => {
      const response = {
        value: [
          { category_id: 'cat-1', total_sales: 125000, order_count: 450 },
          { category_id: 'cat-2', total_sales: 85000, order_count: 620 },
        ],
        '@odata.count': 2,
      }

      mockFetch.mockResolvedValueOnce(createMockResponse(response))

      const result = await client.queryData('sales_summary', {
        $orderby: 'total_sales desc',
      })

      expect(result.value).toHaveLength(2)
    })
  })

  describe('Cross-Table Operations', () => {
    it('should perform bulk insert into a table', async () => {
      const batchResponse = {
        successCount: 3,
        failureCount: 0,
        results: [
          { index: 0, success: true, affectedRows: 1 },
          { index: 1, success: true, affectedRows: 1 },
          { index: 2, success: true, affectedRows: 1 },
        ],
      }

      mockFetch.mockResolvedValueOnce(createMockResponse(batchResponse))

      const result = await client.bulkInsert('order_items', [
        { order_id: 'order-1', product_id: 'prod-1', quantity: 2, unit_price: 50.00 },
        { order_id: 'order-1', product_id: 'prod-2', quantity: 1, unit_price: 50.00 },
        { order_id: 'order-1', product_id: 'prod-3', quantity: 3, unit_price: 25.00 },
      ])

      expect(result.successCount).toBe(3)
    })

    it('should perform bulk update with filter', async () => {
      const batchResponse = {
        successCount: 1,
        failureCount: 0,
        results: [{ index: 0, success: true, affectedRows: 5 }],
      }

      mockFetch.mockResolvedValueOnce(createMockResponse(batchResponse))

      const result = await client.bulkUpdate(
        'products',
        { stock: 0 },
        "category_id eq 'cat-discontinued'"
      )

      expect(result.results[0].affectedRows).toBe(5)
    })
  })
})

// =============================================================================
// SCENARIO 5: FORMULA COLUMNS
// =============================================================================

describe('Usage Scenario: Formula Columns', () => {
  let client: MorphDBClient

  beforeEach(() => {
    mockFetch.mockReset()
    client = new MorphDBClient({
      url: 'http://localhost:5000',
      apiKey: 'test-api-key',
    })
  })

  it('should add formula column for calculated subtotal', async () => {
    const formulaColumn = {
      id: 'col-subtotal',
      name: 'subtotal',
      type: 'decimal',
      nullable: false,
      unique: false,
      indexed: false,
      primaryKey: false,
      position: 5,
      isDerived: true,
    }

    mockFetch.mockResolvedValueOnce(createMockResponse(formulaColumn))

    const column = await client.addColumn('order_items', {
      name: 'subtotal',
      type: 'decimal',
      nullable: false,
      formula: {
        formula: 'quantity * unit_price',
        returnType: 'decimal',
      },
    })

    expect(column.isDerived).toBe(true)
  })

  it('should add formula column for discount calculation', async () => {
    const formulaColumn = {
      id: 'col-discounted_price',
      name: 'discounted_price',
      type: 'decimal',
      nullable: false,
      unique: false,
      indexed: false,
      primaryKey: false,
      position: 6,
      isDerived: true,
    }

    mockFetch.mockResolvedValueOnce(createMockResponse(formulaColumn))

    const column = await client.addColumn('products', {
      name: 'discounted_price',
      type: 'decimal',
      nullable: false,
      formula: {
        formula: 'price * (1 - COALESCE(discount_pct, 0) / 100)',
        returnType: 'decimal',
      },
    })

    expect(column.isDerived).toBe(true)
  })

  it('should add formula column for full name concatenation', async () => {
    const formulaColumn = {
      id: 'col-full_name',
      name: 'full_name',
      type: 'text',
      nullable: false,
      unique: false,
      indexed: true,
      primaryKey: false,
      position: 4,
      isDerived: true,
    }

    mockFetch.mockResolvedValueOnce(createMockResponse(formulaColumn))

    const column = await client.addColumn('contacts', {
      name: 'full_name',
      type: 'text',
      nullable: false,
      indexed: true,
      formula: {
        formula: "CONCAT(first_name, ' ', last_name)",
        returnType: 'text',
      },
    })

    expect(column.isDerived).toBe(true)
  })

  it('should add formula column for age calculation', async () => {
    const formulaColumn = {
      id: 'col-age',
      name: 'age',
      type: 'integer',
      nullable: true,
      unique: false,
      indexed: false,
      primaryKey: false,
      position: 5,
      isDerived: true,
    }

    mockFetch.mockResolvedValueOnce(createMockResponse(formulaColumn))

    const column = await client.addColumn('contacts', {
      name: 'age',
      type: 'integer',
      nullable: true,
      formula: {
        formula: 'EXTRACT(YEAR FROM AGE(CURRENT_DATE, birth_date))',
        returnType: 'integer',
      },
    })

    expect(column.isDerived).toBe(true)
  })

  it('should add conditional formula column', async () => {
    const formulaColumn = {
      id: 'col-stock_status',
      name: 'stock_status',
      type: 'text',
      nullable: false,
      unique: false,
      indexed: true,
      primaryKey: false,
      position: 6,
      isDerived: true,
    }

    mockFetch.mockResolvedValueOnce(createMockResponse(formulaColumn))

    const column = await client.addColumn('products', {
      name: 'stock_status',
      type: 'text',
      nullable: false,
      indexed: true,
      formula: {
        formula: "CASE WHEN stock = 0 THEN 'out_of_stock' WHEN stock < 10 THEN 'low_stock' ELSE 'in_stock' END",
        returnType: 'text',
      },
    })

    expect(column.isDerived).toBe(true)
  })
})
