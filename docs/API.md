# API Reference

## Scoping a request

Every schema and data endpoint applies to one project. Say which:

```http
X-Project-Id: <project id>
```

A request that omits it is answered with `400` and the code `MISSING_PROJECT`.

### What this is not

**A project is a schema namespace, not a trust boundary, and the header is not a credential.** No
endpoint requires authentication — the service has none: a request carrying only `X-Project-Id` is
served. The project exists so MorphDB can operate physical schemas on its own judgement — it is an
internal operating unit, not a multi-tenancy feature.

So: run MorphDB where only your application can reach it, and decide there who may see what. Never
forward a project id supplied by a browser or an end user — whoever picks that value picks which
schemas they read.

## REST API

### Schema Management (DDL)

```yaml
# Tables
POST   /api/schema/tables                      # Create table
GET    /api/schema/tables                      # List tables
GET    /api/schema/tables/{name}               # Get table details
PATCH  /api/schema/tables/{name}               # Update table
DELETE /api/schema/tables/{name}               # Delete table

# Columns — a column is addressed by its id once created, not by table and name
POST   /api/schema/tables/{name}/columns       # Add column
PATCH  /api/schema/columns/{columnId}          # Update column
DELETE /api/schema/columns/{columnId}          # Delete column

# Relations & Indexes
POST   /api/schema/relations                   # Create relation
POST   /api/schema/tables/{name}/indexes       # Create index
POST   /api/schema/batch                       # Batch DDL operations

# Schema Changelog
GET    /api/schema/tables/{name}/history       # Table change history
GET    /api/schema/changelog                   # Global schema changelog
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
```

> Batch writes live under `/api/batch`, not under `/api/data` — see [Batch Operations](#batch-operations).

### Query Parameters

```http
GET /api/data/customers?filter=grade:eq:VIP&orderBy=_created_at:desc&page=1&pageSize=20
```

| Parameter | Description | Example |
|-----------|-------------|---------|
| `filter` | Field filtering (`column:operator:value`) | `grade:eq:VIP`, `status:neq:inactive` |
| `orderBy` | Sort order (`column:asc` or `column:desc`) | `name:asc`, `_created_at:desc` |
| `search` | Full-text search across text columns | `john` |
| `select` | Comma-separated column names | `name,email,grade` |
| `state` | Row state filter (if enabled) | `valid`, `draft`, `error`, `all` |
| `page` | Page number | `1` |
| `pageSize` | Records per page (max 1000) | `20` |

#### Filter Operators

| Operator | Description | Example |
|----------|-------------|---------|
| `eq` | Equals | `status:eq:active` |
| `neq` | Not equals | `status:neq:deleted` |
| `gt` | Greater than | `price:gt:100` |
| `gte` | Greater than or equal | `age:gte:18` |
| `lt` | Less than | `stock:lt:10` |
| `lte` | Less than or equal | `score:lte:50` |
| `like` | Pattern match (case-sensitive, `%` wildcards) | `name:like:Jo%` |
| `ilike` | Pattern match (case-insensitive) | `name:ilike:jo%` |
| `contains` | String contains | `name:contains:john` |
| `startswith` | String starts with | `email:startswith:admin` |
| `endswith` | String ends with | `file:endswith:.pdf` |

An operator outside this list is answered with `400` listing the supported set — it is never
silently coerced. (`in`/`isnull` were documented here once but no server ever accepted them — on
this parameter or anywhere else; the operator vocabulary above is the whole set, on every surface.)

### Complex query — `POST /api/data/{table}/query`

For predicates the flat `filter` parameter cannot express — AND/OR trees — post a filter tree.
A node is either a `condition` or a `group` (discriminated by `$type`); conditions use the same
operator vocabulary as the `filter` parameter above:

```http
POST /api/data/customers/query
Content-Type: application/json
X-Project-Id: <project id>

{
  "filter": {
    "$type": "group",
    "logic": "and",
    "filters": [
      { "$type": "condition", "column": "grade", "operator": "eq", "value": "vip" },
      { "$type": "condition", "column": "amount", "operator": "gte", "value": 50 }
    ]
  },
  "select": ["name", "amount"],
  "orderBy": ["amount:desc"],
  "page": 1,
  "pageSize": 10
}
```

- `filter` — optional; a `condition` (`column`, `operator`, `value`) or a `group` (`logic`:
  `"and"`|`"or"`, `filters`: child nodes).
- `select` — optional column list; omitted selects all.
- `orderBy` — optional `column` or `column:desc` entries.
- `page` / `pageSize` — 1-based; `pageSize` is clamped to the server maximum.

The response is the same paged envelope as `GET /api/data/{table}`:
`{ "data": [...], "pagination": { "page", "pageSize", "totalCount", "totalPages", "hasNext", "hasPrevious" } }`.
These examples run verbatim in the contract suite (`ComplexQueryApiTests`) — if the wire shape
drifts, the suite fails before the docs lie.

### Batch Operations

```yaml
POST   /api/batch/data                         # Mixed operations, in order, across tables
POST   /api/batch/data/{table}/insert          # Insert many into one table
PATCH  /api/batch/data/{table}                 # Update many, selected by filter
DELETE /api/batch/data/{table}?filter=...      # Delete many, selected by filter
PUT    /api/batch/data/{table}                 # Upsert many, matched on key columns
POST   /api/batch/data/{table}/seed            # Seed rows (upsert, ignoring conflicts)
POST   /api/batch/transaction                  # Atomic cross-entity operations
```

An operation names a **table and a data method** — it is not an embedded HTTP request:

```http
POST /api/batch/data
Content-Type: application/json
X-Project-Id: <project id>

{
  "operations": [
    { "method": "INSERT", "table": "customers", "data": { "name": "Acme" } },
    { "method": "UPDATE", "table": "customers", "id": "…", "data": { "grade": "VIP" } },
    { "method": "DELETE", "table": "orders", "id": "…" },
    { "method": "UPSERT", "table": "customers", "data": {...}, "keyColumns": ["email"] }
  ]
}
```

Operations run in order and are reported individually. **A batch containing failed operations still
returns 200** — read `results` rather than the status code:

```json
{
  "results": [
    { "index": 0, "success": true, "data": { "_id": "…" }, "affectedRows": 1 },
    { "index": 1, "success": false, "error": "null value in column 'name'" }
  ],
  "successCount": 1,
  "failureCount": 1
}
```

Inserting many rows into one table has a shorter form that takes a bare array and returns the same
response shape:

```http
POST /api/batch/data/customers/insert
[ { "name": "Acme" }, { "name": "Globex" } ]
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
  .withUrl("/hubs/morph")
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

Every write door — data CRUD, batch, seed, upsert, bulk import rows, GraphQL mutations — goes
through the same write pipeline: constraint validation (required / unique / FK / CHECK) is
validated and system columns (`_id`, timestamps, `_version`, audit fields) are applied uniformly.

The request body of a data write is the record itself — there is no `{ "data": ..., "options": ... }`
envelope:

```http
POST /api/data/{table}
Content-Type: application/json

{ "name": "John", "email": "john@example.com" }
```

Behaviour is selected per request with query parameters:

| Parameter | Effect |
|-----------|--------|
| `?mode=draft` | Skips validation and stores the row with `_row_state = 'draft'` (requires row state enabled on the table) |
| `?ignoreUnknown=true` | Fields naming no declared column are dropped instead of failing the write. Without it, an unknown field is a `400 UNKNOWN_COLUMN` naming the field — a typo must not become silent data loss |

The validation and auto-apply behaviours below are pipeline policy (what the server enforces),
not request-body switches.

### Validation Options

| Option | Default | Description |
|--------|---------|-------------|
| `validateRequired` | `true` | Validate required fields (NOT NULL) |
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

---

## Bulk Operations

Bulk import and export are **asynchronous jobs**. The request returns `202 Accepted` with a job
id; progress and results are read from the job endpoints. The format is part of the path, not a
query parameter, because each format takes its own options.

```http
# Import — one endpoint per format
POST /api/bulk/{table}/import/csv
POST /api/bulk/{table}/import/json
POST /api/bulk/{table}/import/ndjson
Content-Type: text/csv

name,email,grade
John Doe,john@example.com,VIP

# Export — options travel in the body
POST /api/bulk/{table}/export/csv
POST /api/bulk/{table}/export/json
POST /api/bulk/{table}/export/xlsx

# Following a job
GET  /api/bulk/jobs/{jobId}/progress      # Progress while it runs
POST /api/bulk/jobs/{jobId}/cancel        # Stop it
GET  /api/bulk/import                     # List import jobs
GET  /api/bulk/export                     # List export jobs
GET  /api/bulk/export/{jobId}/download    # Fetch a finished export
```

---

## Schema Evolution

### Update Column

Update a column's metadata and/or physical constraints:

```http
PATCH /api/schema/columns/{columnId}
Content-Type: application/json

{
  "name": "new_column_name",
  "type": "biginteger",
  "nullable": true,
  "unique": false,
  "check": "value > 0",
  "default": "0",
  "version": 3
}
```

All fields except `version` are optional. Only provided fields are changed.

| Field | Description |
|-------|-------------|
| `name` | New logical column name |
| `type` | New data type (safe type widening only: integer→biginteger→decimal, *→text) |
| `nullable` | Whether the column allows null |
| `unique` | Whether the column has a unique constraint (physical DDL) |
| `check` | Check expression (virtual constraint) — see [Expression fields](#expression-fields) |
| `default` | Default value — see [Expression fields](#expression-fields) |
| `version` | Expected schema version for optimistic concurrency |

### Expression fields

`default` and an index `where` are written into DDL, so what they may contain is bounded. `check`
never reaches DDL at all — it is a **virtual constraint**, enforced by the app-layer evaluator, and
a declaration is accepted only when that evaluator can enforce it (a stored-but-unenforceable CHECK
would constrain nothing, silently). A value outside these bounds is answered with `400` and an
error code, not applied.

| Field | Accepted | Rejected (`400`) |
|-------|----------|------------------|
| `default` | A literal (`0`, `pending`, `O'Brien` — quoted for you), or one of `gen_random_uuid()`, `now()`, `transaction_timestamp()`, `statement_timestamp()`, `clock_timestamp()` | Any other value containing parentheses → `INVALID_DEFAULT`. Notably `uuid_generate_v4()`: it needs the `uuid-ossp` extension, which managed PostgreSQL does not grant. Use `gen_random_uuid()`. |
| `check` | The CHECK grammar: `<field> <op> <value>` or `<field> <op> <field>` (op: `>` `>=` `<` `<=` `=` `==` `!=` `<>`; value: a `'quoted string'`, number, `true`/`false`/`null`), `<field> MATCHES '<regex>'`, combined with `AND`/`OR` and parentheses — `age >= 0 AND age <= 150`, `status = 'a)b'`, `email MATCHES '^[^@]+@[^@]+$'` | Anything else — SQL functions, `IN`, `BETWEEN`, the `~` operator (use `MATCHES`) → `INVALID_ARGUMENT` listing the supported forms |
| index `where` | Any predicate that stays within itself — `age >= 0`, `status = 'a)b'` | Unbalanced parentheses or quotes, a statement separator, or a comment → `INVALID_EXPRESSION` |

MorphDB requires no PostgreSQL extension. This is what lets it run on Azure Database for PostgreSQL,
Cloud SQL and RDS, where `CREATE EXTENSION` is gated behind a server-parameter allow-list.

---

## Attachment Type

The `attachment` data type stores file metadata as JSONB. MorphDB does not manage file storage directly — files should be stored in external services (S3, Azure Blob, etc.) and referenced by URL.

### Schema

```json
{
  "url": "https://s3.example.com/bucket/file.pdf",
  "filename": "report.pdf",
  "size": 1048576,
  "mimeType": "application/pdf",
  "uploadedAt": "2026-01-01T00:00:00Z"
}
```

### Usage

```http
POST /api/schema/tables/{name}/columns
{
  "name": "document",
  "type": "attachment",
  "nullable": true
}
```

```http
POST /api/data/{table}
{
  "document": {
    "url": "https://storage.example.com/file.pdf",
    "filename": "file.pdf",
    "size": 2048,
    "mimeType": "application/pdf"
  }
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `url` | string | Yes | URL to the file in external storage |
| `filename` | string | Yes | Original file name |
| `size` | number | No | File size in bytes |
| `mimeType` | string | No | MIME type |
| `uploadedAt` | string | No | ISO 8601 timestamp |

---

## Row-Level Security Policies

A policy narrows what a table's rows answer, per operation. Applicable policies are combined with
`AND`, so adding one can only ever restrict a read further.

```yaml
GET    /api/security/policies/{tableName}      # Policies applying to a table
POST   /api/security/policies                  # Create a policy
PATCH  /api/security/policies/{policyId}       # Update name, expression, description or is_active
DELETE /api/security/policies/{policyId}       # Delete a policy
```

```json
{
  "name": "owner_reads_only",
  "tableName": "orders",
  "policyType": "Select",
  "expression": "owner_id = {{user_id}}"
}
```

`policyType` is `Select`, `Insert`, `Update`, `Delete` or `All`. The expression is a SQL predicate
over the table's own columns, with `{{user_id}}`, `{{email}}`, `{{role}}`, `{{project_id}}`,
`{{is_authenticated}}` and `{{claims.<name>}}` substituted from the request's security context
before the query runs. Substituted values are emitted as quoted literals — a caller's identity
cannot become part of the predicate.

Because the service is unauthenticated, an HTTP request's context is the project's anonymous one:
`{{project_id}}` carries the header's value, `{{is_authenticated}}` is `false`, and the
user-bearing placeholders substitute `NULL` — a policy written against `{{user_id}}` therefore
matches no rows over HTTP. That is fail-closed on purpose; there is currently no way for a caller
to assert an end-user identity to this service.

**The expression is a predicate, not a statement.** It is checked before it is stored and again
before it is used: a statement separator, a comment opener, an unbalanced parenthesis or an
unterminated quote is refused with `INVALID_EXPRESSION`. A stored policy that fails that check
fails the read rather than being quietly dropped from it — a security rule that silently stops
applying is worse than an error.

---

## Health Checks

```http
GET /health        # Overall health
GET /health/live   # Liveness probe
GET /health/ready  # Readiness probe
```

---

## Errors

Every error is a JSON envelope — **no path answers a 5xx with an empty body**:

```json
{ "error": "ValidationError", "message": "what went wrong, and what is possible", "code": "VALIDATION_ERROR" }
```

`code` is the machine-readable contract; branch on it, not on `message` text. Request envelopes are
strict: a JSON member a request body does not declare (a typo'd `filters` for `filter`, a `colums`
for `columns`) answers `400 INVALID_ARGUMENT` naming the member and listing the supported ones —
never a silent drop. (Row-data bodies are dictionaries — arbitrary members are the point; their
unknown-field policy is the write pipeline's `UNKNOWN_COLUMN`.) A `4xx` means the
request must change before retrying; a `500 INTERNAL_ERROR` is a service defect (its message is a
fixed string — internal exception text never reaches the wire) and retrying may succeed.

| Status | Code | When |
|--------|------|------|
| 400 | `VALIDATION_ERROR` | A value failed validation — a required / unique / FK / CHECK constraint, a type mismatch, or any mix of write-validation causes; physical `NOT NULL`/`UNIQUE` violations translate to the same code |
| 400 | `UNKNOWN_COLUMN` | A write named a column the table does not declare (see `?ignoreUnknown=true`) — answered whenever undeclared fields are the only thing wrong with the write |
| 400 | `COLUMN_NOT_FOUND` | A query referenced a column the table does not have |
| 400 | `INVALID_FILTER` | A malformed `filter` expression, or an unknown filter operator |
| 400 | `INVALID_ARGUMENT` | A malformed value elsewhere in the request (e.g. an unknown column type — the message lists the supported set) |
| 400 | `MISSING_PROJECT` | The request did not say which project it applies to — send `X-Project-Id` |
| 400 | `INVALID_EXPRESSION` | A CHECK predicate, index predicate or policy expression that could escape the clause it is written into |
| 400 | `TABLE_HAS_DEPENDENTS` | Deleting a table another table still references — delete those relations first |
| 400 | `EMPTY_BATCH` | A batch request with no operations |
| 400 | `EMPTY_DATA` | A batch write with no rows |
| 400 | `EMPTY_TRANSACTION` | A transaction with no operations |
| 400 | `EMPTY_RECORD_IDS` | A bulk-delete with no record ids |
| 400 | `MISSING_KEY_COLUMNS` | A batch upsert without the key columns to match on |
| 400 | `FILTER_REQUIRED` | A batch update-by-filter without a filter (a full-table write must be said out loud) |
| 400 | `AGGREGATION_REQUIRED` | An aggregate query with no aggregation |
| 400 | `ROW_STATE_NOT_ENABLED` | A row-state operation on a table whose `systemColumns.rowState` is off |
| 400 | `JOB_NOT_COMPLETED` | Reading the result of a bulk job that has not finished |
| 400 | `NOT_MATERIALIZED` | Refreshing or reading a view that is not materialized |
| 404 | `TABLE_NOT_FOUND` | The table (or the project the request scoped it to) does not exist |
| 404 | `RECORD_NOT_FOUND` | The record id does not exist in the table |
| 404 | `PROJECT_NOT_FOUND` | The project id does not exist |
| 404 | `VIEW_NOT_FOUND` | The view does not exist |
| 404 | `JOB_NOT_FOUND` | The bulk job does not exist |
| 404 | `AUDIT_LOG_NOT_FOUND` | The audit log entry does not exist |
| 404 | `NOT_FOUND` | Another addressable resource (entity set, policy…) does not exist |
| 409 | `DUPLICATE_NAME` | Creating a table/column under a name that is taken |
| 409 | `DUPLICATE_SLUG` | Creating a project under a slug that is taken |
| 409 | `SCHEMA_VERSION_CONFLICT` | An optimistic schema update lost the race |
| 409 | `LOCK_ACQUISITION_FAILED` | A concurrent schema operation holds the lock — retry |
| 500 | `INTERNAL_ERROR` | Our defect, logged on the server — never your request's fault |
