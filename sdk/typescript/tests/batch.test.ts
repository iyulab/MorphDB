import { describe, it, expect, beforeEach, type Mock } from 'vitest';
import { BatchClient } from '../src/batch.js';
import { createMockHttpClient, createSampleBatchResponse } from './test-utils.js';
import type { HttpClient } from '../src/http.js';
import type { BatchRequest } from '../src/types.js';

/**
 * The routes asserted here are the ones BatchController serves:
 * `[Route("api/batch")]` with `[HttpPost("data")]` and `[HttpPost("data/{table}/insert")]`.
 *
 * The previous batch method targeted `/api/data/{table}/batch`, which no controller serves — the
 * server answered 405 — and its request and response shapes matched no endpoint. Its test asserted
 * the same wrong route against a mock, so it passed while nothing worked.
 */
describe('BatchClient', () => {
  let batchClient: BatchClient;
  let mockHttp: HttpClient;

  beforeEach(() => {
    mockHttp = createMockHttpClient();
    batchClient = new BatchClient(mockHttp);
  });

  describe('insertMany', () => {
    it('posts the records to the route the server serves', async () => {
      const response = createSampleBatchResponse();
      (mockHttp.post as Mock).mockResolvedValue(response);
      const records = [{ name: 'User 1' }, { name: 'User 2' }];

      const result = await batchClient.insertMany('users', records);

      expect(mockHttp.post).toHaveBeenCalledWith('/api/batch/data/users/insert', records);
      expect(result.successCount).toBe(2);
      expect(result.results).toHaveLength(2);
    });

    it('encodes the table name', async () => {
      (mockHttp.post as Mock).mockResolvedValue(createSampleBatchResponse());

      await batchClient.insertMany('my table', []);

      expect(mockHttp.post).toHaveBeenCalledWith('/api/batch/data/my%20table/insert', []);
    });
  });

  describe('execute', () => {
    it('posts the operations to the route the server serves', async () => {
      const response = createSampleBatchResponse();
      (mockHttp.post as Mock).mockResolvedValue(response);
      const request: BatchRequest = {
        operations: [
          { method: 'INSERT', table: 'users', data: { name: 'User 1' } },
          { method: 'DELETE', table: 'users', id: 'record-456' },
        ],
      };

      const result = await batchClient.execute(request);

      expect(mockHttp.post).toHaveBeenCalledWith('/api/batch/data', request);
      expect(result).toEqual(response);
    });
  });
});
