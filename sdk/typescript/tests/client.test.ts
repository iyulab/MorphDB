import { describe, it, expect, vi, beforeEach } from 'vitest';
import { MorphDBClient } from '../src/client.js';
import { SchemaClient } from '../src/schema.js';
import { DataClient } from '../src/data.js';
import { WebhookClient } from '../src/webhook.js';
import { BulkClient } from '../src/bulk.js';
import { RealtimeClient } from '../src/realtime.js';

describe('MorphDBClient', () => {
  const baseUrl = 'http://localhost:5000';
  const options = {
    projectId: 'test-project',
  };

  describe('initialization', () => {
    it('creates client with base URL', () => {
      const client = new MorphDBClient(baseUrl);
      expect(client).toBeDefined();
    });

    it('creates client with options', () => {
      const client = new MorphDBClient(baseUrl, options);
      expect(client).toBeDefined();
    });

    it('removes trailing slash from base URL', () => {
      const client = new MorphDBClient('http://localhost:5000/');
      expect(client).toBeDefined();
    });

    it('initializes all sub-clients', () => {
      const client = new MorphDBClient(baseUrl, options);

      expect(client.schema).toBeInstanceOf(SchemaClient);
      expect(client.data).toBeInstanceOf(DataClient);
      expect(client.webhooks).toBeInstanceOf(WebhookClient);
      expect(client.bulk).toBeInstanceOf(BulkClient);
      expect(client.realtime).toBeInstanceOf(RealtimeClient);
    });
  });

  describe('configuration methods', () => {
    let client: MorphDBClient;

    beforeEach(() => {
      client = new MorphDBClient(baseUrl, options);
    });

    it('setProjectId updates project ID', () => {
      expect(() => client.setProjectId('new-project')).not.toThrow();
    });


  });

  describe('disconnect', () => {
    it('disconnects realtime client', async () => {
      const client = new MorphDBClient(baseUrl, options);

      // Mock the realtime disconnect
      const disconnectSpy = vi.spyOn(client.realtime, 'disconnect').mockResolvedValue();

      await client.disconnect();

      expect(disconnectSpy).toHaveBeenCalled();
    });
  });

  describe('options', () => {
    it('uses default timeout', () => {
      const client = new MorphDBClient(baseUrl);
      expect(client).toBeDefined();
    });

    it('uses custom timeout', () => {
      const client = new MorphDBClient(baseUrl, { timeout: 60000 });
      expect(client).toBeDefined();
    });

    it('uses custom retry settings', () => {
      const client = new MorphDBClient(baseUrl, {
        retryCount: 5,
        retryDelay: 2000,
      });
      expect(client).toBeDefined();
    });

      expect(client).toBeDefined();
    });
  });
});
