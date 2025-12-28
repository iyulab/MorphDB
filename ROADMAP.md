# MorphDB Development Roadmap

## Overview

MorphDB is developed in 12 phases. Each phase represents an independently testable functional unit.

---

## Phase 0: Foundation ✅ Completed

**Goal**: Solution structure and development environment setup

- [x] Solution structure (MorphDB.sln)
- [x] Central Package Management (Directory.Packages.props)
- [x] MorphDB.Core - Core abstractions and interfaces
- [x] MorphDB.Npgsql - PostgreSQL provider skeleton
- [x] MorphDB.Service - ASP.NET Core Web API skeleton
- [x] MorphDB.Tests - xUnit test framework
- [x] Docker Compose development environment
- [x] GitHub Actions CI/CD pipeline
- [x] Code style (.editorconfig)

**Key Components**:
- `ISchemaManager`, `ISchemaMapping`, `INameHasher` interfaces
- `IMorphQueryBuilder`, `IMorphDataService` query abstractions
- `Sha256NameHasher` - Logical name → Physical name hash generation
- `PostgresAdvisoryLockManager` - Advisory Lock for DDL serialization

---

## Phase 1: Core Schema Management ✅ Completed

**Goal**: Dynamic schema creation and management

### 1.1 SchemaManager Implementation
- [x] `ISchemaManager` implementation (`PostgresSchemaManager`)
- [x] Table CRUD (CREATE, ALTER, DROP)
- [x] Column CRUD (ADD, MODIFY, DROP)
- [x] System table synchronization (`MetadataRepository`)

### 1.2 DDL Safety
- [x] Advisory Lock integration (`PostgresAdvisoryLockManager`)
- [x] Transaction-based DDL
- [x] DdlBuilder - DDL SQL generation

### 1.3 Change Logging
- [x] `ChangeLogger` - _morph_changelog recording
- [x] `SchemaChangeEntry` - Change history model

### 1.4 Testing
- [x] Unit tests (`DdlBuilderTests`)
- [x] Integration tests (`SchemaManagerTests`, `MetadataRepositoryTests`)

---

## Phase 2: Data Operations ✅ Completed

**Goal**: Basic CRUD data manipulation

### 2.1 DataService Implementation
- [x] `IMorphDataService` implementation (`PostgresDataService`)
- [x] INSERT, UPDATE, DELETE operations
- [x] Batch DML support (InsertBatchAsync)
- [x] Upsert support (INSERT ... ON CONFLICT)

### 2.2 Logical → Physical Name Conversion
- [x] DmlBuilder - DML SQL generation
- [x] Automatic naming conversion (logical ↔ physical)
- [x] Auto tenant_id injection

### 2.3 Type Mapping
- [x] MorphDataType → PostgreSQL conversion (`TypeMapper`)
- [x] JSONB type serialization/deserialization
- [x] Value validation and conversion

### 2.4 Testing
- [x] Unit tests (`DmlBuilderTests`)
- [x] Integration tests (`DataServiceTests`)

---

## Phase 3: Query Builder ✅ Completed

**Goal**: Logical query interface

### 3.1 MorphQueryBuilder Implementation
- [x] `IMorphQueryBuilder` implementation (`MorphQueryBuilder`)
- [x] SELECT, WHERE, JOIN, ORDER BY
- [x] Aggregate functions (COUNT, SUM, AVG, MIN, MAX)

### 3.2 SqlKata Integration
- [x] Physical query generation (logical → physical name conversion)
- [x] Parameter binding

### 3.3 Pagination
- [x] Offset-based pagination (Limit/Offset)
- [x] Cursor-based pagination (After/Before)

### 3.4 Testing
- [x] Integration tests (`QueryBuilderTests`)

---

## Phase 4: REST API ✅ Completed

**Goal**: RESTful API endpoints

### 4.1 Schema API
- [x] POST /api/schema/tables
- [x] GET/PATCH/DELETE /api/schema/tables/{name}
- [x] Column, relation, index management

### 4.2 Data API
- [x] GET /api/data/{table} (filter, sort, pagination)
- [x] GET /api/data/{table}/{id}
- [x] POST /api/data/{table} (Insert)
- [x] PATCH /api/data/{table}/{id} (Update)
- [x] DELETE /api/data/{table}/{id}

### 4.3 Batch API
- [x] POST /api/batch/data (mixed operations)
- [x] POST /api/batch/data/{table}/insert (bulk insert)
- [x] PATCH /api/batch/data/{table} (filter-based update)
- [x] DELETE /api/batch/data/{table} (filter-based delete)
- [x] PUT /api/batch/data/{table} (Upsert)

**Key Implementations**:
- `SchemaController`: Table, column, index, relation CRUD
- `DataController`: Data query and CRUD
- `BatchController`: Bulk operations
- X-Tenant-Id header-based tenant isolation
- Filter expressions (column:operator:value)

---

## Phase 5: GraphQL ✅ Completed

**Goal**: HotChocolate-based GraphQL

### 5.1 Dynamic Schema Generation
- [x] Table → GraphQL Type mapping (`DynamicSchemaBuilder`)
- [x] Query generation (`DynamicQuery` - GetTables, GetTable, GetRecords, GetRecord)
- [x] Mutation generation (`DynamicMutation` - CreateRecord, UpdateRecord, DeleteRecord, UpsertRecord, CreateRecords)
- [x] Tenant context support (`ITenantContextAccessor`)

### 5.2 Relation Resolution
- [x] FK → GraphQL relation fields (`RelationGraphType`)
- [x] DataLoader integration (`TableByNameDataLoader`, `TableByIdDataLoader`, `RecordByIdDataLoader`, `RelatedRecordsDataLoader`)

### 5.3 Subscription
- [x] GraphQL Subscription support (`DynamicSubscription`)
- [x] Change event streaming (`ISubscriptionEventSender`, `HotChocolateSubscriptionEventSender`)
- [x] WebSocket support (in-memory subscriptions)

---

## Phase 6: OData ✅ Completed

**Goal**: OData v4 protocol support

### 6.1 EDM Model
- [x] Dynamic $metadata generation (`DynamicEdmModelBuilder`)
- [x] Entity type mapping (MorphDataType → EdmPrimitiveTypeKind)
- [x] Navigation properties for relations
- [x] EDM model caching per tenant (`CachingEdmModelProvider`)

### 6.2 Query Options
- [x] $filter (eq, ne, gt, ge, lt, le, contains, startswith, endswith)
- [x] $orderby (asc, desc)
- [x] $top, $skip
- [x] $select
- [x] $count

### 6.3 CUD Operations
- [x] POST /odata/{entitySet} (Create)
- [x] PATCH /odata/{entitySet}({key}) (Update)
- [x] DELETE /odata/{entitySet}({key})
- [x] POST /odata/$batch (Batch requests)

**Key Implementations**:
- `DynamicEdmModelBuilder`: Static EDM model builder from table metadata
- `CachingEdmModelProvider`: Per-tenant EDM model caching with IServiceScopeFactory
- `ODataQueryHandler`: OData query options → MorphDB query conversion
- `MorphODataController`: OData endpoints (CRUD + batch)

---

## Phase 7: Real-time (WebSocket) ✅ Completed

**Goal**: Real-time data synchronization

### 7.1 SignalR Hub
- [x] MorphHub implementation
- [x] Table subscribe/unsubscribe
- [x] GetSubscriptions method
- [x] Tenant isolation (X-Tenant-Id header)

### 7.2 Change Detection
- [x] PostgreSQL LISTEN/NOTIFY (`morphdb_changes` channel)
- [x] Change event broadcast (INSERT, UPDATE, DELETE)
- [x] Database trigger function (`morphdb.notify_change()`)
- [x] Automatic trigger creation for all tables

### 7.3 Filtering
- [x] Subscription filter support (`SubscriptionOptions`)
- [x] Selective field transmission
- [x] Per-connection subscription management (`SubscriptionManager`)

**Key Implementations**:
- `MorphHub`: SignalR Hub for real-time subscriptions
- `IMorphHubClient`: Typed client interface (RecordCreated, RecordUpdated, RecordDeleted)
- `PostgresChangeListener`: BackgroundService for PostgreSQL LISTEN/NOTIFY
- `ChangeNotificationSetup`: Database trigger setup and management
- `SubscriptionManager`: Connection-based subscription tracking
- `RealtimeServiceExtensions`: Service registration extensions

---

## Phase 8: Webhook ✅ Completed

**Goal**: External system integration

### 8.1 Webhook Management
- [x] Webhook registration/deletion (`WebhookController`)
- [x] Event filtering (insert, update, delete triggers)
- [x] Webhook metadata storage (`WebhookRepository`)

### 8.2 Delivery
- [x] HTTP callback delivery (`WebhookDeliveryService`)
- [x] HMAC signing (SHA256 signature header)
- [x] Retry logic with exponential backoff
- [x] Background delivery queue (`WebhookBackgroundService`)

### 8.3 Monitoring
- [x] Delivery history (`WebhookDeliveryLog`)
- [x] Failure tracking

**Key Implementations**:
- `WebhookController`: Webhook CRUD endpoints
- `WebhookDeliveryService`: HTTP delivery with retry logic
- `WebhookBackgroundService`: Background queue processor
- PostgreSQL trigger integration for automatic webhook firing

---

## Phase 9: Bulk Operations ✅ Completed

**Goal**: Large-scale data processing

### 9.1 Import
- [x] CSV parsing (`CsvHelper`)
- [x] JSON/NDJSON parsing
- [x] Streaming processing for large files
- [x] Upsert mode support

### 9.2 Export
- [x] CSV generation
- [x] JSON generation
- [x] XLSX generation (`ClosedXML`)
- [x] Filter-based export

### 9.3 Controller
- [x] `BulkController`: Import/Export endpoints
- [x] Streaming response for exports

**Key Implementations**:
- `BulkImportService`: Import processing with validation
- `BulkExportService`: Export generation (CSV, JSON, XLSX)
- `BulkController`: REST endpoints for bulk operations

---

## Phase 10: Client SDKs ✅ Completed

**Goal**: Client libraries

### 10.1 .NET SDK
- [x] MorphDB.Client package
- [x] Schema management (`SchemaClient`)
- [x] Data operations (`DataClient`)
- [x] Bulk operations (`BulkClient`)
- [x] Real-time subscriptions (`RealtimeClient`)
- [x] Webhook management (`WebhookClient`)

### 10.2 TypeScript SDK (Design)
- [x] TypeScript SDK design in `clients/typescript/`
- [x] Type definitions
- [x] React Query integration examples

### 10.3 Python SDK (Design)
- [x] Python SDK design in `clients/python/`
- [x] Async support examples

---

## Phase 11: Security ✅ Completed

**Goal**: Authentication and access control

### 11.1 API Key System
- [x] anon-key: Public operations with RLS
- [x] service-key: Full access, bypasses RLS
- [x] Key management API (`SecurityController`)
- [x] BCrypt hashing for key storage
- [x] Key expiration support

### 11.2 JWT Authentication
- [x] JWT Bearer tokens (HMAC-SHA256)
- [x] Access and refresh token support
- [x] Custom `MorphDBAuthenticationHandler`
- [x] Claim-based permissions

### 11.3 Row-Level Security
- [x] RLS policy definition (`SecurityPolicyService`)
- [x] Tenant isolation via tenant_id
- [x] Variable substitution (`{{user_id}}`, `{{email}}`, `{{claims.x}}`)
- [x] Policy types (Select, Insert, Update, Delete, All)
- [x] Automatic query filtering

**Key Implementations**:
- `ApiKeyService`: API key CRUD and validation
- `JwtService`: JWT token generation and validation
- `SecurityPolicyService`: RLS policy management
- `SecurityContextAccessor`: AsyncLocal context propagation
- `MorphDBAuthenticationHandler`: Custom ASP.NET Core auth handler

---

## Phase 12: Deployment & Operations ✅ Completed

**Goal**: Deployment configurations

### 12.1 Deployment Options
- [x] Dockerfile (multi-stage, non-root user)
- [x] Docker Compose (PostgreSQL 16 + Redis + pgAdmin)
- [x] Kubernetes manifests (Namespace, ConfigMap, Secret, Deployment, Service, Ingress, HPA)
- [x] Kustomize integration

### 12.2 Observability
- [x] Health checks (`/health`, `/health/live`, `/health/ready`)
- [x] PostgreSQL health check
- [x] Redis health check
- [x] Prometheus metrics (`/metrics`)
- [x] OpenTelemetry tracing (OTLP export)
- [x] Runtime instrumentation

### 12.3 Documentation
- [x] Swagger/OpenAPI documentation
- [x] API security definitions (API Key, Bearer, TenantId)
- [x] README updated with complete feature documentation
- [x] Sample console application

---

## Version Milestones

| Version | Phases | Goal | Status |
|---------|--------|------|--------|
| 0.1.0 | 0-3 | Core functionality complete | ✅ Completed |
| 0.2.0 | 4-6 | API layer complete | ✅ Completed |
| 0.3.0 | 7-8 | Real-time features | ✅ Completed |
| 0.4.0 | 9-10 | Bulk & SDKs | ✅ Completed |
| **0.5.0** | **11-12** | **Production Ready (Beta)** | **✅ Completed** |

---

## Scope Definition

### MorphDB (This Repository)

MorphDB is the **open-source core** with MIT license, providing:

| Area | Features |
|------|----------|
| Schema | Table, column, relation, index, view management |
| Naming | logical_name ↔ hash_name mapping |
| Type | Strong type system, PostgreSQL native types |
| Validation | NOT NULL, UNIQUE, CHECK, FK constraints |
| Encryption | Column-level encryption/decryption |
| Default | DEFAULT values, auto_number, created_at/updated_at |
| Computed | GENERATED columns, computed fields |
| Query | Logical query → Physical query transformation |
| API | REST, GraphQL, OData auto-generation |
| Realtime | WebSocket-based change subscriptions |
| Event | Webhook delivery |
| Bulk | Import/Export |

### Out of Scope (Enterprise/Cloud)

The following features are provided by **MorphDB Enterprise** (commercial license):

| Area | Features |
|------|----------|
| Multi-tenancy | Project/Organization management |
| UI | Admin dashboard |
| Auth | OIDC/SAML/LDAP integration |
| Backup | Backup/Recovery |
| Audit | Audit logging |
| License | License management |

---

## Contributing

Each phase is developed in a feature branch and merged via PR.

```bash
git checkout -b feature/phase-X-feature-name
# ... develop ...
git push origin feature/phase-X-feature-name
# Create PR
```

## License

MIT License
