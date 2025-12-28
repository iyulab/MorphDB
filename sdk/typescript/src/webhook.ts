import { HttpClient } from './http.js';
import type { CreateWebhookRequest, WebhookInfo, WebhookDelivery } from './types.js';

/**
 * Client for webhook operations
 */
export class WebhookClient {
  constructor(private readonly http: HttpClient) {}

  /**
   * Gets all webhooks
   */
  async getAll(): Promise<WebhookInfo[]> {
    return this.http.get<WebhookInfo[]>('/api/webhooks');
  }

  /**
   * Gets a webhook by ID
   */
  async getById(webhookId: string): Promise<WebhookInfo | null> {
    try {
      return await this.http.get<WebhookInfo>(`/api/webhooks/${webhookId}`);
    } catch (error: unknown) {
      if (error instanceof Error && 'statusCode' in error && (error as { statusCode: number }).statusCode === 404) {
        return null;
      }
      throw error;
    }
  }

  /**
   * Gets webhooks for a table
   */
  async getByTable(tableName: string): Promise<WebhookInfo[]> {
    return this.http.get<WebhookInfo[]>(
      `/api/webhooks?tableName=${encodeURIComponent(tableName)}`
    );
  }

  /**
   * Creates a new webhook
   */
  async create(request: CreateWebhookRequest): Promise<WebhookInfo> {
    return this.http.post<WebhookInfo>('/api/webhooks', request);
  }

  /**
   * Deletes a webhook
   */
  async delete(webhookId: string): Promise<void> {
    await this.http.delete(`/api/webhooks/${webhookId}`);
  }

  /**
   * Gets webhook deliveries
   */
  async getDeliveries(
    webhookId: string,
    page = 1,
    pageSize = 50
  ): Promise<WebhookDelivery[]> {
    return this.http.get<WebhookDelivery[]>(
      `/api/webhooks/${webhookId}/deliveries?page=${page}&pageSize=${pageSize}`
    );
  }

  /**
   * Retries a failed delivery
   */
  async retryDelivery(deliveryId: string): Promise<void> {
    await this.http.post(`/api/webhooks/deliveries/${deliveryId}/retry`);
  }
}
