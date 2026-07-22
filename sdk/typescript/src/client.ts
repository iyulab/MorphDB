import { HttpClient } from './http.js';
import { SchemaClient } from './schema.js';
import { DataClient } from './data.js';
import { BatchClient } from './batch.js';
import { WebhookClient } from './webhook.js';
import { BulkClient } from './bulk.js';
import { RealtimeClient } from './realtime.js';
import type { MorphDBClientOptions } from './types.js';

/**
 * Main client for interacting with MorphDB
 */
export class MorphDBClient {
  private readonly http: HttpClient;
  private readonly baseUrl: string;
  private readonly options: MorphDBClientOptions;

  /**
   * Schema management operations
   */
  public readonly schema: SchemaClient;

  /**
   * Data operations
   */
  public readonly data: DataClient;

  /**
   * Batch data operations (many writes in one request)
   */
  public readonly batch: BatchClient;

  /**
   * Webhook operations
   */
  public readonly webhooks: WebhookClient;

  /**
   * Bulk import/export operations
   */
  public readonly bulk: BulkClient;

  /**
   * Real-time subscription operations
   */
  public readonly realtime: RealtimeClient;

  /**
   * Creates a new MorphDB client
   * @param baseUrl Base URL of the MorphDB server
   * @param options Client configuration options
   */
  constructor(baseUrl: string, options: MorphDBClientOptions = {}) {
    this.baseUrl = baseUrl.replace(/\/$/, '');
    this.options = options;
    this.http = new HttpClient(this.baseUrl, options);

    this.schema = new SchemaClient(this.http);
    this.data = new DataClient(this.http);
    this.batch = new BatchClient(this.http);
    this.webhooks = new WebhookClient(this.http);
    this.bulk = new BulkClient(this.http);
    this.realtime = new RealtimeClient(this.baseUrl, options);
  }

  /**
   * Sets the project ID for all requests
   */
  setProjectId(projectId: string): void {
    this.http.setProjectId(projectId);
  }

  /**
   * Disconnects all real-time connections
   */
  async disconnect(): Promise<void> {
    await this.realtime.disconnect();
  }
}
