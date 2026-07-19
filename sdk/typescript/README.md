# @morphdb/client

Official TypeScript client SDK for MorphDB - a PostgreSQL-based dynamic schema database service.

## Installation

**This SDK is not published.** It is a reference implementation that lives in the MorphDB
repository; `@morphdb/client` is not registered on npm. Install it from a checkout:

```bash
npm install ./sdk/typescript    # from a clone of iyulab/MorphDB
```

Only the .NET client (`MorphDB.Client` on NuGet) is published and exercised against a live server.

## Quick Start

```typescript
import { MorphDBClient } from '@morphdb/client';

// Create client
const client = new MorphDBClient('http://localhost:5000', {
  tenantId: 'your-tenant-id',
  apiKey: 'your-api-key',
});

// Schema Management
await client.schema.createTable({
  name: 'users',
  columns: [
    { name: 'name', type: 'text' },
    { name: 'email', type: 'text', unique: true },
    { name: 'age', type: 'integer' },
  ],
});

// Data Operations
const user = await client.data.insert('users', {
  name: 'John Doe',
  email: 'john@example.com',
  age: 30,
});

// Query with filters
const adults = await client.data.query('users', {
  filters: [{ column: 'age', operator: 'gte', value: 18 }],
  orderBy: [{ column: 'name', ascending: true }],
  pageSize: 10,
});

// Real-time subscriptions
const subscription = await client.realtime.subscribe('users', (change) => {
  console.log(`Change: ${change.operation} on ${change.tableName}`);
});

// Unsubscribe when done
await subscription.unsubscribe();
```

## Features

- **Schema Management**: Create, alter, and drop tables dynamically
- **Data Operations**: CRUD operations with filtering, pagination, and ordering
- **Real-time Subscriptions**: WebSocket-based change notifications
- **Bulk Operations**: Import/export data in CSV, JSON, and XLSX formats
- **Webhooks**: Manage webhook subscriptions for event notifications
- **Type-safe API**: Full TypeScript support with comprehensive types

## API Reference

### Schema Client

```typescript
// Get all tables
const tables = await client.schema.getTables();

// Get a table by name
const table = await client.schema.getTable('users');

// Create a table
const newTable = await client.schema.createTable({
  name: 'products',
  columns: [
    { name: 'name', type: 'text', nullable: false },
    { name: 'price', type: 'decimal' },
  ],
});

// Add a column
await client.schema.addColumn('products', {
  name: 'description',
  type: 'text',
});

// Alter a column
await client.schema.alterColumn('products', 'description', {
  nullable: false,
});

// Drop a column
await client.schema.dropColumn('products', 'description');

// Drop a table
await client.schema.dropTable('products');
```

### Data Client

```typescript
// Query records
const result = await client.data.query('users', {
  select: ['name', 'email'],
  filters: [
    { column: 'age', operator: 'gte', value: 18 },
    { column: 'is_active', operator: 'eq', value: true },
  ],
  orderBy: [{ column: 'created_at', ascending: false }],
  page: 1,
  pageSize: 20,
});

// Get by ID
const user = await client.data.getById('users', 'uuid-here');

// Insert
const newUser = await client.data.insert('users', {
  name: 'Jane Doe',
  email: 'jane@example.com',
});

// Update
const updated = await client.data.update('users', 'uuid-here', {
  name: 'Jane Smith',
});

// Delete
await client.data.delete('users', 'uuid-here');

```

### Batch Client

```typescript
// Insert many records into one table
const inserted = await client.batch.insertMany('users', [
  { name: 'User 1' },
  { name: 'User 2' },
]);
console.log(inserted.successCount);

// Mixed operations, in order, across tables
const result = await client.batch.execute({
  operations: [
    { method: 'INSERT', table: 'users', data: { name: 'User 3' } },
    { method: 'UPDATE', table: 'users', id: 'uuid-1', data: { name: 'Updated User' } },
    { method: 'DELETE', table: 'orders', id: 'uuid-to-delete' },
  ],
});

// A batch with failed operations still succeeds as a request — check the per-operation results
for (const failure of result.results.filter((r) => !r.success)) {
  console.error(failure.index, failure.error);
}
```

### Bulk Client

```typescript
// Import CSV
const importJob = await client.bulk.importCsv('users', csvFile, {
  delimiter: ',',
  hasHeader: true,
});

// Check import status
const status = await client.bulk.getImportJobStatus(importJob.jobId);

// Export to CSV
const exportJob = await client.bulk.exportCsv('users', {
  columns: ['name', 'email'],
  filter: 'is_active:eq:true',
});

// Download export
const blob = await client.bulk.downloadExport(exportJob.jobId);
```

### Webhook Client

```typescript
// Create webhook
const webhook = await client.webhooks.create({
  name: 'user-changes',
  tableName: 'users',
  url: 'https://your-server.com/webhook',
  events: ['insert', 'update', 'delete'],
});

// Get deliveries
const deliveries = await client.webhooks.getDeliveries(webhook.webhookId);

// Retry failed delivery
await client.webhooks.retryDelivery(deliveryId);
```

### Realtime Client

```typescript
// Subscribe to table changes
const subscription = await client.realtime.subscribe(
  'users',
  (change) => {
    console.log(`${change.operation}: ${change.recordId}`);
  },
  { filter: 'is_active:eq:true' }
);

// Unsubscribe
await subscription.unsubscribe();

// Disconnect all subscriptions
await client.disconnect();
```

## Error Handling

```typescript
import {
  MorphDBError,
  MorphDBNotFoundError,
  MorphDBValidationError,
  MorphDBAuthenticationError,
} from '@morphdb/client';

try {
  await client.data.getById('users', 'non-existent');
} catch (error) {
  if (error instanceof MorphDBNotFoundError) {
    console.log('User not found');
  } else if (error instanceof MorphDBValidationError) {
    console.log('Validation errors:', error.errors);
  } else if (error instanceof MorphDBAuthenticationError) {
    console.log('Authentication required');
  }
}
```

## Configuration

```typescript
const client = new MorphDBClient('http://localhost:5000', {
  tenantId: 'your-tenant-id',
  apiKey: 'your-api-key',
  jwtToken: 'optional-jwt-token',
  timeout: 30000, // 30 seconds
  retryCount: 3,
  retryDelay: 1000, // 1 second
});

// Update credentials at runtime
client.setTenantId('new-tenant-id');
client.setApiKey('new-api-key');
client.setJwtToken('new-jwt-token');
```

## License

Apache License 2.0
