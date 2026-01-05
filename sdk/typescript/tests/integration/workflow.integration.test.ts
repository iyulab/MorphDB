/**
 * Integration tests for complete MorphDB TypeScript SDK workflows.
 *
 * These tests verify end-to-end scenarios combining multiple operations.
 * Requires a running MorphDB server.
 *
 * Start the test server with: docker compose -f docker-compose.test.yml up -d
 * Run with: npm run test:integration
 */

import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import { randomUUID } from 'crypto';
import { MorphDBClient } from '../../src/client.js';
import { FilterOperator } from '../../src/types.js';
import { createTestClient, uniqueTableName, cleanupTable } from './test-utils.js';

describe('Workflow Integration Tests', () => {
  let client: MorphDBClient;
  let tableName: string;

  beforeEach(() => {
    client = createTestClient();
    tableName = uniqueTableName();
  });

  afterEach(async () => {
    await cleanupTable(client, tableName);
  });

  describe('Complete Table Lifecycle', () => {
    it('should handle full lifecycle: create -> use -> modify -> drop', async () => {
      // 1. Create table
      const table = await client.schema.createTable({
        name: tableName,
        columns: [
          { name: 'product_name', type: 'text', nullable: false },
          { name: 'price', type: 'decimal', nullable: false },
          { name: 'in_stock', type: 'boolean', nullable: true },
        ],
        description: 'Product catalog table',
      });

      expect(table.name).toBe(tableName);
      const initialColumnCount = table.columns.length;

      // 2. Insert initial data
      const products = [
        { product_name: 'Widget A', price: 19.99, in_stock: true },
        { product_name: 'Widget B', price: 29.99, in_stock: true },
        { product_name: 'Widget C', price: 39.99, in_stock: false },
      ];

      const insertedIds: string[] = [];
      for (const product of products) {
        const record = await client.data.insert(tableName, product);
        insertedIds.push(record.id);
        expect(record.data.product_name).toBe(product.product_name);
      }

      // 3. Query data
      const queryResult = await client.data.query(tableName, {
        filters: [{ column: 'in_stock', operator: FilterOperator.EQ, value: true }],
        orderBy: [{ column: 'price', ascending: true }],
      });

      expect(queryResult.pagination.totalCount).toBe(2);
      expect(queryResult.data[0].data.product_name).toBe('Widget A');
      expect(queryResult.data[1].data.product_name).toBe('Widget B');

      // 4. Update data
      await client.data.update(tableName, insertedIds[2], {
        in_stock: true,
        price: 34.99,
      });

      const updated = await client.data.getById(tableName, insertedIds[2]);
      expect(updated.data.in_stock).toBe(true);
      expect(parseFloat(updated.data.price)).toBe(34.99);

      // 5. Add a new column
      const modifiedTable = await client.schema.addColumn(tableName, {
        name: 'category',
        type: 'text',
        nullable: true,
        defaultValue: "'general'",
      });

      expect(modifiedTable.columns.length).toBe(initialColumnCount + 1);
      const columnNames = modifiedTable.columns.map((col) => col.name);
      expect(columnNames).toContain('category');

      // 6. Insert with new column
      const newProduct = await client.data.insert(tableName, {
        product_name: 'Widget D',
        price: 49.99,
        in_stock: true,
        category: 'premium',
      });
      expect(newProduct.data.category).toBe('premium');

      // 7. Delete a record
      await client.data.delete(tableName, insertedIds[0]);

      // Verify count
      const allResult = await client.data.query(tableName, {});
      expect(allResult.pagination.totalCount).toBe(3);

      // 8. Drop table
      await client.schema.dropTable(tableName);

      // Verify table is gone
      const tables = await client.schema.getTables();
      const tableNames = tables.map((t) => t.name);
      expect(tableNames).not.toContain(tableName);
    });
  });

  describe('Multi-Table Workflow', () => {
    let categoriesTable: string;
    let productsTable: string;

    beforeEach(() => {
      categoriesTable = `categories_${randomUUID().slice(0, 8)}`;
      productsTable = `products_${randomUUID().slice(0, 8)}`;
    });

    afterEach(async () => {
      try {
        await client.schema.dropTable(productsTable);
      } catch {
        // Ignore
      }
      try {
        await client.schema.dropTable(categoriesTable);
      } catch {
        // Ignore
      }
    });

    it('should manage multiple related tables', async () => {
      // Create categories table
      await client.schema.createTable({
        name: categoriesTable,
        columns: [
          { name: 'name', type: 'text', nullable: false, unique: true },
          { name: 'description', type: 'text', nullable: true },
        ],
      });

      // Create products table
      await client.schema.createTable({
        name: productsTable,
        columns: [
          { name: 'name', type: 'text', nullable: false },
          { name: 'category_name', type: 'text', nullable: true },
          { name: 'price', type: 'decimal', nullable: false },
        ],
      });

      // Insert categories
      await client.data.insert(categoriesTable, {
        name: 'Electronics',
        description: 'Electronic devices and accessories',
      });
      await client.data.insert(categoriesTable, {
        name: 'Clothing',
        description: 'Apparel and fashion items',
      });

      // Insert products
      const productData = [
        { name: 'Laptop', category_name: 'Electronics', price: 999.99 },
        { name: 'Phone', category_name: 'Electronics', price: 699.99 },
        { name: 'T-Shirt', category_name: 'Clothing', price: 29.99 },
        { name: 'Jeans', category_name: 'Clothing', price: 59.99 },
      ];

      for (const product of productData) {
        await client.data.insert(productsTable, product);
      }

      // Query products by category
      const electronicsProducts = await client.data.query(productsTable, {
        filters: [{ column: 'category_name', operator: FilterOperator.EQ, value: 'Electronics' }],
      });
      expect(electronicsProducts.pagination.totalCount).toBe(2);

      // Query high-value products
      const expensiveProducts = await client.data.query(productsTable, {
        filters: [{ column: 'price', operator: FilterOperator.GTE, value: 100 }],
      });
      expect(expensiveProducts.pagination.totalCount).toBe(2);

      // Get all categories
      const categories = await client.data.query(categoriesTable, {});
      expect(categories.pagination.totalCount).toBe(2);
    });
  });

  describe('Large Dataset Operations', () => {
    it('should handle larger datasets efficiently', async () => {
      // Create table
      await client.schema.createTable({
        name: tableName,
        columns: [
          { name: 'index', type: 'integer', nullable: false },
          { name: 'value', type: 'text', nullable: false },
          { name: 'category', type: 'text', nullable: true },
        ],
      });

      try {
        // Insert 100 records
        const categories = ['A', 'B', 'C', 'D', 'E'];
        for (let i = 0; i < 100; i++) {
          await client.data.insert(tableName, {
            index: i,
            value: `Value ${i}`,
            category: categories[i % 5],
          });
        }

        // Verify count
        const result = await client.data.query(tableName, {});
        expect(result.pagination.totalCount).toBe(100);

        // Query by category
        for (const cat of categories) {
          const catResult = await client.data.query(tableName, {
            filters: [{ column: 'category', operator: FilterOperator.EQ, value: cat }],
          });
          expect(catResult.pagination.totalCount).toBe(20);
        }

        // Query with range
        const rangeResult = await client.data.query(tableName, {
          filters: [
            { column: 'index', operator: FilterOperator.GTE, value: 40 },
            { column: 'index', operator: FilterOperator.LT, value: 60 },
          ],
        });
        expect(rangeResult.pagination.totalCount).toBe(20);

        // Pagination through all records
        const allRecords: unknown[] = [];
        let page = 1;
        while (true) {
          const pageResult = await client.data.query(tableName, {
            page,
            pageSize: 25,
          });
          allRecords.push(...pageResult.data);
          if (!pageResult.pagination.hasNextPage) {
            break;
          }
          page++;
        }

        expect(allRecords.length).toBe(100);
      } finally {
        await client.schema.dropTable(tableName);
      }
    });
  });

  describe('Error Recovery', () => {
    it('should recover from constraint violations', async () => {
      await client.schema.createTable({
        name: tableName,
        columns: [
          { name: 'code', type: 'text', nullable: false, unique: true },
          { name: 'name', type: 'text', nullable: false },
        ],
      });

      // Insert first record
      await client.data.insert(tableName, { code: 'ABC123', name: 'First' });

      // Try to insert duplicate - should fail
      await expect(
        client.data.insert(tableName, { code: 'ABC123', name: 'Second' })
      ).rejects.toThrow();

      // Should be able to insert with different code
      const valid = await client.data.insert(tableName, { code: 'DEF456', name: 'Second' });
      expect(valid.data.code).toBe('DEF456');

      // Verify only 2 records exist
      const result = await client.data.query(tableName, {});
      expect(result.pagination.totalCount).toBe(2);
    });

    it('should handle concurrent operations gracefully', async () => {
      await client.schema.createTable({
        name: tableName,
        columns: [
          { name: 'counter', type: 'integer', nullable: false },
          { name: 'name', type: 'text', nullable: false },
        ],
      });

      // Insert records concurrently
      const insertPromises = [];
      for (let i = 0; i < 10; i++) {
        insertPromises.push(
          client.data.insert(tableName, { counter: i, name: `Item ${i}` })
        );
      }

      const results = await Promise.all(insertPromises);
      expect(results.length).toBe(10);

      // Verify all records were inserted
      const queryResult = await client.data.query(tableName, {});
      expect(queryResult.pagination.totalCount).toBe(10);
    });
  });
});
