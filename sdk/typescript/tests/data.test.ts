import { describe, it, expect, vi, beforeEach, type Mock } from 'vitest';
import { DataClient } from '../src/data.js';
import {
  createMockHttpClient,
  createSampleDataRecord,
  createSamplePagedResponse,
} from './test-utils.js';
import type { HttpClient } from '../src/http.js';
import type { QueryRequest } from '../src/types.js';

describe('DataClient', () => {
  let dataClient: DataClient;
  let mockHttp: HttpClient;

  beforeEach(() => {
    mockHttp = createMockHttpClient();
    dataClient = new DataClient(mockHttp);
  });

  describe('query', () => {
    it('queries with default parameters', async () => {
      const response = createSamplePagedResponse([createSampleDataRecord()]);
      (mockHttp.get as Mock).mockResolvedValue(response);

      const result = await dataClient.query('users');

      expect(result).toEqual(response);
      expect(mockHttp.get).toHaveBeenCalledWith('/api/data/users');
    });

    it('queries with filters', async () => {
      const response = createSamplePagedResponse([createSampleDataRecord()]);
      (mockHttp.get as Mock).mockResolvedValue(response);

      const request: QueryRequest = {
        filters: [{ column: 'name', operator: 'contains', value: 'John' }],
      };

      await dataClient.query('users', request);

      expect(mockHttp.get).toHaveBeenCalledWith(
        '/api/data/users?filter=name%3Acontains%3AJohn'
      );
    });

    it('queries with pagination', async () => {
      const response = createSamplePagedResponse([createSampleDataRecord()], 2, 25);
      (mockHttp.get as Mock).mockResolvedValue(response);

      const request: QueryRequest = { page: 2, pageSize: 25 };

      await dataClient.query('users', request);

      expect(mockHttp.get).toHaveBeenCalledWith('/api/data/users?page=2&pageSize=25');
    });

    it('queries with order by', async () => {
      const response = createSamplePagedResponse([createSampleDataRecord()]);
      (mockHttp.get as Mock).mockResolvedValue(response);

      const request: QueryRequest = {
        orderBy: [
          { column: 'name', ascending: true },
          { column: 'email', ascending: false },
        ],
      };

      await dataClient.query('users', request);

      expect(mockHttp.get).toHaveBeenCalledWith(
        '/api/data/users?orderBy=name%3Aasc%2Cemail%3Adesc'
      );
    });

    it('queries with column selection', async () => {
      const response = createSamplePagedResponse([createSampleDataRecord()]);
      (mockHttp.get as Mock).mockResolvedValue(response);

      const request: QueryRequest = { select: ['name', 'email'] };

      await dataClient.query('users', request);

      expect(mockHttp.get).toHaveBeenCalledWith('/api/data/users?select=name%2Cemail');
    });

    it('encodes table name in URL', async () => {
      const response = createSamplePagedResponse([createSampleDataRecord()]);
      (mockHttp.get as Mock).mockResolvedValue(response);

      await dataClient.query('my table');

      expect(mockHttp.get).toHaveBeenCalledWith('/api/data/my%20table');
    });
  });

  describe('getById', () => {
    it('gets record by ID', async () => {
      const record = createSampleDataRecord();
      (mockHttp.get as Mock).mockResolvedValue(record);

      const result = await dataClient.getById('users', 'record-123');

      expect(result).toEqual(record);
      expect(mockHttp.get).toHaveBeenCalledWith('/api/data/users/record-123');
    });

    it('returns null when record not found', async () => {
      const error = new Error('Not found');
      (error as Error & { statusCode: number }).statusCode = 404;
      (mockHttp.get as Mock).mockRejectedValue(error);

      const result = await dataClient.getById('users', 'nonexistent');

      expect(result).toBeNull();
    });

    it('throws on other errors', async () => {
      const error = new Error('Server error');
      (error as Error & { statusCode: number }).statusCode = 500;
      (mockHttp.get as Mock).mockRejectedValue(error);

      await expect(dataClient.getById('users', 'id')).rejects.toThrow('Server error');
    });
  });

  describe('insert', () => {
    it('inserts a new record', async () => {
      const record = createSampleDataRecord();
      (mockHttp.post as Mock).mockResolvedValue(record);

      const data = { name: 'John Doe', email: 'john@example.com' };
      const result = await dataClient.insert('users', data);

      expect(result).toEqual(record);
      expect(mockHttp.post).toHaveBeenCalledWith('/api/data/users', data);
    });
  });

  describe('update', () => {
    it('updates an existing record', async () => {
      const record = createSampleDataRecord({ name: 'Jane Doe' });
      (mockHttp.patch as Mock).mockResolvedValue(record);

      const data = { name: 'Jane Doe' };
      const result = await dataClient.update('users', 'record-123', data);

      expect(result).toEqual(record);
      expect(mockHttp.patch).toHaveBeenCalledWith('/api/data/users/record-123', data);
    });
  });

  describe('delete', () => {
    it('deletes a record', async () => {
      (mockHttp.delete as Mock).mockResolvedValue(undefined);

      await dataClient.delete('users', 'record-123');

      expect(mockHttp.delete).toHaveBeenCalledWith('/api/data/users/record-123');
    });
  });

  describe('upsert', () => {
    it('upserts a record', async () => {
      const record = createSampleDataRecord();
      (mockHttp.put as Mock).mockResolvedValue(record);

      const data = { name: 'John Doe', email: 'john@example.com' };
      const result = await dataClient.upsert('users', data);

      expect(result).toEqual(record);
      expect(mockHttp.put).toHaveBeenCalledWith('/api/data/users', data);
    });
  });
});

describe('QueryRequest building', () => {
  let dataClient: DataClient;
  let mockHttp: HttpClient;

  beforeEach(() => {
    mockHttp = createMockHttpClient();
    dataClient = new DataClient(mockHttp);
    (mockHttp.get as Mock).mockResolvedValue(createSamplePagedResponse([]));
  });

  it('does not include page=1 in query string', async () => {
    await dataClient.query('users', { page: 1 });
    expect(mockHttp.get).toHaveBeenCalledWith('/api/data/users');
  });

  it('does not include default pageSize=50 in query string', async () => {
    await dataClient.query('users', { pageSize: 50 });
    expect(mockHttp.get).toHaveBeenCalledWith('/api/data/users');
  });

  it('handles multiple filters', async () => {
    await dataClient.query('users', {
      filters: [
        { column: 'name', operator: 'eq', value: 'John' },
        { column: 'age', operator: 'gt', value: 18 },
      ],
    });

    const call = (mockHttp.get as Mock).mock.calls[0][0];
    expect(call).toContain('filter=name%3Aeq%3AJohn');
    expect(call).toContain('filter=age%3Agt%3A18');
  });

  it('handles ascending order (default)', async () => {
    await dataClient.query('users', {
      orderBy: [{ column: 'name' }],
    });

    expect(mockHttp.get).toHaveBeenCalledWith('/api/data/users?orderBy=name%3Aasc');
  });

  it('handles descending order', async () => {
    await dataClient.query('users', {
      orderBy: [{ column: 'name', ascending: false }],
    });

    expect(mockHttp.get).toHaveBeenCalledWith('/api/data/users?orderBy=name%3Adesc');
  });
});
