import { HttpClient } from './http.js';
import type {
  CreateTableRequest,
  AddColumnRequest,
  AlterColumnRequest,
  TableInfo,
  ColumnInfo,
} from './types.js';

/**
 * Client for schema management operations
 */
export class SchemaClient {
  constructor(private readonly http: HttpClient) {}

  /**
   * Gets all tables
   */
  async getTables(): Promise<TableInfo[]> {
    return this.http.get<TableInfo[]>('/api/schema/tables');
  }

  /**
   * Gets a table by name
   */
  async getTable(tableName: string): Promise<TableInfo | null> {
    try {
      return await this.http.get<TableInfo>(`/api/schema/tables/${encodeURIComponent(tableName)}`);
    } catch (error: unknown) {
      if (error instanceof Error && 'statusCode' in error && (error as { statusCode: number }).statusCode === 404) {
        return null;
      }
      throw error;
    }
  }

  /**
   * Creates a new table
   */
  async createTable(request: CreateTableRequest): Promise<TableInfo> {
    return this.http.post<TableInfo>('/api/schema/tables', request);
  }

  /**
   * Drops a table
   */
  async dropTable(tableName: string): Promise<void> {
    await this.http.delete(`/api/schema/tables/${encodeURIComponent(tableName)}`);
  }

  /**
   * Adds a column to an existing table
   */
  async addColumn(tableName: string, request: AddColumnRequest): Promise<ColumnInfo> {
    return this.http.post<ColumnInfo>(
      `/api/schema/tables/${encodeURIComponent(tableName)}/columns`,
      request
    );
  }

  /**
   * Alters a column
   */
  async alterColumn(
    tableName: string,
    columnName: string,
    request: AlterColumnRequest
  ): Promise<ColumnInfo> {
    return this.http.patch<ColumnInfo>(
      `/api/schema/tables/${encodeURIComponent(tableName)}/columns/${encodeURIComponent(columnName)}`,
      request
    );
  }

  /**
   * Drops a column from a table
   */
  async dropColumn(tableName: string, columnName: string): Promise<void> {
    await this.http.delete(
      `/api/schema/tables/${encodeURIComponent(tableName)}/columns/${encodeURIComponent(columnName)}`
    );
  }
}
