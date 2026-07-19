import { vi } from 'vitest';
import type { HttpClient } from '../src/http.js';
import type {
  TableInfo,
  ColumnInfo,
  DataRecord,
  PagedResponse,
  WebhookInfo,
  WebhookDelivery,
  ImportJobStatus,
  ExportJobStatus,
  BatchResponse,
} from '../src/types.js';

/**
 * Creates a mock HttpClient
 */
export function createMockHttpClient(): HttpClient {
  return {
    get: vi.fn(),
    post: vi.fn(),
    put: vi.fn(),
    patch: vi.fn(),
    delete: vi.fn(),
    postFormData: vi.fn(),
    getBlob: vi.fn(),
    setTenantId: vi.fn(),
    setApiKey: vi.fn(),
    setJwtToken: vi.fn(),
  } as unknown as HttpClient;
}

/**
 * Sample table info for testing
 */
export function createSampleTableInfo(): TableInfo {
  return {
    tableId: 'table-123',
    name: 'users',
    physicalName: 'tbl_a1b2c3d4',
    schemaVersion: 1,
    columns: [
      createSampleColumnInfo('_id', 'uuid', true),
      createSampleColumnInfo('name', 'text'),
      createSampleColumnInfo('email', 'text'),
    ],
    createdAt: new Date().toISOString(),
    updatedAt: new Date().toISOString(),
  };
}

/**
 * Sample column info for testing
 */
export function createSampleColumnInfo(
  name: string,
  dataType: string,
  primaryKey = false
): ColumnInfo {
  return {
    columnId: `col-${name}`,
    name,
    physicalName: `col_${name.slice(0, 8)}`,
    dataType,
    nativeType: dataType,
    nullable: !primaryKey,
    unique: primaryKey,
    primaryKey,
    indexed: primaryKey,
    ordinalPosition: 1,
  };
}

/**
 * Sample data record for testing
 */
export function createSampleDataRecord(data: Record<string, unknown> = {}): DataRecord {
  return {
    id: 'record-123',
    data: { name: 'John Doe', email: 'john@example.com', ...data },
    createdAt: new Date().toISOString(),
    updatedAt: new Date().toISOString(),
  };
}

/**
 * Sample paged response for testing
 */
export function createSamplePagedResponse<T>(
  data: T[],
  page = 1,
  pageSize = 50,
  totalCount = 1
): PagedResponse<T> {
  return {
    data,
    pagination: {
      page,
      pageSize,
      totalCount,
      totalPages: Math.ceil(totalCount / pageSize),
      hasNextPage: page * pageSize < totalCount,
      hasPreviousPage: page > 1,
    },
  };
}

/**
 * Sample webhook info for testing
 */
export function createSampleWebhookInfo(): WebhookInfo {
  return {
    webhookId: 'webhook-123',
    name: 'order-notifications',
    tableName: 'orders',
    url: 'https://example.com/webhook',
    events: ['insert', 'update', 'delete'],
    isActive: true,
    createdAt: new Date().toISOString(),
    updatedAt: new Date().toISOString(),
  };
}

/**
 * Sample webhook delivery for testing
 */
export function createSampleWebhookDelivery(): WebhookDelivery {
  return {
    deliveryId: 'delivery-123',
    webhookId: 'webhook-123',
    recordId: 'record-123',
    event: 'insert',
    status: 'delivered',
    attemptCount: 1,
    httpStatusCode: 200,
    createdAt: new Date().toISOString(),
    deliveredAt: new Date().toISOString(),
  };
}

/**
 * Sample import job status for testing
 */
export function createSampleImportJobStatus(): ImportJobStatus {
  return {
    jobId: 'import-123',
    tableName: 'users',
    format: 'csv',
    status: 'processing',
    totalRows: 100,
    processedRows: 50,
    successCount: 50,
    errorCount: 0,
    createdAt: new Date().toISOString(),
    startedAt: new Date().toISOString(),
    percentComplete: 50,
  };
}

/**
 * Sample export job status for testing
 */
export function createSampleExportJobStatus(): ExportJobStatus {
  return {
    jobId: 'export-123',
    tableName: 'users',
    format: 'csv',
    status: 'completed',
    totalRows: 100,
    processedRows: 100,
    downloadUrl: 'https://example.com/download/export-123',
    fileSize: 1024,
    createdAt: new Date().toISOString(),
    startedAt: new Date().toISOString(),
    completedAt: new Date().toISOString(),
    expiresAt: new Date(Date.now() + 3600000).toISOString(),
    percentComplete: 100,
  };
}

/**
 * Sample batch response for testing
 */
export function createSampleBatchResponse(): BatchResponse {
  return {
    results: [
      { index: 0, success: true, data: { _id: 'record-123' }, affectedRows: 1 },
      { index: 1, success: true, data: { _id: 'record-456' }, affectedRows: 1 },
    ],
    successCount: 2,
    failureCount: 0,
  };
}
