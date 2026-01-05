/**
 * Integration tests for MorphDB TypeScript SDK data operations.
 *
 * These tests require a running MorphDB server.
 * Start the test server with: docker compose -f docker-compose.test.yml up -d
 *
 * Run with: npm run test:integration
 */

import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import { MorphDBClient } from '../../src/client.js';
import { MorphDBError } from '../../src/errors.js';
import { FilterOperator } from '../../src/types.js';
import { createTestClient, uniqueTableName, cleanupTable } from './test-utils.js';

describe('Data Integration Tests', () => {
  let client: MorphDBClient;
  let tableName: string;

  beforeEach(async () => {
    client = createTestClient();
    tableName = uniqueTableName();

    // Create test table for data operations
    await client.schema.createTable({
      name: tableName,
      columns: [
        { name: 'name', type: 'text', nullable: false },
        { name: 'email', type: 'text', unique: true },
        { name: 'age', type: 'integer', nullable: true },
        { name: 'active', type: 'boolean', nullable: true },
      ],
    });
  });

  afterEach(async () => {
    await cleanupTable(client, tableName);
  });

  describe('CRUD Operations', () => {
    it('should insert and get by id', async () => {
      // Insert
      const data = {
        name: 'John Doe',
        email: 'john@example.com',
        age: 30,
        active: true,
      };

      const record = await client.data.insert(tableName, data);

      expect(record.id).toBeDefined();
      expect(record.data.name).toBe('John Doe');
      expect(record.data.email).toBe('john@example.com');
      expect(record.data.age).toBe(30);
      expect(record.data.active).toBe(true);

      // Get by ID
      const retrieved = await client.data.getById(tableName, record.id);
      expect(retrieved.id).toBe(record.id);
      expect(retrieved.data.name).toBe('John Doe');
    });

    it('should update a record', async () => {
      // Insert
      const record = await client.data.insert(tableName, {
        name: 'Original Name',
        email: 'original@example.com',
        age: 25,
      });

      // Update
      const updated = await client.data.update(tableName, record.id, {
        name: 'Updated Name',
        age: 26,
      });

      expect(updated.data.name).toBe('Updated Name');
      expect(updated.data.email).toBe('original@example.com'); // Unchanged
      expect(updated.data.age).toBe(26);

      // Verify update persisted
      const retrieved = await client.data.getById(tableName, record.id);
      expect(retrieved.data.name).toBe('Updated Name');
    });

    it('should delete a record', async () => {
      // Insert
      const record = await client.data.insert(tableName, {
        name: 'To Delete',
        email: 'delete@example.com',
      });

      // Delete
      await client.data.delete(tableName, record.id);

      // Verify deletion
      await expect(client.data.getById(tableName, record.id)).rejects.toThrow(MorphDBError);
    });
  });

  describe('Query Operations', () => {
    beforeEach(async () => {
      // Insert test data
      const users = [
        { name: 'Alice', email: 'alice@example.com', age: 25, active: true },
        { name: 'Bob', email: 'bob@example.com', age: 35, active: true },
        { name: 'Charlie', email: 'charlie@example.com', age: 30, active: false },
        { name: 'David', email: 'david@example.com', age: 28, active: true },
      ];

      for (const user of users) {
        await client.data.insert(tableName, user);
      }
    });

    it('should query with numeric filter', async () => {
      const result = await client.data.query(tableName, {
        filters: [{ column: 'age', operator: FilterOperator.GTE, value: 30 }],
      });

      expect(result.pagination.totalCount).toBe(2);
      const names = result.data.map((r) => r.data.name);
      expect(names).toContain('Bob');
      expect(names).toContain('Charlie');
    });

    it('should query with boolean filter', async () => {
      const result = await client.data.query(tableName, {
        filters: [{ column: 'active', operator: FilterOperator.EQ, value: true }],
      });

      expect(result.pagination.totalCount).toBe(3);
    });

    it('should query with text contains filter', async () => {
      const result = await client.data.query(tableName, {
        filters: [{ column: 'name', operator: FilterOperator.CONTAINS, value: 'li' }],
      });

      expect(result.pagination.totalCount).toBe(2);
      const names = result.data.map((r) => r.data.name);
      expect(names).toContain('Alice');
      expect(names).toContain('Charlie');
    });

    it('should query with ordering ascending', async () => {
      const result = await client.data.query(tableName, {
        orderBy: [{ column: 'name', ascending: true }],
      });

      const names = result.data.map((r) => r.data.name);
      expect(names).toEqual(['Alice', 'Bob', 'Charlie', 'David']);
    });

    it('should query with ordering descending', async () => {
      const result = await client.data.query(tableName, {
        orderBy: [{ column: 'age', ascending: false }],
      });

      const ages = result.data.map((r) => r.data.age);
      expect(ages).toEqual([35, 30, 28, 25]);
    });

    it('should query with pagination', async () => {
      // Get first page
      const page1 = await client.data.query(tableName, {
        page: 1,
        pageSize: 2,
      });

      expect(page1.data.length).toBe(2);
      expect(page1.pagination.totalCount).toBe(4);
      expect(page1.pagination.totalPages).toBe(2);
      expect(page1.pagination.hasNextPage).toBe(true);
      expect(page1.pagination.hasPreviousPage).toBe(false);

      // Get second page
      const page2 = await client.data.query(tableName, {
        page: 2,
        pageSize: 2,
      });

      expect(page2.data.length).toBe(2);
      expect(page2.pagination.hasNextPage).toBe(false);
      expect(page2.pagination.hasPreviousPage).toBe(true);
    });

    it('should query with column selection', async () => {
      const result = await client.data.query(tableName, {
        select: ['name', 'email'],
      });

      expect(result.data.length).toBe(4);
      const record = result.data[0];
      expect(record.data.name).toBeDefined();
      expect(record.data.email).toBeDefined();
    });
  });

  describe('Batch Operations', () => {
    it('should perform batch insert, update, and delete', async () => {
      // Insert initial records for updating and deleting
      const record1 = await client.data.insert(tableName, {
        name: 'Update Me',
        email: 'update@example.com',
      });
      const record2 = await client.data.insert(tableName, {
        name: 'Delete Me',
        email: 'delete@example.com',
      });

      // Batch operation
      const result = await client.data.batch(tableName, {
        inserts: [
          { name: 'New User 1', email: 'new1@example.com', age: 21 },
          { name: 'New User 2', email: 'new2@example.com', age: 22 },
        ],
        updates: [{ _id: record1.id, name: 'Updated User', age: 30 }],
        deletes: [record2.id],
      });

      expect(result.inserted.length).toBe(2);
      expect(result.updated.length).toBe(1);
      expect(result.deleted).toBe(1);

      // Verify inserts
      const queryResult = await client.data.query(tableName, {
        filters: [{ column: 'name', operator: FilterOperator.STARTSWITH, value: 'New User' }],
      });
      expect(queryResult.pagination.totalCount).toBe(2);

      // Verify update
      const retrieved = await client.data.getById(tableName, record1.id);
      expect(retrieved.data.name).toBe('Updated User');

      // Verify delete
      await expect(client.data.getById(tableName, record2.id)).rejects.toThrow(MorphDBError);
    });
  });

  describe('Edge Cases', () => {
    it('should handle null values', async () => {
      const record = await client.data.insert(tableName, {
        name: 'Nullable Test',
        email: 'nullable@example.com',
        age: null,
        active: null,
      });

      expect(record.data.age).toBeNull();
      expect(record.data.active).toBeNull();

      const retrieved = await client.data.getById(tableName, record.id);
      expect(retrieved.data.age).toBeNull();
    });

    it('should handle special characters in text', async () => {
      const specialName = "O'Brien & Sons \"Inc.\"";
      const record = await client.data.insert(tableName, {
        name: specialName,
        email: 'special@example.com',
      });

      expect(record.data.name).toBe(specialName);

      const retrieved = await client.data.getById(tableName, record.id);
      expect(retrieved.data.name).toBe(specialName);
    });

    it('should throw on non-existent record', async () => {
      await expect(client.data.getById(tableName, 'non-existent-id')).rejects.toThrow(MorphDBError);
    });
  });
});
