import { describe, it, expect, vi, beforeEach, type Mock } from 'vitest';
import { WebhookClient } from '../src/webhook.js';
import {
  createMockHttpClient,
  createSampleWebhookInfo,
  createSampleWebhookDelivery,
} from './test-utils.js';
import type { HttpClient } from '../src/http.js';
import type { CreateWebhookRequest } from '../src/types.js';

describe('WebhookClient', () => {
  let webhookClient: WebhookClient;
  let mockHttp: HttpClient;

  beforeEach(() => {
    mockHttp = createMockHttpClient();
    webhookClient = new WebhookClient(mockHttp);
  });

  describe('getAll', () => {
    it('gets all webhooks', async () => {
      const webhooks = [createSampleWebhookInfo()];
      (mockHttp.get as Mock).mockResolvedValue(webhooks);

      const result = await webhookClient.getAll();

      expect(result).toEqual(webhooks);
      expect(mockHttp.get).toHaveBeenCalledWith('/api/webhooks');
    });

    it('returns empty array when no webhooks exist', async () => {
      (mockHttp.get as Mock).mockResolvedValue([]);

      const result = await webhookClient.getAll();

      expect(result).toEqual([]);
    });
  });

  describe('getById', () => {
    it('gets webhook by ID', async () => {
      const webhook = createSampleWebhookInfo();
      (mockHttp.get as Mock).mockResolvedValue(webhook);

      const result = await webhookClient.getById('webhook-123');

      expect(result).toEqual(webhook);
      expect(mockHttp.get).toHaveBeenCalledWith('/api/webhooks/webhook-123');
    });

    it('returns null when webhook not found', async () => {
      const error = new Error('Not found');
      (error as Error & { statusCode: number }).statusCode = 404;
      (mockHttp.get as Mock).mockRejectedValue(error);

      const result = await webhookClient.getById('nonexistent');

      expect(result).toBeNull();
    });

    it('throws on other errors', async () => {
      const error = new Error('Server error');
      (error as Error & { statusCode: number }).statusCode = 500;
      (mockHttp.get as Mock).mockRejectedValue(error);

      await expect(webhookClient.getById('id')).rejects.toThrow('Server error');
    });
  });

  describe('getByTable', () => {
    it('gets webhooks for a table', async () => {
      const webhooks = [createSampleWebhookInfo()];
      (mockHttp.get as Mock).mockResolvedValue(webhooks);

      const result = await webhookClient.getByTable('orders');

      expect(result).toEqual(webhooks);
      expect(mockHttp.get).toHaveBeenCalledWith('/api/webhooks?tableName=orders');
    });

    it('encodes table name in query string', async () => {
      (mockHttp.get as Mock).mockResolvedValue([]);

      await webhookClient.getByTable('my table');

      expect(mockHttp.get).toHaveBeenCalledWith('/api/webhooks?tableName=my%20table');
    });
  });

  describe('create', () => {
    it('creates a webhook', async () => {
      const webhook = createSampleWebhookInfo();
      (mockHttp.post as Mock).mockResolvedValue(webhook);

      const request: CreateWebhookRequest = {
        name: 'order-notifications',
        tableName: 'orders',
        url: 'https://example.com/webhook',
        events: ['insert', 'update', 'delete'],
      };

      const result = await webhookClient.create(request);

      expect(result).toEqual(webhook);
      expect(mockHttp.post).toHaveBeenCalledWith('/api/webhooks', request);
    });

    it('creates webhook with custom headers', async () => {
      const webhook = createSampleWebhookInfo();
      (mockHttp.post as Mock).mockResolvedValue(webhook);

      const request: CreateWebhookRequest = {
        name: 'order-notifications',
        tableName: 'orders',
        url: 'https://example.com/webhook',
        headers: { Authorization: 'Bearer token123' },
      };

      await webhookClient.create(request);

      expect(mockHttp.post).toHaveBeenCalledWith('/api/webhooks', request);
    });

    it('creates webhook with filter', async () => {
      const webhook = createSampleWebhookInfo();
      (mockHttp.post as Mock).mockResolvedValue(webhook);

      const request: CreateWebhookRequest = {
        name: 'order-notifications',
        tableName: 'orders',
        url: 'https://example.com/webhook',
        filter: 'status eq "active"',
      };

      await webhookClient.create(request);

      expect(mockHttp.post).toHaveBeenCalledWith('/api/webhooks', request);
    });
  });

  describe('delete', () => {
    it('deletes a webhook', async () => {
      (mockHttp.delete as Mock).mockResolvedValue(undefined);

      await webhookClient.delete('webhook-123');

      expect(mockHttp.delete).toHaveBeenCalledWith('/api/webhooks/webhook-123');
    });
  });

  describe('getDeliveries', () => {
    it('gets webhook deliveries', async () => {
      const deliveries = [createSampleWebhookDelivery()];
      (mockHttp.get as Mock).mockResolvedValue(deliveries);

      const result = await webhookClient.getDeliveries('webhook-123');

      expect(result).toEqual(deliveries);
      expect(mockHttp.get).toHaveBeenCalledWith(
        '/api/webhooks/webhook-123/deliveries?page=1&pageSize=50'
      );
    });

    it('gets deliveries with pagination', async () => {
      const deliveries = [createSampleWebhookDelivery()];
      (mockHttp.get as Mock).mockResolvedValue(deliveries);

      const result = await webhookClient.getDeliveries('webhook-123', 2, 25);

      expect(result).toEqual(deliveries);
      expect(mockHttp.get).toHaveBeenCalledWith(
        '/api/webhooks/webhook-123/deliveries?page=2&pageSize=25'
      );
    });
  });

  describe('retryDelivery', () => {
    it('retries a failed delivery', async () => {
      (mockHttp.post as Mock).mockResolvedValue(undefined);

      await webhookClient.retryDelivery('delivery-123');

      expect(mockHttp.post).toHaveBeenCalledWith(
        '/api/webhooks/deliveries/delivery-123/retry'
      );
    });
  });
});
