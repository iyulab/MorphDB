import { HttpClient } from './http.js';
import type {
  QueryRequest,
  PagedResponse,
  DataRecord,
  BatchRequest,
  BatchResponse,
} from './types.js';

/**
 * Client for data operations
 */
export class DataClient {
  constructor(private readonly http: HttpClient) {}

  /**
   * Queries records from a table
   */
  async query(tableName: string, request?: QueryRequest): Promise<PagedResponse<DataRecord>> {
    const queryString = this.buildQueryString(request);
    return this.http.get<PagedResponse<DataRecord>>(
      `/api/data/${encodeURIComponent(tableName)}${queryString}`
    );
  }

  /**
   * Gets a single record by ID
   */
  async getById(tableName: string, id: string): Promise<DataRecord | null> {
    try {
      return await this.http.get<DataRecord>(
        `/api/data/${encodeURIComponent(tableName)}/${id}`
      );
    } catch (error: unknown) {
      if (error instanceof Error && 'statusCode' in error && (error as { statusCode: number }).statusCode === 404) {
        return null;
      }
      throw error;
    }
  }

  /**
   * Inserts a new record
   */
  async insert(tableName: string, data: Record<string, unknown>): Promise<DataRecord> {
    return this.http.post<DataRecord>(
      `/api/data/${encodeURIComponent(tableName)}`,
      data
    );
  }

  /**
   * Updates an existing record
   */
  async update(
    tableName: string,
    id: string,
    data: Record<string, unknown>
  ): Promise<DataRecord> {
    return this.http.patch<DataRecord>(
      `/api/data/${encodeURIComponent(tableName)}/${id}`,
      data
    );
  }

  /**
   * Deletes a record
   */
  async delete(tableName: string, id: string): Promise<void> {
    await this.http.delete(`/api/data/${encodeURIComponent(tableName)}/${id}`);
  }

  /**
   * Performs batch operations
   */
  async batch(tableName: string, request: BatchRequest): Promise<BatchResponse> {
    return this.http.post<BatchResponse>(
      `/api/data/${encodeURIComponent(tableName)}/batch`,
      request
    );
  }

  /**
   * Upserts a record (insert or update based on ID)
   */
  async upsert(tableName: string, data: Record<string, unknown>): Promise<DataRecord> {
    return this.http.put<DataRecord>(
      `/api/data/${encodeURIComponent(tableName)}`,
      data
    );
  }

  private buildQueryString(request?: QueryRequest): string {
    if (!request) return '';

    const parts: string[] = [];

    if (request.select && request.select.length > 0) {
      parts.push(`select=${encodeURIComponent(request.select.join(','))}`);
    }

    if (request.filters && request.filters.length > 0) {
      for (const filter of request.filters) {
        const value = filter.value?.toString() ?? '';
        parts.push(`filter=${encodeURIComponent(`${filter.column}:${filter.operator}:${value}`)}`);
      }
    }

    if (request.orderBy && request.orderBy.length > 0) {
      const orderParts = request.orderBy.map(
        (o) => `${o.column}:${o.ascending !== false ? 'asc' : 'desc'}`
      );
      parts.push(`orderBy=${encodeURIComponent(orderParts.join(','))}`);
    }

    if (request.page && request.page > 1) {
      parts.push(`page=${request.page}`);
    }

    if (request.pageSize && request.pageSize !== 50) {
      parts.push(`pageSize=${request.pageSize}`);
    }

    return parts.length > 0 ? `?${parts.join('&')}` : '';
  }
}
