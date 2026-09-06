import * as signalR from '@microsoft/signalr';
import { MorphDBConnectionError } from './errors.js';
import type {
  MorphDBClientOptions,
  ChangeNotification,
  ChangeOperation,
  Subscription,
} from './types.js';

/**
 * Client for real-time subscriptions
 */
export class RealtimeClient {
  private connection: signalR.HubConnection | null = null;
  private readonly subscriptions = new Map<string, SubscriptionHandler>();

  constructor(
    private readonly baseUrl: string,
    private readonly options: MorphDBClientOptions
  ) {}

  /**
   * Whether the client is connected
   */
  get isConnected(): boolean {
    return this.connection?.state === signalR.HubConnectionState.Connected;
  }

  /**
   * Connects to the real-time hub
   */
  async connect(): Promise<void> {
    if (this.connection?.state === signalR.HubConnectionState.Connected) {
      return;
    }

    const hubUrl = `${this.baseUrl}/hubs/morph`;

    const builder = new signalR.HubConnectionBuilder().withUrl(hubUrl, {
      headers: this.getHeaders(),
    });

    this.connection = builder.withAutomaticReconnect().build();

    this.connection.on('ReceiveChange', (tableName: string, operation: string, dataJson: string) => {
      const handler = this.subscriptions.get(tableName);
      if (handler) {
        const changeOp = this.parseOperation(operation);
        const data = dataJson ? JSON.parse(dataJson) : undefined;

        const notification: ChangeNotification = {
          tableName,
          operation: changeOp,
          recordId: data?.id ?? '',
          data,
          projectId: this.options.projectId ?? '',
          timestamp: new Date().toISOString(),
        };

        handler.callback(notification);
      }
    });

    await this.connection.start();
  }

  /**
   * Subscribes to changes on a table
   */
  async subscribe(
    tableName: string,
    callback: (change: ChangeNotification) => void
  ): Promise<Subscription> {
    await this.connect();

    if (!this.connection) {
      throw new MorphDBConnectionError('Failed to connect to real-time hub');
    }

    const subscriptionId = crypto.randomUUID();
    const handler: SubscriptionHandler = {
      subscriptionId,
      tableName,
      callback,
      isActive: true,
    };

    this.subscriptions.set(tableName, handler);

    await this.connection.invoke('Subscribe', tableName);

    return {
      subscriptionId,
      tableName,
      isActive: true,
      unsubscribe: async () => {
        await this.unsubscribe(tableName);
      },
    };
  }

  /**
   * Unsubscribes from a table
   */
  private async unsubscribe(tableName: string): Promise<void> {
    if (this.connection?.state === signalR.HubConnectionState.Connected) {
      await this.connection.invoke('Unsubscribe', tableName);
    }

    this.subscriptions.delete(tableName);
  }

  /**
   * Disconnects from the real-time hub
   */
  async disconnect(): Promise<void> {
    if (this.connection) {
      await this.connection.stop();
      this.connection = null;
    }

    this.subscriptions.clear();
  }

  private getHeaders(): Record<string, string> {
    const headers: Record<string, string> = {};

    if (this.options.projectId) {
      headers['X-Project-Id'] = this.options.projectId;
    }

    return headers;
  }

  private parseOperation(operation: string): ChangeOperation {
    switch (operation.toLowerCase()) {
      case 'insert':
        return 'insert';
      case 'update':
        return 'update';
      case 'delete':
        return 'delete';
      default:
        return 'insert';
    }
  }
}

interface SubscriptionHandler {
  subscriptionId: string;
  tableName: string;
  callback: (change: ChangeNotification) => void;
  isActive: boolean;
}
