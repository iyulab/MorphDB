/**
 * Integration tests for MorphDB TypeScript SDK schema operations.
 *
 * These tests require a running MorphDB server.
 * Start the test server with: docker compose -f docker-compose.test.yml up -d
 *
 * Run with: npm run test:integration
 */

import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import { MorphDBClient } from '../../src/client.js';
import { MorphDBError } from '../../src/errors.js';
import { createTestClient, uniqueTableName, cleanupTable } from './test-utils.js';

describe('Schema Integration Tests', () => {
  let client: MorphDBClient;
  let tableName: string;

  beforeEach(() => {
    client = createTestClient();
    tableName = uniqueTableName();
  });

  afterEach(async () => {
    await cleanupTable(client, tableName);
  });

  describe('Table Operations', () => {
    it('should create and retrieve a table', async () => {
      // Create table
      const table = await client.schema.createTable({
        name: tableName,
        columns: [
          { name: 'name', type: 'text', nullable: false },
          { name: 'email', type: 'text', unique: true },
          { name: 'age', type: 'integer', nullable: true },
        ],
        description: 'Test table for integration tests',
      });

      // Verify created table
      expect(table.name).toBe(tableName);
      expect(table.columns.length).toBeGreaterThanOrEqual(4); // 3 user + _id at minimum
      expect(table.schemaVersion).toBe(1);

      // Find user-defined columns (exclude system columns)
      const userColumns = table.columns.filter((col) => !col.name.startsWith('_'));
      expect(userColumns.map((c) => c.name)).toContain('name');
      expect(userColumns.map((c) => c.name)).toContain('email');
      expect(userColumns.map((c) => c.name)).toContain('age');

      // Verify column properties
      const nameCol = userColumns.find((c) => c.name === 'name');
      const emailCol = userColumns.find((c) => c.name === 'email');
      const ageCol = userColumns.find((c) => c.name === 'age');

      expect(nameCol?.nullable).toBe(false);
      expect(emailCol?.unique).toBe(true);
      expect(ageCol?.nullable).toBe(true);

      // Get table
      const retrieved = await client.schema.getTable(tableName);
      expect(retrieved.name).toBe(tableName);
      expect(retrieved.tableId).toBe(table.tableId);
    });

    it('should list all tables', async () => {
      // Create a test table
      await client.schema.createTable({
        name: tableName,
        columns: [{ name: 'value', type: 'text' }],
      });

      // List tables
      const tables = await client.schema.getTables();
      expect(tables.length).toBeGreaterThanOrEqual(1);

      const tableNames = tables.map((t) => t.name);
      expect(tableNames).toContain(tableName);
    });

    it('should drop a table', async () => {
      // Create table
      await client.schema.createTable({
        name: tableName,
        columns: [{ name: 'value', type: 'text' }],
      });

      // Drop table
      await client.schema.dropTable(tableName);

      // Verify table was dropped
      await expect(client.schema.getTable(tableName)).rejects.toThrow(MorphDBError);
    });

    it('should create table with all column types', async () => {
      const table = await client.schema.createTable({
        name: tableName,
        columns: [
          { name: 'text_col', type: 'text' },
          { name: 'int_col', type: 'integer' },
          { name: 'bigint_col', type: 'bigint' },
          { name: 'decimal_col', type: 'decimal' },
          { name: 'bool_col', type: 'boolean' },
          { name: 'date_col', type: 'date' },
          { name: 'timestamp_col', type: 'timestamp' },
          { name: 'json_col', type: 'jsonb' },
          { name: 'uuid_col', type: 'uuid' },
        ],
      });

      // Verify all columns exist
      const userColumns = table.columns.filter((col) => !col.name.startsWith('_'));
      const expectedColumns = [
        'text_col',
        'int_col',
        'bigint_col',
        'decimal_col',
        'bool_col',
        'date_col',
        'timestamp_col',
        'json_col',
        'uuid_col',
      ];

      for (const colName of expectedColumns) {
        expect(userColumns.map((c) => c.name)).toContain(colName);
      }
    });
  });

  describe('Column Operations', () => {
    it('should add a column', async () => {
      // Create table
      await client.schema.createTable({
        name: tableName,
        columns: [{ name: 'name', type: 'text' }],
      });

      // Add column
      const updated = await client.schema.addColumn(tableName, {
        name: 'status',
        type: 'text',
        nullable: true,
        defaultValue: "'active'",
      });

      // Verify column was added
      const columnNames = updated.columns.map((c) => c.name);
      expect(columnNames).toContain('status');
    });

    it('should drop a column', async () => {
      // Create table with multiple columns
      await client.schema.createTable({
        name: tableName,
        columns: [
          { name: 'name', type: 'text' },
          { name: 'temp_column', type: 'text' },
        ],
      });

      // Drop the temporary column
      const updated = await client.schema.dropColumn(tableName, 'temp_column');

      // Verify column was dropped
      const columnNames = updated.columns.map((c) => c.name);
      expect(columnNames).not.toContain('temp_column');
      expect(columnNames).toContain('name');
    });

    it('should alter a column', async () => {
      // Create table
      await client.schema.createTable({
        name: tableName,
        columns: [{ name: 'old_name', type: 'text' }],
      });

      // Alter column - rename
      const updated = await client.schema.alterColumn(tableName, 'old_name', {
        newName: 'new_name',
      });

      // Verify column was renamed
      const columnNames = updated.columns.map((c) => c.name);
      expect(columnNames).toContain('new_name');
      expect(columnNames).not.toContain('old_name');
    });
  });
});
