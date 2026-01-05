# API Reference

## Authentication

All requests require API Key authentication:

```http
X-API-Key: your-api-key
```

## REST API

### Schema Management (DDL)

```yaml
# Tables
POST   /api/schema/tables                      # Create table
GET    /api/schema/tables                      # List tables
GET    /api/schema/tables/{name}               # Get table details
PATCH  /api/schema/tables/{name}               # Update table
DELETE /api/schema/tables/{name}               # Delete table

# Columns
POST   /api/schema/tables/{name}/columns       # Add column
PATCH  /api/schema/tables/{name}/columns/{col} # Update column
DELETE /api/schema/tables/{name}/columns/{col} # Delete column

# Relations & Indexes
POST   /api/schema/relations                   # Create relation
POST   /api/schema/indexes                     # Create index
POST   /api/schema/batch                       # Batch DDL operations
```

### Data Operations (DML)

```yaml
# CRUD - Auto-generated per table
GET    /api/data/{table}                       # List records
GET    /api/data/{table}/{id}                  # Get single record
POST   /api/data/{table}                       # Create record
PATCH  /api/data/{table}/{id}                  # Update record
DELETE /api/data/{table}/{id}                  # Delete record

# Advanced
POST   /api/data/{table}/query                 # Complex query
POST   /api/data/{table}/batch                 # Batch operations
```

### Query Parameters

```http
GET /api/data/customers?filter=grade:VIP&sort=-createdAt&page=1&pageSize=20
```

| Parameter | Description | Example |
|-----------|-------------|---------|
| `filter` | Field filtering | `grade:VIP`, `status:active` |
| `sort` | Sort order (- for desc) | `name`, `-createdAt` |
| `page` | Page number | `1` |
| `pageSize` | Records per page | `20` |

### Batch Operations

```http
POST /api/batch
Content-Type: application/json

{
  "operations": [
    { "method": "POST", "path": "/api/data/customers", "body": {...} },
    { "method": "PATCH", "path": "/api/data/customers/123", "body": {...} }
  ]
}
```

---

## GraphQL

**Endpoint**: `/graphql`

Tables automatically generate GraphQL types:

```graphql
# Auto-generated from table definition
type Customer {
  id: ID!
  name: String!
  email: String
  createdAt: DateTime!
  orders: [Order!]!  # Relations auto-resolved
}

# Queries
query {
  customer(id: "123") { name, email }
  customers(filter: { grade: "VIP" }, first: 10) {
    nodes { id, name }
    pageInfo { hasNextPage }
  }
}

# Mutations
mutation {
  createCustomer(input: { name: "John", email: "john@example.com" }) {
    id
  }
}

# Subscriptions
subscription {
  customerChanged { operation, data { id, name } }
}
```

---

## OData

**Endpoint**: `/odata`

Standard OData v4 protocol for enterprise tool integration (Excel, Power BI).

```http
# Metadata
GET /odata/$metadata

# Queries
GET /odata/Customers?$filter=grade eq 'VIP'&$orderby=createdAt desc&$top=10
GET /odata/Customers?$expand=orders&$select=name,email
GET /odata/Customers/$count

# CRUD
POST   /odata/Customers
PATCH  /odata/Customers('id')
DELETE /odata/Customers('id')
```

**Supported operators**: `$filter`, `$orderby`, `$top`, `$skip`, `$select`, `$expand`, `$count`

---

## WebSocket (Real-time)

**Endpoint**: `/hubs/morph` (SignalR)

```javascript
const connection = new signalR.HubConnectionBuilder()
  .withUrl("/hubs/morph", { accessTokenFactory: () => apiKey })
  .build();

// Subscribe to table changes
await connection.invoke("Subscribe", "customers", { grade: "VIP" });

// Receive events
connection.on("DataChanged", (event) => {
  // event: { table, operation: "insert"|"update"|"delete", data, previous }
});
```

---

## Webhook

Register webhooks for external system integration:

```http
POST /api/webhooks
Content-Type: application/json

{
  "name": "Order notification",
  "table": "orders",
  "events": ["insert", "update"],
  "url": "https://external.system/callback",
  "headers": { "Authorization": "Bearer xxx" },
  "filter": { "status": "completed" },
  "secret": "webhook-signing-secret"
}
```

Webhook payload:
```json
{
  "event": "insert",
  "table": "orders",
  "data": { "id": "123", "status": "completed" },
  "timestamp": "2025-01-01T00:00:00Z"
}
```

---

## Write Options

Control virtual constraint validation and automatic field management during write operations.

### Usage

Pass `options` in the request body for data operations:

```http
POST /api/data/{table}
Content-Type: application/json

{
  "data": { "name": "John", "email": "john@example.com" },
  "options": {
    "validateRequired": true,
    "validateForeignKeys": true,
    "validateUnique": true,
    "validateCheck": true,
    "applyDefaults": true,
    "applyTimestamps": true,
    "applyVersion": true,
    "expectedVersion": 1
  }
}
```

### Validation Options

| Option | Default | Description |
|--------|---------|-------------|
| `validateRequired` | `true` | Validate required fields (virtual NOT NULL) |
| `validateForeignKeys` | `true` | Validate foreign key references exist |
| `validateUnique` | `true` | Validate unique constraints |
| `validateCheck` | `true` | Validate CHECK constraints (supports AND/OR expressions) |

### Auto-Apply Options

| Option | Default | Description |
|--------|---------|-------------|
| `applyDefaults` | `true` | Apply default values for missing fields |
| `applyTimestamps` | `true` | Auto-manage `_created_at` and `_updated_at` |
| `applyVersion` | `true` | Auto-manage `_version` for optimistic locking |
| `applyAuditFields` | `true` | Auto-manage `_created_by` and `_updated_by` |
| `applyOwnership` | `true` | Auto-manage `_owner_id` for ownership tables |
| `applySortOrder` | `true` | Auto-manage `_sort_order` for hierarchy tables |

### Advanced Options

| Option | Default | Description |
|--------|---------|-------------|
| `deferValidation` | `false` | Defer validation until after bulk insert |
| `expectedVersion` | `null` | Expected version for optimistic locking |

### Preset Configurations

**Default** (all enabled):
```json
{ "validateRequired": true, "validateForeignKeys": true, "validateUnique": true, "validateCheck": true, "applyDefaults": true, "applyTimestamps": true, "applyVersion": true }
```

**Bulk Import** (deferred validation):
```json
{ "validateRequired": true, "validateForeignKeys": false, "validateUnique": false, "validateCheck": false, "applyDefaults": true, "applyTimestamps": true, "applyVersion": false, "deferValidation": true }
```

**No Validation** (use with caution):
```json
{ "validateRequired": false, "validateForeignKeys": false, "validateUnique": false, "validateCheck": false, "applyDefaults": false, "applyTimestamps": false, "applyVersion": false }
```

### Dry Run (Validate Only)

Validate data without writing:

```http
POST /api/data/{table}/validate
Content-Type: application/json

{
  "data": { "name": "John", "email": "invalid-email" },
  "options": { "validateRequired": true }
}
```

Response:
```json
{
  "success": false,
  "errors": [
    { "field": "email", "code": "INVALID_FORMAT", "message": "Invalid email format" }
  ]
}
```

---

## Bulk Operations

```http
# CSV Import
POST /api/bulk/{table}/import
Content-Type: text/csv

name,email,grade
John Doe,john@example.com,VIP

# Export
GET /api/bulk/{table}/export?format=csv
GET /api/bulk/{table}/export?format=json
GET /api/bulk/{table}/export?format=xlsx
```

---

## Health Checks

```http
GET /health        # Overall health
GET /health/live   # Liveness probe
GET /health/ready  # Readiness probe
```
