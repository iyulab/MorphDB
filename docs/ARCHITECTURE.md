# Architecture

## Overview

MorphDB is an abstraction layer over PostgreSQL that enables runtime schema changes while preserving relational database capabilities.

## System Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                     Client Layer                            │
│  REST API    GraphQL    OData    WebSocket    Webhook       │
│  /api/*      /graphql   /odata/* /hubs/morph  (outbound)    │
└─────────────────────────────────────────────────────────────┘
                              │
┌─────────────────────────────────────────────────────────────┐
│                   MorphDB.Service                           │
│  SchemaController    DataController    BatchController      │
│  GraphQL Engine      OData Provider    SignalR Hub          │
│                                                             │
│  ┌───────────────────────────────────────────────────────┐  │
│  │            Table-Agnostic Endpoints                   │  │
│  │  • A new table needs no new endpoint code            │  │
│  │  • GraphQL needs no per-table schema change          │  │
│  │  • Auto-refresh OData $metadata                      │  │
│  └───────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────┘
                              │
┌─────────────────────────────────────────────────────────────┐
│                    MorphDB.Npgsql                           │
│  PostgresSchemaManager    PostgresDataService               │
│  MorphQueryBuilder        MetadataRepository                │
└─────────────────────────────────────────────────────────────┘
                              │
┌─────────────────────────────────────────────────────────────┐
│                     MorphDB.Core                            │
│  ISchemaManager    IMorphDataService    IMorphQueryBuilder  │
│  TableMetadata     ColumnMetadata       RelationMetadata    │
└─────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────┐
│                      PostgreSQL                             │
│                                                             │
│  morphdb schema (System Tables)                             │
│  ├── _morph_tables     Table metadata and mapping           │
│  ├── _morph_columns    Column definitions and types         │
│  ├── _morph_relations  Foreign key relationships            │
│  ├── _morph_indexes    Index definitions                    │
│  └── _morph_changelog  DDL change history                   │
│                                                             │
│  project_{id} schema (User Data)                             │
│  └── tbl_* (hash-based table names)                         │
└─────────────────────────────────────────────────────────────┘
```

## Core Concept: Logical vs Physical Names

All user-facing operations use **logical names**. MorphDB translates them to **physical hash-based names** internally.

```
User Query                    Physical Execution
─────────────────────────     ─────────────────────────
SELECT * FROM customers   →   SELECT * FROM tbl_a7f3b2c1
WHERE email = '...'           WHERE col_e9d8c7b6 = '...'
```

Benefits:
- Column/table renames don't require data migration
- Physical names are stable identifiers
- Logical names can change freely

## Layer Responsibilities

### MorphDB.Core
- Interface definitions (`ISchemaManager`, `IMorphDataService`)
- Domain models (`TableMetadata`, `ColumnMetadata`)
- Data type abstractions (`MorphDataType`)

### MorphDB.Npgsql
- PostgreSQL-specific DDL/DML operations
- Name hashing (`Sha256NameHasher`)
- Query building with logical→physical translation
- Advisory locking for concurrent schema changes

### MorphDB.Service
- REST API controllers
- GraphQL (HotChocolate) over a table-agnostic schema — tables and rows are served as data by
  resolvers that read metadata per request, so creating a table changes no GraphQL type
- OData with dynamic EDM model
- SignalR hub for real-time sync

## Data Flow

### Schema Creation
```
POST /api/schema/tables
    │
    ▼
SchemaController
    │
    ▼
PostgresSchemaManager
    │
    ├─→ Generate hash-based physical name
    ├─→ Create physical table in PostgreSQL
    └─→ Store mapping in _morph_tables/_morph_columns
```

### Data Query
```
GET /api/data/customers?filter=grade:VIP
    │
    ▼
DataController
    │
    ▼
PostgresDataService
    │
    ├─→ Resolve "customers" → tbl_a7f3b2c1
    ├─→ Build SQL with physical names
    ├─→ Execute query
    └─→ Map results back to logical names
```

## Multi-Tenancy

Each project has isolated data in a separate PostgreSQL schema:

```
morphdb (shared)
├── _morph_tables
├── _morph_columns
└── ...

project_abc123 (project-specific)
├── tbl_a7f3b2c1 (customers)
├── tbl_b8c4d5e6 (orders)
└── ...

project_xyz789 (project-specific)
├── tbl_a7f3b2c1 (products)
└── ...
```

## Request Scoping

```
X-Project-Id header → SecurityContextMiddleware → SecurityContext (ambient) → RLS / write pipeline
```

The service carries no authentication of its own — access control belongs to the deployment
(private binding, or an authenticating proxy in front). The project header scopes a request to a
schema namespace via `IProjectContextAccessor`; it is not a credential.

## Virtual Constraint Architecture

Integrity constraints are physically enforced — the database is the final backstop — while the
write pipeline validates the same rules ahead of it, so a caller's mistake answers a clean 4xx
before it ever reaches a physical violation (and a physical violation that does surface is
translated to the same error contract). The one deliberate exception is CHECK: its expressions
live in the logical-name world, so it stays application-layer — materializing it physically would
tie every rename to a constraint rebuild.

### Physical vs Virtual Constraints

| Constraint Type | Physical | Virtual | Rationale |
|-----------------|----------|---------|-----------|
| Primary Key (PK) | ✅ | | Identity and lookups |
| Index | ✅ | | Query performance |
| Foreign Key (FK) | ✅ | | Referential integrity under concurrency; dropping a referenced table requires releasing the relation first (`TABLE_HAS_DEPENDENTS`) |
| NOT NULL | ✅ | | Required values cannot depend on every writer behaving |
| UNIQUE | ✅ | | Uniqueness is a race unless the database enforces it |
| DEFAULT | ✅ | | Applied by DDL; pipeline transformers add context-based values on top |
| CHECK | | ✅ | Expressions reference logical names; kept virtual for rename flexibility |

### Write Pipeline Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                     Write Request                           │
│              (Insert/Update/Delete)                         │
└─────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────┐
│                    WritePipeline                            │
│                                                             │
│  Phase 1: TRANSFORMERS (data modification)                  │
│  ├── DefaultValueApplier     (static/computed defaults)     │
│  ├── TimestampApplier        (_created_at, _updated_at)     │
│  ├── VersionApplier          (_version for optimistic lock) │
│  ├── AuditFieldApplier       (_created_by, _updated_by)     │
│  └── SoftDeleteApplier       (_deleted_at for soft delete)  │
│                                                             │
│  Phase 2: VALIDATORS (constraint validation, ahead of DDL)  │
│  ├── RequiredValidator       (NOT NULL, friendly 400)       │
│  ├── UniqueValidator         (with conditional support)     │
│  ├── ForeignKeyValidator     (reference existence check)    │
│  └── CheckValidator          (expression evaluation)        │
│                                                             │
│  Phase 3: EXECUTOR (database write)                         │
│  └── PostgresWriteExecutor   (actual SQL execution)         │
└─────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────┐
│                      PostgreSQL                             │
└─────────────────────────────────────────────────────────────┘
```

### WriteOptions for Flexibility

```csharp
// Standard write - full validation
WriteOptions.Default

// Bulk import - deferred validation for performance
WriteOptions.BulkImport

// Custom options
new WriteOptions {
    ValidateRequired = true,
    ValidateForeignKeys = false,  // Skip FK checks
    ApplyTimestamps = true,
    ApplyVersion = false
}
```

### Soft Delete Pattern

When `SoftDeleteEnabled = true`:
- DELETE operations become UPDATE (_deleted_at = NOW())
- SELECT queries automatically filter `_deleted_at IS NULL`
- Preserves referential integrity and audit trail

## Data Types

| MorphDB Type | PostgreSQL Type | Description |
|--------------|-----------------|-------------|
| `text` | varchar/text | String |
| `integer` | int2/int4/int8 | Integer |
| `decimal` | numeric(p,s) | Fixed-point |
| `boolean` | boolean | True/False |
| `timestamp` | timestamp | DateTime |
| `uuid` | uuid | UUID |
| `json` | jsonb | JSON data |
| `enum` | enum/lookup | Single selection |
