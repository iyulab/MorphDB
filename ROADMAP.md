# MorphDB Development Roadmap

## Overview

MorphDB is developed in 24 phases. Each phase represents an independently testable functional unit.

---

## Architecture Overview

### Schema-based Multi-tenancy Model (Phase 17+)

```
morphdb (Global Control Plane)
├── _organizations        # Organization registry
├── _projects            # Project registry
├── _global_config       # Global settings
└── _schema_migrations   # Migration tracking

p_{project_id}_sys (Project System Layer)
├── _tables              # Table metadata
├── _columns             # Column metadata
├── _relations           # Relation definitions
├── _indexes             # Index definitions
├── _api_keys            # API keys for this project
├── _webhooks            # Webhook configurations
├── _webhook_deliveries  # Delivery tracking
├── _security_policies   # RLS policies
├── _audit_logs          # Audit trail (partitioned)
├── _import_jobs         # Bulk import jobs
└── _export_jobs         # Bulk export jobs

p_{project_id}_dat (Project Data Layer)
├── customers            # User-defined tables (logical names!)
├── orders               # No hash naming needed
├── products
└── ...
```

### Naming Convention

| Component | Format | Example |
|-----------|--------|---------|
| Project System Schema | `p_{id8}_sys` | `p_a1b2c3d4_sys` |
| Project Data Schema | `p_{id8}_dat` | `p_a1b2c3d4_dat` |
| System Tables | `_{name}` | `_tables`, `_audit_logs` |
| User Tables | `{logical_name}` | `customers`, `orders` |

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

## Phase 13: Encryption Testing & Key Rotation ✅ Completed

**Goal**: Production-ready encryption with comprehensive testing

### 13.1 Encryption Testing
- [x] Unit tests for `AesGcmDataEncryptionService`
- [x] Key derivation tests (HKDF-based hierarchy)
- [x] Tenant/table isolation verification
- [x] Round-trip encryption/decryption validation

### 13.2 Key Rotation
- [x] `IKeyRotationService` abstraction
- [x] `KeyRotationService` implementation
- [x] Batch re-encryption support
- [x] Progress tracking and resumption
- [x] Versioned key management

### 13.3 API Integration
- [x] `SecurityController` encryption endpoints
- [x] POST `/api/security/encryption/rotate-key`
- [x] POST `/api/security/encryption/table/{table}/re-encrypt`
- [x] GET `/api/security/encryption/status`

---

## Phase 14: Query Builder JOIN Completion ✅ Completed

**Goal**: Full JOIN support with logical name resolution

### 14.1 JOIN Enhancement
- [x] Joined table metadata resolution
- [x] Physical name translation for JOIN tables
- [x] Column resolution across joined tables
- [x] Cached metadata for joined tables

---

## Phase 15: Webhook Reliability & DLQ ✅ Completed

**Goal**: Production-grade webhook delivery with Dead Letter Queue

### 15.1 Dead Letter Queue
- [x] `WebhookDlqMessage` model
- [x] DLQ database table (`_morph_webhook_dlq`)
- [x] DLQ reasons (MaxRetries, WebhookDeleted, WebhookInactive, PersistentClientError)
- [x] DLQ status tracking (PendingReview, Resolved, Archived, Replayed)

### 15.2 Enhanced Retry Logic
- [x] Exponential backoff with jitter
- [x] Persistent 4xx error detection (immediate DLQ)
- [x] Inactive webhook handling
- [x] Configurable retry settings

### 15.3 DLQ API
- [x] GET `/api/webhooks/dlq` - List DLQ messages
- [x] GET `/api/webhooks/dlq/stats` - DLQ statistics
- [x] GET `/api/webhooks/dlq/{dlqId}` - Get DLQ message
- [x] POST `/api/webhooks/dlq/{dlqId}/resolve` - Resolve DLQ message
- [x] POST `/api/webhooks/dlq/{dlqId}/replay` - Replay DLQ message
- [x] POST `/api/webhooks/dlq/archive` - Archive old DLQ messages

---

## Phase 16: Performance Optimization ✅ Completed

**Goal**: Caching and connection pooling for production performance

### 16.1 Schema Caching
- [x] `ISchemaCache` abstraction
- [x] `RedisSchemaCache` implementation
- [x] `CachingSchemaManagerDecorator` pattern
- [x] Table metadata caching (tenant-isolated)
- [x] Automatic cache invalidation on schema changes

### 16.2 Connection Pooling
- [x] `ConnectionPoolOptions` configuration
- [x] Configurable pool sizes (min/max)
- [x] Connection idle lifetime management
- [x] Connection pruning intervals
- [x] Command timeout settings
- [x] Optional multiplexing support

### 16.3 Service Registration
- [x] Conditional Redis cache registration
- [x] Decorator pattern integration
- [x] Configuration-driven optimization

---

## Phase 17: Schema-based Layer Separation ✅ Completed

**Goal**: Implement foundational schema-based multi-tenancy architecture

**Priority**: Critical | **Effort**: High

### 17.1 Core Abstractions
- [x] `ISchemaLayerService` - Schema layer management abstraction
- [x] `SchemaType` enum (System, Data, Global, Unknown)
- [x] `SchemaNames` model (SystemSchema, DataSchema)
- [x] `Project` model with schema references

### 17.2 Schema Naming Service
- [x] `ISchemaNameResolver` interface
- [x] `PostgresSchemaNameResolver` implementation
- [x] Short ID generation (first 8 chars of UUID, lowercase)
- [x] Schema name validation (PostgreSQL 63-char limit)
- [x] `TryParseSchemaName` for reverse lookup

### 17.3 DdlBuilder Enhancement
- [x] Schema-qualified DDL operations
- [x] `BuildCreateSchema` / `BuildDropSchema`
- [x] `BuildSystemTablesDdl` for project system tables
- [x] Schema existence queries

### 17.4 PostgresSchemaLayerService
- [x] `ProvisionProjectSchemasAsync` - Create both schemas with system tables
- [x] `DropProjectSchemasAsync` - Remove project schemas
- [x] `SchemaExistsAsync` / `ProjectSchemasExistAsync`
- [x] `GetSchemaStatsAsync` / `GetProjectStatsAsync`
- [x] `ValidateSchemaHealthAsync` - Health check with issue detection
- [x] `ListManagedSchemasAsync` - List all MorphDB schemas

### 17.5 Project Repository & Service
- [x] `IProjectRepository` - Project CRUD operations
- [x] `ProjectRepository` PostgreSQL implementation
- [x] `IProjectService` - Project lifecycle management
- [x] `ProjectService` with schema provisioning coordination
- [x] Project status lifecycle (Provisioning → Active → Suspended/Archived → Deleted)

### 17.6 Database Schema
- [x] `morphdb._morph_organizations` table
- [x] `morphdb._morph_projects` table
- [x] Project-specific system tables template
- [x] Indexes for efficient queries

### 17.7 Service Registration
- [x] `ISchemaNameResolver` → `PostgresSchemaNameResolver`
- [x] `ISchemaLayerService` → `PostgresSchemaLayerService`
- [x] `IProjectRepository` → `ProjectRepository`
- [x] `IProjectService` → `ProjectService`

**Key Implementations**:
- `PostgresSchemaNameResolver`: Schema naming with `p_{id8}_sys` / `p_{id8}_dat` format
- `PostgresSchemaLayerService`: Full schema lifecycle management
- `ProjectService`: Project lifecycle with atomic provisioning/cleanup
- High-performance logging with `LoggerMessage` source generators

---

## Phase 18: Project Lifecycle API ✅

**Goal**: Project management REST API

**Priority**: Critical | **Effort**: Low

> Note: v0.x simplification - Schema migrations deferred to v1.x when needed.
> Current approach: drop/recreate schemas for development iteration.

### 18.1 Project Provisioner ✅
- [x] Covered by Phase 17 `ISchemaLayerService`
- [x] `PostgresSchemaLayerService.ProvisionProjectSchemasAsync()`
- [x] Idempotent schema creation

### 18.2 Project Lifecycle API ✅
- [x] `ProjectController` with full CRUD
- [x] POST `/api/projects` - Create project (provisions schemas)
- [x] DELETE `/api/projects/{id}` - Delete project (drops schemas)
- [x] GET `/api/projects/{id}/stats` - Schema statistics
- [x] GET `/api/projects/{id}/health` - Schema health check
- [x] Lifecycle actions: suspend, reactivate, archive

---

## Phase 19: Audit Logging ✅ Completed

**Goal**: Comprehensive audit trail with schema-based isolation

**Priority**: Critical | **Effort**: Medium

> Note: v0.x simplification - Partitioning and hash chain deferred to v1.x.
> Current approach: Simple audit table with indexed queries.

### 19.1 Audit Event Model ✅
- [x] `IAuditService` abstraction
- [x] `AuditEvent` model with full context
- [x] Event categories: auth, data, schema, admin, security
- [x] Severity levels: debug, info, warning, error, critical

### 19.2 Audit Capture ✅
- [x] HTTP middleware for API audit (`AuditMiddleware`)
- [x] Request/response capture with timing
- [x] Async Channel-based queue for non-blocking writes
- [x] Authentication event capture (JWT/API Key)

### 19.3 Schema-isolated Storage ✅
- [x] `_audit_logs` table in each `p_{id}_sys` schema
- [x] Indexes for efficient querying
- [x] PostgresAuditService with background batch writer

### 19.4 Audit Query API ✅
- [x] GET `/api/projects/{id}/audit/logs` - Query logs
- [x] GET `/api/projects/{id}/audit/logs/{logId}` - Get specific
- [x] GET `/api/projects/{id}/audit/stats` - Statistics
- [x] Filters: time, actor, resource, action, severity

**Key Implementations**:
- `IAuditService`: Audit logging abstraction
- `PostgresAuditService`: Channel-based async queue with batch writes
- `AuditMiddleware`: HTTP request/response capture
- `AuditController`: Query API with pagination and filtering

---

## Phase 20: Rate Limiting & Quota ✅ Completed

**Goal**: Fair usage enforcement and resource protection

**Priority**: High | **Effort**: Medium

> Note: v0.x simplification - Memory-based implementation.
> Redis-based distributed limiting deferred to v1.x.

### 20.1 Rate Limiter Core ✅
- [x] `IRateLimiter` interface
- [x] Token bucket algorithm (`MemoryRateLimiter`)
- [x] Per-project rate configuration
- [x] Configurable limits per project

### 20.2 Rate Limit Middleware ✅
- [x] ASP.NET Core middleware (`RateLimitMiddleware`)
- [x] Rate limit headers (X-RateLimit-Limit, X-RateLimit-Remaining, X-RateLimit-Reset)
- [x] 429 response with Retry-After
- [x] Path-based exclusions

### 20.3 Quota Management ✅
- [x] `IQuotaService` interface
- [x] `MemoryQuotaService` implementation
- [x] API call quota tracking (per project)
- [x] Data read/write tracking
- [x] Storage/bandwidth tracking

### 20.4 Quota API ✅
- [x] GET `/api/projects/{id}/quota` - Combined summary
- [x] GET `/api/projects/{id}/quota/usage` - Current usage
- [x] GET `/api/projects/{id}/quota/limits` - Quota limits
- [x] GET `/api/projects/{id}/quota/rate-limit` - Rate limit status

**Key Implementations**:
- `IRateLimiter`: Rate limiting abstraction
- `MemoryRateLimiter`: Token bucket with sliding window
- `RateLimitMiddleware`: Request throttling with headers
- `IQuotaService`: Usage tracking abstraction
- `MemoryQuotaService`: In-memory quota tracking
- `QuotaController`: Usage and limits query API

---

## Phase 21: Organization Hierarchy & RBAC ✅ Completed

**Goal**: Enterprise organization and permission management

**Priority**: High | **Effort**: High

### 21.1 Organization Model
- [x] `Organization` entity in morphdb schema
- [x] `OrganizationMember` with roles
- [x] Organization settings and billing link
- [x] Organization-level SSO configuration

### 21.2 Project Hierarchy
- [x] Projects belong to Organizations
- [x] `ProjectMember` with roles
- [x] Environment concept (prod/staging/dev)
- [x] Project-level settings

### 21.3 RBAC System
- [x] `IPermissionService` interface
- [x] Built-in roles: owner, admin, developer, viewer
- [x] Custom role definitions (Enterprise) - v1.x
- [x] Permission inheritance (org → project)

### 21.4 Organization API
- [x] CRUD `/api/organizations`
- [x] CRUD `/api/organizations/{id}/members`
- [x] CRUD `/api/organizations/{id}/projects`
- [x] Role assignment endpoints

---

## Phase 22: OIDC/SAML SSO ✅ Completed

**Goal**: Enterprise identity provider integration

**Priority**: High | **Effort**: High

### 22.1 OIDC Support
- [x] `ISsoAuthenticationService` interface
- [x] Generic OIDC provider
- [x] Pre-configured: Google, Microsoft, Auth0, Okta, Keycloak
- [x] PKCE flow
- [x] Token refresh handling

### 22.2 SAML Support (Enterprise)
- [ ] `ISamlService` interface - v1.x
- [ ] SP metadata generation - v1.x
- [ ] IdP configuration - v1.x
- [ ] Attribute mapping - v1.x
- [ ] JIT provisioning - v1.x

> Note: v0.x simplification - SAML support deferred to v1.x. OIDC covers most enterprise use cases.

### 22.3 SSO Configuration API
- [x] POST `/api/organizations/{id}/sso/configs` - Create SSO config
- [x] GET `/api/organizations/{id}/sso/configs` - List SSO configs
- [x] POST `/api/sso/login/{orgSlug}` - Initiate SSO login
- [x] POST `/api/sso/callback/{orgSlug}` - Complete SSO login

### 22.4 MFA Enhancement
- [ ] WebAuthn/FIDO2 support - v1.x
- [ ] Organization-level MFA enforcement - v1.x
- [ ] Backup codes - v1.x

> Note: v0.x simplification - MFA enhancements deferred to v1.x.

---

## Phase 23: Backup & PITR (Schema-based) ✅ Completed

**Goal**: Data protection with schema-level granularity

**Priority**: Critical | **Effort**: High

> Note: v0.x simplification - Cloud storage and PITR deferred to v1.x.
> Current approach: Local storage with pg_dump/psql for backup/restore.

### 23.1 Backup Service
- [x] `IBackupService` interface
- [x] Schema-level backup (`pg_dump -n schema`)
- [x] Full project backup (both schemas)
- [ ] Scheduled backup jobs - v1.x
- [x] On-demand backup API

### 23.2 Storage Backend
- [x] Local storage support
- [ ] S3/GCS/Azure Blob support - v1.x
- [x] Backup compression (Gzip via pg_dump -Z)
- [x] SHA-256 checksum verification
- [ ] Cross-region storage - v1.x

### 23.3 Point-in-Time Recovery
- [ ] WAL archiving per project (Enterprise) - v1.x
- [ ] PITR target selection - v1.x
- [ ] Recovery to new project - v1.x
- [ ] Recovery verification - v1.x

> Note: PITR requires WAL archiving infrastructure, deferred to v1.x.

### 23.4 Backup API
- [x] POST `/api/projects/{id}/backups` - Create backup
- [x] GET `/api/projects/{id}/backups` - List backups
- [x] GET `/api/projects/{id}/backups/{bid}` - Get backup
- [x] POST `/api/projects/{id}/backups/{bid}/restore` - Restore backup
- [x] GET `/api/projects/{id}/backups/{bid}/download` - Download backup file
- [x] DELETE `/api/projects/{id}/backups/{bid}` - Delete backup

**Key Implementations**:
- `IBackupService`: Backup lifecycle abstraction
- `BackupService`: pg_dump/psql execution with async processing
- `BackupRepository`: Backup metadata persistence
- `BackupController`: REST API with permission checks

---

## Phase 24: Desktop Client (MorphDB Desk) 🔄 In Progress

**Goal**: Native database management tool like pgAdmin/DBeaver/TablePlus

**Priority**: High | **Effort**: High

**Tech Stack**: Electron + Vite + React 19 + TypeScript + Tailwind CSS v4 + shadcn/ui

**Location**: `desk/`

### 24.1 Foundation & Scaffolding ✅
- [x] electron-vite project setup
- [x] React 19 + TypeScript configuration
- [x] Tailwind CSS v4 + shadcn/ui integration
- [x] Project structure (main/renderer/preload)
- [x] IPC communication layer
- [x] Basic window management (minimize, maximize, close)
- [x] Application menu structure

### 24.2 Connection Management ✅
- [x] Connection profile model (URL, API Key)
- [x] Add/Edit/Delete connection dialog
- [x] Connection testing with health check
- [x] Secure credential storage (electron-store + encryption)
- [ ] Multi-connection tabs
- [ ] Recent connections history
- [x] Connection status indicator

### 24.3 Project & Table Explorer
- [ ] Tree view component (projects → tables)
- [ ] Lazy loading for large projects
- [ ] Table metadata sidebar (columns, relations, indexes)
- [ ] Context menu (Create, Edit, Delete, Refresh)
- [ ] Search/filter tables
- [ ] Favorites/pinned tables
- [ ] Table icon by column count/type

### 24.4 Table CRUD
- [ ] Create table wizard (name, description)
- [ ] Edit table properties dialog
- [ ] Delete table with confirmation
- [ ] Duplicate table structure
- [ ] Table structure view (columns, relations)
- [ ] DDL export (CREATE TABLE statement)

### 24.5 Column Management
- [ ] Column list view with drag-reorder
- [ ] Add column dialog (all MorphDB types)
- [ ] Edit column properties (name, type, nullable, default)
- [ ] Delete column with impact warning
- [ ] Column type visualization (icons)
- [ ] Relation indicator (FK badge)

### 24.6 Data Grid & Record CRUD
- [ ] Virtualized data grid (tanstack-table + tanstack-virtual)
- [ ] Pagination with configurable page size
- [ ] Column sorting (multi-column)
- [ ] Column filtering (per-type filters)
- [ ] Inline cell editing with validation
- [ ] Add new row (empty row at top/bottom)
- [ ] Delete row(s) with confirmation
- [ ] Bulk selection and operations
- [ ] Copy/paste support (cells, rows)
- [ ] Null value handling
- [ ] JSON/Array column expansion

### 24.7 Query Console
- [ ] OData query builder (visual)
- [ ] GraphQL query editor with syntax highlight
- [ ] Query execution with timing
- [ ] Results grid with export
- [ ] Query history with search
- [ ] Saved queries per connection
- [ ] Explain query (if supported)

### 24.8 Import/Export
- [ ] Import from CSV/JSON/Excel
- [ ] Export to CSV/JSON/Excel
- [ ] Column mapping UI
- [ ] Preview before import
- [ ] Progress indicator for large files
- [ ] Bulk job status tracking

### 24.9 Distribution & Polish
- [ ] Dark/Light theme with system preference
- [ ] Keyboard shortcuts (Cmd/Ctrl+S, etc.)
- [ ] Settings management (preferences)
- [ ] Auto-update (electron-updater)
- [ ] Cross-platform builds (Windows, macOS, Linux)
- [ ] Installer/DMG/AppImage packaging
- [ ] Error handling & crash reporting
- [ ] Telemetry (opt-in)
- [ ] Documentation & help

---

## Version Milestones

| Version | Phases | Goal | Status |
|---------|--------|------|--------|
| 0.1.0 | 0-3 | Core functionality complete | ✅ Completed |
| 0.2.0 | 4-6 | API layer complete | ✅ Completed |
| 0.3.0 | 7-8 | Real-time features | ✅ Completed |
| 0.4.0 | 9-10 | Bulk & SDKs | ✅ Completed |
| 0.5.0 | 11-12 | Production Ready (Beta) | ✅ Completed |
| 0.6.0 | 13-16 | Enterprise Hardening | ✅ Completed |
| 0.7.0 | 17-18 | Schema Architecture | ✅ Completed |
| 0.8.0 | 19-20 | Audit + Rate Limiting | ✅ Completed |
| **0.9.0** | **21-22** | **Organization + SSO** | ✅ Completed |
| **0.10.0** | **23** | **Backup & PITR** | ✅ Completed |
| **1.0.0** | **24** | **Desktop Client (MorphDB Desk)** | 🔄 In Progress |

---

## Migration Path

### For Existing Users

```yaml
legacy_compatibility:
  mode: dual_track

  existing_tenants:
    - Continue working unchanged
    - Opt-in migration available
    - No forced migration

  migration_process:
    1. Create new project with schema-based architecture
    2. Export data from legacy tenant
    3. Import to new project
    4. Verify and switch over
    5. Decommission legacy tenant

  timeline:
    - v0.7.0: New projects use schema-based
    - v0.8.0: Migration tool available
    - v1.0.0: Legacy mode deprecated notice
    - v2.0.0: Legacy mode removed
```

---

## Compliance Targets

| Compliance | Target Version | Key Requirements |
|------------|----------------|------------------|
| **GDPR** | 0.8.0 | Audit logs, data export, deletion |
| **SOC 2 Type I** | 0.9.0 | Security controls, access management |
| **SOC 2 Type II** | 1.0.0 | 6-month audit period |
| **HIPAA** | 1.0.0+ | BAA, encryption, audit, access control |

---

## Risk Mitigation

| Risk | Mitigation |
|------|------------|
| Schema explosion | Monitoring, cleanup automation |
| Migration complexity | Dual-mode, gradual rollout |
| Connection pool issues | Explicit schema in queries |
| Cross-tenant queries | Strict schema isolation |
| Performance impact | Benchmarking at each phase |

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
