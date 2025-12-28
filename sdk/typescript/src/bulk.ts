import { HttpClient } from './http.js';
import type {
  CsvImportOptions,
  JsonImportOptions,
  CsvExportOptions,
  JsonExportOptions,
  XlsxExportOptions,
  ImportJobStatus,
  ExportJobStatus,
} from './types.js';

/**
 * Client for bulk import/export operations
 */
export class BulkClient {
  constructor(private readonly http: HttpClient) {}

  // Import Operations

  /**
   * Imports data from a CSV file
   */
  async importCsv(
    tableName: string,
    file: Blob | File,
    options?: CsvImportOptions
  ): Promise<ImportJobStatus> {
    const formData = new FormData();
    formData.append('file', file, 'import.csv');

    if (options?.delimiter) formData.append('delimiter', options.delimiter);
    if (options?.hasHeader !== undefined) formData.append('hasHeader', String(options.hasHeader));
    if (options?.dateFormat) formData.append('dateFormat', options.dateFormat);
    if (options?.trimWhitespace !== undefined)
      formData.append('trimWhitespace', String(options.trimWhitespace));
    if (options?.nullHandling) formData.append('nullHandling', options.nullHandling);
    if (options?.duplicateHandling) formData.append('duplicateHandling', options.duplicateHandling);
    if (options?.keyColumns) formData.append('keyColumns', options.keyColumns.join(','));

    return this.http.postFormData<ImportJobStatus>(
      `/api/bulk/${encodeURIComponent(tableName)}/import/csv`,
      formData
    );
  }

  /**
   * Imports data from a JSON file
   */
  async importJson(
    tableName: string,
    file: Blob | File,
    options?: JsonImportOptions
  ): Promise<ImportJobStatus> {
    const formData = new FormData();
    formData.append('file', file, 'import.json');

    if (options?.jsonPath) formData.append('jsonPath', options.jsonPath);
    if (options?.dateFormat) formData.append('dateFormat', options.dateFormat);
    if (options?.duplicateHandling) formData.append('duplicateHandling', options.duplicateHandling);
    if (options?.keyColumns) formData.append('keyColumns', options.keyColumns.join(','));

    return this.http.postFormData<ImportJobStatus>(
      `/api/bulk/${encodeURIComponent(tableName)}/import/json`,
      formData
    );
  }

  /**
   * Imports data from an NDJSON file
   */
  async importNdjson(
    tableName: string,
    file: Blob | File,
    options?: JsonImportOptions
  ): Promise<ImportJobStatus> {
    const formData = new FormData();
    formData.append('file', file, 'import.ndjson');

    if (options?.dateFormat) formData.append('dateFormat', options.dateFormat);
    if (options?.duplicateHandling) formData.append('duplicateHandling', options.duplicateHandling);
    if (options?.keyColumns) formData.append('keyColumns', options.keyColumns.join(','));

    return this.http.postFormData<ImportJobStatus>(
      `/api/bulk/${encodeURIComponent(tableName)}/import/ndjson`,
      formData
    );
  }

  /**
   * Gets import job status
   */
  async getImportJobStatus(jobId: string): Promise<ImportJobStatus | null> {
    try {
      return await this.http.get<ImportJobStatus>(`/api/bulk/import/${jobId}`);
    } catch (error: unknown) {
      if (error instanceof Error && 'statusCode' in error && (error as { statusCode: number }).statusCode === 404) {
        return null;
      }
      throw error;
    }
  }

  /**
   * Cancels an import job
   */
  async cancelImportJob(jobId: string): Promise<void> {
    await this.http.post(`/api/bulk/import/${jobId}/cancel`);
  }

  // Export Operations

  /**
   * Exports data to CSV format
   */
  async exportCsv(
    tableName: string,
    options?: CsvExportOptions
  ): Promise<ExportJobStatus> {
    return this.http.post<ExportJobStatus>(
      `/api/bulk/${encodeURIComponent(tableName)}/export/csv`,
      options ?? {}
    );
  }

  /**
   * Exports data to JSON format
   */
  async exportJson(
    tableName: string,
    options?: JsonExportOptions
  ): Promise<ExportJobStatus> {
    return this.http.post<ExportJobStatus>(
      `/api/bulk/${encodeURIComponent(tableName)}/export/json`,
      options ?? {}
    );
  }

  /**
   * Exports data to XLSX format
   */
  async exportXlsx(
    tableName: string,
    options?: XlsxExportOptions
  ): Promise<ExportJobStatus> {
    return this.http.post<ExportJobStatus>(
      `/api/bulk/${encodeURIComponent(tableName)}/export/xlsx`,
      options ?? {}
    );
  }

  /**
   * Gets export job status
   */
  async getExportJobStatus(jobId: string): Promise<ExportJobStatus | null> {
    try {
      return await this.http.get<ExportJobStatus>(`/api/bulk/export/${jobId}`);
    } catch (error: unknown) {
      if (error instanceof Error && 'statusCode' in error && (error as { statusCode: number }).statusCode === 404) {
        return null;
      }
      throw error;
    }
  }

  /**
   * Downloads an exported file
   */
  async downloadExport(jobId: string): Promise<Blob> {
    return this.http.getBlob(`/api/bulk/export/${jobId}/download`);
  }

  /**
   * Cancels an export job
   */
  async cancelExportJob(jobId: string): Promise<void> {
    await this.http.post(`/api/bulk/export/${jobId}/cancel`);
  }
}
