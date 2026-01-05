import { describe, it, expect, vi, beforeEach, type Mock } from 'vitest';
import { SchemaClient } from '../src/schema.js';
import {
  createMockHttpClient,
  createSampleTableInfo,
  createSampleColumnInfo,
} from './test-utils.js';
import type { HttpClient } from '../src/http.js';
import type { CreateTableRequest, AddColumnRequest, AlterColumnRequest } from '../src/types.js';

describe('SchemaClient', () => {
  let schemaClient: SchemaClient;
  let mockHttp: HttpClient;

  beforeEach(() => {
    mockHttp = createMockHttpClient();
    schemaClient = new SchemaClient(mockHttp);
  });

  describe('getTables', () => {
    it('gets all tables', async () => {
      const tables = [createSampleTableInfo()];
      (mockHttp.get as Mock).mockResolvedValue(tables);

      const result = await schemaClient.getTables();

      expect(result).toEqual(tables);
      expect(mockHttp.get).toHaveBeenCalledWith('/api/schema/tables');
    });

    it('returns empty array when no tables exist', async () => {
      (mockHttp.get as Mock).mockResolvedValue([]);

      const result = await schemaClient.getTables();

      expect(result).toEqual([]);
    });
  });

  describe('getTable', () => {
    it('gets table by name', async () => {
      const table = createSampleTableInfo();
      (mockHttp.get as Mock).mockResolvedValue(table);

      const result = await schemaClient.getTable('users');

      expect(result).toEqual(table);
      expect(mockHttp.get).toHaveBeenCalledWith('/api/schema/tables/users');
    });

    it('returns null when table not found', async () => {
      const error = new Error('Not found');
      (error as Error & { statusCode: number }).statusCode = 404;
      (mockHttp.get as Mock).mockRejectedValue(error);

      const result = await schemaClient.getTable('nonexistent');

      expect(result).toBeNull();
    });

    it('throws on other errors', async () => {
      const error = new Error('Server error');
      (error as Error & { statusCode: number }).statusCode = 500;
      (mockHttp.get as Mock).mockRejectedValue(error);

      await expect(schemaClient.getTable('users')).rejects.toThrow('Server error');
    });

    it('encodes table name in URL', async () => {
      (mockHttp.get as Mock).mockResolvedValue(createSampleTableInfo());

      await schemaClient.getTable('table with spaces');

      expect(mockHttp.get).toHaveBeenCalledWith(
        '/api/schema/tables/table%20with%20spaces'
      );
    });
  });

  describe('createTable', () => {
    it('creates a new table', async () => {
      const table = createSampleTableInfo();
      (mockHttp.post as Mock).mockResolvedValue(table);

      const request: CreateTableRequest = {
        name: 'users',
        columns: [
          { name: 'name', type: 'text', nullable: false },
          { name: 'email', type: 'text', unique: true },
        ],
      };

      const result = await schemaClient.createTable(request);

      expect(result).toEqual(table);
      expect(mockHttp.post).toHaveBeenCalledWith('/api/schema/tables', request);
    });

    it('creates table with description', async () => {
      const table = createSampleTableInfo();
      (mockHttp.post as Mock).mockResolvedValue(table);

      const request: CreateTableRequest = {
        name: 'users',
        columns: [{ name: 'name', type: 'text' }],
        description: 'User accounts table',
      };

      await schemaClient.createTable(request);

      expect(mockHttp.post).toHaveBeenCalledWith('/api/schema/tables', request);
    });
  });

  describe('dropTable', () => {
    it('drops a table', async () => {
      (mockHttp.delete as Mock).mockResolvedValue(undefined);

      await schemaClient.dropTable('users');

      expect(mockHttp.delete).toHaveBeenCalledWith('/api/schema/tables/users');
    });

    it('encodes table name in URL', async () => {
      (mockHttp.delete as Mock).mockResolvedValue(undefined);

      await schemaClient.dropTable('my table');

      expect(mockHttp.delete).toHaveBeenCalledWith('/api/schema/tables/my%20table');
    });
  });

  describe('addColumn', () => {
    it('adds a column to a table', async () => {
      const column = createSampleColumnInfo('age', 'integer');
      (mockHttp.post as Mock).mockResolvedValue(column);

      const request: AddColumnRequest = {
        name: 'age',
        type: 'integer',
        nullable: true,
        defaultValue: '0',
      };

      const result = await schemaClient.addColumn('users', request);

      expect(result).toEqual(column);
      expect(mockHttp.post).toHaveBeenCalledWith(
        '/api/schema/tables/users/columns',
        request
      );
    });
  });

  describe('alterColumn', () => {
    it('renames a column', async () => {
      const column = createSampleColumnInfo('full_name', 'text');
      (mockHttp.patch as Mock).mockResolvedValue(column);

      const request: AlterColumnRequest = { newName: 'full_name' };

      const result = await schemaClient.alterColumn('users', 'name', request);

      expect(result).toEqual(column);
      expect(mockHttp.patch).toHaveBeenCalledWith(
        '/api/schema/tables/users/columns/name',
        request
      );
    });

    it('changes column type', async () => {
      const column = createSampleColumnInfo('name', 'text');
      (mockHttp.patch as Mock).mockResolvedValue(column);

      const request: AlterColumnRequest = { newType: 'text' };

      await schemaClient.alterColumn('users', 'name', request);

      expect(mockHttp.patch).toHaveBeenCalledWith(
        '/api/schema/tables/users/columns/name',
        request
      );
    });

    it('changes column nullability', async () => {
      const column = createSampleColumnInfo('email', 'text');
      (mockHttp.patch as Mock).mockResolvedValue(column);

      const request: AlterColumnRequest = { nullable: true };

      await schemaClient.alterColumn('users', 'email', request);

      expect(mockHttp.patch).toHaveBeenCalledWith(
        '/api/schema/tables/users/columns/email',
        request
      );
    });
  });

  describe('dropColumn', () => {
    it('drops a column from a table', async () => {
      (mockHttp.delete as Mock).mockResolvedValue(undefined);

      await schemaClient.dropColumn('users', 'email');

      expect(mockHttp.delete).toHaveBeenCalledWith(
        '/api/schema/tables/users/columns/email'
      );
    });

    it('encodes column name in URL', async () => {
      (mockHttp.delete as Mock).mockResolvedValue(undefined);

      await schemaClient.dropColumn('users', 'my column');

      expect(mockHttp.delete).toHaveBeenCalledWith(
        '/api/schema/tables/users/columns/my%20column'
      );
    });
  });
});
