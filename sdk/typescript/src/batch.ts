import { HttpClient } from './http.js';
import type { BatchRequest, BatchResponse } from './types.js';

/**
 * Client for batch data operations — many writes in one request
 */
export class BatchClient {
  constructor(private readonly http: HttpClient) {}

  /**
   * Executes a batch of operations in order. Each operation names its own table, so one batch may
   * span tables. Operations are reported individually — inspect `results` for partial failures,
   * since a batch containing failed operations still succeeds as a request.
   */
  async execute(request: BatchRequest): Promise<BatchResponse> {
    return this.http.post<BatchResponse>('/api/batch/data', request);
  }

  /**
   * Inserts many records into one table. Records without an `_id` are assigned one by the server.
   */
  async insertMany(
    tableName: string,
    records: Record<string, unknown>[]
  ): Promise<BatchResponse> {
    return this.http.post<BatchResponse>(
      `/api/batch/data/${encodeURIComponent(tableName)}/insert`,
      records
    );
  }
}
