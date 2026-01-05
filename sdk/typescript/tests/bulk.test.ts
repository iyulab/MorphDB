import { describe, it, expect, vi, beforeEach, type Mock } from 'vitest';
import { BulkClient } from '../src/bulk.js';
import {
  createMockHttpClient,
  createSampleImportJobStatus,
  createSampleExportJobStatus,
} from './test-utils.js';
import type { HttpClient } from '../src/http.js';

describe('BulkClient', () => {
  let bulkClient: BulkClient;
  let mockHttp: HttpClient;

  beforeEach(() => {
    mockHttp = createMockHttpClient();
    bulkClient = new BulkClient(mockHttp);
  });

  describe('Import Operations', () => {
    describe('importCsv', () => {
      it('imports CSV data', async () => {
        const job = createSampleImportJobStatus();
        (mockHttp.postFormData as Mock).mockResolvedValue(job);

        const file = new Blob(['name,email\nJohn,john@example.com'], {
          type: 'text/csv',
        });

        const result = await bulkClient.importCsv('users', file);

        expect(result).toEqual(job);
        expect(mockHttp.postFormData).toHaveBeenCalled();
        const [path, formData] = (mockHttp.postFormData as Mock).mock.calls[0];
        expect(path).toBe('/api/bulk/users/import/csv');
        expect(formData).toBeInstanceOf(FormData);
      });

      it('imports CSV with options', async () => {
        const job = createSampleImportJobStatus();
        (mockHttp.postFormData as Mock).mockResolvedValue(job);

        const file = new Blob(['data']);

        await bulkClient.importCsv('users', file, {
          delimiter: ',',
          hasHeader: true,
          dateFormat: 'yyyy-MM-dd',
          trimWhitespace: true,
          nullHandling: 'empty-as-null',
          duplicateHandling: 'update',
          keyColumns: ['id', 'email'],
        });

        expect(mockHttp.postFormData).toHaveBeenCalled();
      });

      it('encodes table name in URL', async () => {
        (mockHttp.postFormData as Mock).mockResolvedValue(createSampleImportJobStatus());

        await bulkClient.importCsv('my table', new Blob(['data']));

        const [path] = (mockHttp.postFormData as Mock).mock.calls[0];
        expect(path).toBe('/api/bulk/my%20table/import/csv');
      });
    });

    describe('importJson', () => {
      it('imports JSON data', async () => {
        const job = { ...createSampleImportJobStatus(), format: 'json' };
        (mockHttp.postFormData as Mock).mockResolvedValue(job);

        const file = new Blob([JSON.stringify([{ name: 'John' }])], {
          type: 'application/json',
        });

        const result = await bulkClient.importJson('users', file);

        expect(result).toEqual(job);
        const [path] = (mockHttp.postFormData as Mock).mock.calls[0];
        expect(path).toBe('/api/bulk/users/import/json');
      });

      it('imports JSON with options', async () => {
        (mockHttp.postFormData as Mock).mockResolvedValue(createSampleImportJobStatus());

        await bulkClient.importJson('users', new Blob(['data']), {
          jsonPath: '$.data',
          dateFormat: 'yyyy-MM-dd',
          duplicateHandling: 'skip',
          keyColumns: ['id'],
        });

        expect(mockHttp.postFormData).toHaveBeenCalled();
      });
    });

    describe('importNdjson', () => {
      it('imports NDJSON data', async () => {
        const job = { ...createSampleImportJobStatus(), format: 'ndjson' };
        (mockHttp.postFormData as Mock).mockResolvedValue(job);

        const file = new Blob(['{"name":"John"}\n{"name":"Jane"}']);

        const result = await bulkClient.importNdjson('users', file);

        expect(result).toEqual(job);
        const [path] = (mockHttp.postFormData as Mock).mock.calls[0];
        expect(path).toBe('/api/bulk/users/import/ndjson');
      });
    });

    describe('getImportJobStatus', () => {
      it('gets import job status', async () => {
        const job = createSampleImportJobStatus();
        (mockHttp.get as Mock).mockResolvedValue(job);

        const result = await bulkClient.getImportJobStatus('import-123');

        expect(result).toEqual(job);
        expect(mockHttp.get).toHaveBeenCalledWith('/api/bulk/import/import-123');
      });

      it('returns null when job not found', async () => {
        const error = new Error('Not found');
        (error as Error & { statusCode: number }).statusCode = 404;
        (mockHttp.get as Mock).mockRejectedValue(error);

        const result = await bulkClient.getImportJobStatus('nonexistent');

        expect(result).toBeNull();
      });
    });

    describe('cancelImportJob', () => {
      it('cancels an import job', async () => {
        (mockHttp.post as Mock).mockResolvedValue(undefined);

        await bulkClient.cancelImportJob('import-123');

        expect(mockHttp.post).toHaveBeenCalledWith('/api/bulk/import/import-123/cancel');
      });
    });
  });

  describe('Export Operations', () => {
    describe('exportCsv', () => {
      it('exports data to CSV', async () => {
        const job = createSampleExportJobStatus();
        (mockHttp.post as Mock).mockResolvedValue(job);

        const result = await bulkClient.exportCsv('users');

        expect(result).toEqual(job);
        expect(mockHttp.post).toHaveBeenCalledWith('/api/bulk/users/export/csv', {});
      });

      it('exports CSV with options', async () => {
        const job = createSampleExportJobStatus();
        (mockHttp.post as Mock).mockResolvedValue(job);

        const options = {
          columns: ['name', 'email'],
          filter: 'status eq "active"',
          orderBy: 'name asc',
          delimiter: ',',
          includeHeader: true,
          dateFormat: 'yyyy-MM-dd',
        };

        await bulkClient.exportCsv('users', options);

        expect(mockHttp.post).toHaveBeenCalledWith('/api/bulk/users/export/csv', options);
      });
    });

    describe('exportJson', () => {
      it('exports data to JSON', async () => {
        const job = { ...createSampleExportJobStatus(), format: 'json' };
        (mockHttp.post as Mock).mockResolvedValue(job);

        const result = await bulkClient.exportJson('users');

        expect(result).toEqual(job);
        expect(mockHttp.post).toHaveBeenCalledWith('/api/bulk/users/export/json', {});
      });

      it('exports JSON with pretty option', async () => {
        (mockHttp.post as Mock).mockResolvedValue(createSampleExportJobStatus());

        await bulkClient.exportJson('users', { pretty: true });

        expect(mockHttp.post).toHaveBeenCalledWith('/api/bulk/users/export/json', {
          pretty: true,
        });
      });
    });

    describe('exportXlsx', () => {
      it('exports data to XLSX', async () => {
        const job = { ...createSampleExportJobStatus(), format: 'xlsx' };
        (mockHttp.post as Mock).mockResolvedValue(job);

        const result = await bulkClient.exportXlsx('users');

        expect(result).toEqual(job);
        expect(mockHttp.post).toHaveBeenCalledWith('/api/bulk/users/export/xlsx', {});
      });

      it('exports XLSX with sheet name', async () => {
        (mockHttp.post as Mock).mockResolvedValue(createSampleExportJobStatus());

        await bulkClient.exportXlsx('users', { sheetName: 'Users Data' });

        expect(mockHttp.post).toHaveBeenCalledWith('/api/bulk/users/export/xlsx', {
          sheetName: 'Users Data',
        });
      });
    });

    describe('getExportJobStatus', () => {
      it('gets export job status', async () => {
        const job = createSampleExportJobStatus();
        (mockHttp.get as Mock).mockResolvedValue(job);

        const result = await bulkClient.getExportJobStatus('export-123');

        expect(result).toEqual(job);
        expect(mockHttp.get).toHaveBeenCalledWith('/api/bulk/export/export-123');
      });

      it('returns null when job not found', async () => {
        const error = new Error('Not found');
        (error as Error & { statusCode: number }).statusCode = 404;
        (mockHttp.get as Mock).mockRejectedValue(error);

        const result = await bulkClient.getExportJobStatus('nonexistent');

        expect(result).toBeNull();
      });
    });

    describe('downloadExport', () => {
      it('downloads export file', async () => {
        const blob = new Blob(['csv,data']);
        (mockHttp.getBlob as Mock).mockResolvedValue(blob);

        const result = await bulkClient.downloadExport('export-123');

        expect(result).toEqual(blob);
        expect(mockHttp.getBlob).toHaveBeenCalledWith(
          '/api/bulk/export/export-123/download'
        );
      });
    });

    describe('cancelExportJob', () => {
      it('cancels an export job', async () => {
        (mockHttp.post as Mock).mockResolvedValue(undefined);

        await bulkClient.cancelExportJob('export-123');

        expect(mockHttp.post).toHaveBeenCalledWith('/api/bulk/export/export-123/cancel');
      });
    });
  });
});
