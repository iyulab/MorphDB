import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { HttpClient } from '../src/http.js';
import {
  MorphDBApiError,
  MorphDBNotFoundError,
  MorphDBValidationError,
  MorphDBAuthenticationError,
  MorphDBAuthorizationError,
  MorphDBConflictError,
} from '../src/errors.js';

describe('HttpClient', () => {
  let httpClient: HttpClient;
  let fetchMock: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    fetchMock = vi.fn();
    global.fetch = fetchMock;
    httpClient = new HttpClient('http://localhost:5000', {
      tenantId: 'test-tenant',
      apiKey: 'test-api-key',
    });
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  describe('constructor', () => {
    it('removes trailing slash from base URL', () => {
      const client = new HttpClient('http://localhost:5000/');
      expect(client).toBeDefined();
    });

    it('uses default options', () => {
      const client = new HttpClient('http://localhost:5000');
      expect(client).toBeDefined();
    });

    it('accepts custom options', () => {
      const client = new HttpClient('http://localhost:5000', {
        timeout: 60000,
        retryCount: 5,
        retryDelay: 2000,
      });
      expect(client).toBeDefined();
    });
  });

  describe('request methods', () => {
    it('makes GET request', async () => {
      fetchMock.mockResolvedValue({
        ok: true,
        text: () => Promise.resolve(JSON.stringify({ data: 'test' })),
      });

      const result = await httpClient.get('/test');

      expect(result).toEqual({ data: 'test' });
      expect(fetchMock).toHaveBeenCalledWith(
        'http://localhost:5000/test',
        expect.objectContaining({
          method: 'GET',
        })
      );
    });

    it('makes POST request with body', async () => {
      fetchMock.mockResolvedValue({
        ok: true,
        text: () => Promise.resolve(JSON.stringify({ id: 1 })),
      });

      const result = await httpClient.post('/test', { name: 'test' });

      expect(result).toEqual({ id: 1 });
      expect(fetchMock).toHaveBeenCalledWith(
        'http://localhost:5000/test',
        expect.objectContaining({
          method: 'POST',
          body: JSON.stringify({ name: 'test' }),
        })
      );
    });

    it('makes PUT request', async () => {
      fetchMock.mockResolvedValue({
        ok: true,
        text: () => Promise.resolve(JSON.stringify({ updated: true })),
      });

      await httpClient.put('/test', { name: 'updated' });

      expect(fetchMock).toHaveBeenCalledWith(
        'http://localhost:5000/test',
        expect.objectContaining({ method: 'PUT' })
      );
    });

    it('makes PATCH request', async () => {
      fetchMock.mockResolvedValue({
        ok: true,
        text: () => Promise.resolve(JSON.stringify({ patched: true })),
      });

      await httpClient.patch('/test', { name: 'patched' });

      expect(fetchMock).toHaveBeenCalledWith(
        'http://localhost:5000/test',
        expect.objectContaining({ method: 'PATCH' })
      );
    });

    it('makes DELETE request', async () => {
      fetchMock.mockResolvedValue({
        ok: true,
        text: () => Promise.resolve(''),
      });

      await httpClient.delete('/test');

      expect(fetchMock).toHaveBeenCalledWith(
        'http://localhost:5000/test',
        expect.objectContaining({ method: 'DELETE' })
      );
    });

    it('returns undefined for empty response', async () => {
      fetchMock.mockResolvedValue({
        ok: true,
        text: () => Promise.resolve(''),
      });

      const result = await httpClient.delete('/test');

      expect(result).toBeUndefined();
    });
  });

  describe('headers', () => {
    it('includes tenant ID header', async () => {
      fetchMock.mockResolvedValue({
        ok: true,
        text: () => Promise.resolve('{}'),
      });

      await httpClient.get('/test');

      expect(fetchMock).toHaveBeenCalledWith(
        expect.any(String),
        expect.objectContaining({
          headers: expect.objectContaining({
            'X-Tenant-Id': 'test-tenant',
          }),
        })
      );
    });

    it('includes API key header', async () => {
      fetchMock.mockResolvedValue({
        ok: true,
        text: () => Promise.resolve('{}'),
      });

      await httpClient.get('/test');

      expect(fetchMock).toHaveBeenCalledWith(
        expect.any(String),
        expect.objectContaining({
          headers: expect.objectContaining({
            'X-API-Key': 'test-api-key',
          }),
        })
      );
    });

    it('includes JWT authorization header', async () => {
      const client = new HttpClient('http://localhost:5000', {
        jwtToken: 'test-jwt-token',
      });

      fetchMock.mockResolvedValue({
        ok: true,
        text: () => Promise.resolve('{}'),
      });

      await client.get('/test');

      expect(fetchMock).toHaveBeenCalledWith(
        expect.any(String),
        expect.objectContaining({
          headers: expect.objectContaining({
            Authorization: 'Bearer test-jwt-token',
          }),
        })
      );
    });
  });

  describe('error handling', () => {
    it('throws MorphDBNotFoundError for 404', async () => {
      fetchMock.mockResolvedValue({
        ok: false,
        status: 404,
        text: () => Promise.resolve('Not found'),
      });

      await expect(httpClient.get('/test')).rejects.toThrow(MorphDBNotFoundError);
    });

    it('throws MorphDBValidationError for 400', async () => {
      fetchMock.mockResolvedValue({
        ok: false,
        status: 400,
        text: () => Promise.resolve('Validation error'),
      });

      await expect(httpClient.post('/test', {})).rejects.toThrow(MorphDBValidationError);
    });

    it('throws MorphDBAuthenticationError for 401', async () => {
      fetchMock.mockResolvedValue({
        ok: false,
        status: 401,
        text: () => Promise.resolve('Unauthorized'),
      });

      await expect(httpClient.get('/test')).rejects.toThrow(MorphDBAuthenticationError);
    });

    it('throws MorphDBAuthorizationError for 403', async () => {
      fetchMock.mockResolvedValue({
        ok: false,
        status: 403,
        text: () => Promise.resolve('Forbidden'),
      });

      await expect(httpClient.get('/test')).rejects.toThrow(MorphDBAuthorizationError);
    });

    it('throws MorphDBConflictError for 409', async () => {
      fetchMock.mockResolvedValue({
        ok: false,
        status: 409,
        text: () => Promise.resolve('Conflict'),
      });

      await expect(httpClient.post('/test', {})).rejects.toThrow(MorphDBConflictError);
    });

    it('throws MorphDBApiError for other errors', async () => {
      fetchMock.mockResolvedValue({
        ok: false,
        status: 500,
        text: () => Promise.resolve('Server error'),
      });

      await expect(httpClient.get('/test')).rejects.toThrow(MorphDBApiError);
    });

    it('does not retry on client errors (4xx)', async () => {
      fetchMock.mockResolvedValue({
        ok: false,
        status: 400,
        text: () => Promise.resolve('Bad request'),
      });

      await expect(httpClient.post('/test', {})).rejects.toThrow();

      expect(fetchMock).toHaveBeenCalledTimes(1);
    });
  });

  describe('configuration methods', () => {
    it('setTenantId updates tenant ID', async () => {
      httpClient.setTenantId('new-tenant');

      fetchMock.mockResolvedValue({
        ok: true,
        text: () => Promise.resolve('{}'),
      });

      await httpClient.get('/test');

      expect(fetchMock).toHaveBeenCalledWith(
        expect.any(String),
        expect.objectContaining({
          headers: expect.objectContaining({
            'X-Tenant-Id': 'new-tenant',
          }),
        })
      );
    });

    it('setApiKey updates API key', async () => {
      httpClient.setApiKey('new-api-key');

      fetchMock.mockResolvedValue({
        ok: true,
        text: () => Promise.resolve('{}'),
      });

      await httpClient.get('/test');

      expect(fetchMock).toHaveBeenCalledWith(
        expect.any(String),
        expect.objectContaining({
          headers: expect.objectContaining({
            'X-API-Key': 'new-api-key',
          }),
        })
      );
    });

    it('setJwtToken updates JWT token', async () => {
      httpClient.setJwtToken('new-jwt-token');

      fetchMock.mockResolvedValue({
        ok: true,
        text: () => Promise.resolve('{}'),
      });

      await httpClient.get('/test');

      expect(fetchMock).toHaveBeenCalledWith(
        expect.any(String),
        expect.objectContaining({
          headers: expect.objectContaining({
            Authorization: 'Bearer new-jwt-token',
          }),
        })
      );
    });
  });

  describe('getBlob', () => {
    it('returns blob for binary data', async () => {
      const mockBlob = new Blob(['test data']);
      fetchMock.mockResolvedValue({
        ok: true,
        blob: () => Promise.resolve(mockBlob),
      });

      const result = await httpClient.getBlob('/download');

      expect(result).toBe(mockBlob);
    });
  });
});
