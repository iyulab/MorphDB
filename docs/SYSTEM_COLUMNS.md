# MorphDB System Columns

## Overview

MorphDB introduces system columns to keep data management consistent while offering runtime schema
flexibility. This document defines their structure, how they are enforced, and the extension
strategy.

## Design Philosophy

### The constraint boundary

Physical constraints are the final backstop for integrity; the write pipeline validates the same
rules in front of them and translates caller mistakes into 4xx (consistent with the 2026-07-22
constitution amendment).

| Enforcement | Criterion | Examples |
|-------------|-----------|----------|
| **Physical** | Data integrity — only the database can guarantee it under concurrency | PK, indexes, FK, NOT NULL, UNIQUE, DEFAULT |
| **Virtual (app layer only)** | The expression must live in logical-name space | CHECK |

What this buys:

- **A kind error surface**: the pipeline answers a 400 naming the column before a physical
  violation is ever reached
- **Rename flexibility**: CHECK expressions are not bound to physical names, so a rename is a
  metadata update and nothing more
- **Bulk-operation tuning**: pipeline validation can be dialed down for mass imports (the physical
  backstop remains)
- **Soft-delete integration**: natural compatibility with application-level deletion

---

## System Column Structure

### Tiers

```
┌─────────────────────────────────────────────────────────────────┐
│  Core (always present)                                          │
│  ─────────────────────                                          │
│  _id, _created_at, _updated_at                                  │
├─────────────────────────────────────────────────────────────────┤
│  Standard (on by default, can be disabled)                      │
│  ─────────────────────────────────────────                      │
│  _version, _created_by, _updated_by                             │
├─────────────────────────────────────────────────────────────────┤
│  Optional (explicit opt-in)                                     │
│  ──────────────────────────                                     │
│  Soft Delete, Ownership, Hierarchy, Source Tracking, Row-State  │
├─────────────────────────────────────────────────────────────────┤
│  Extension (plugin-style) — v1.x and later                      │
│  ─────────────────────────                                      │
│  Workflow, Search, Analytics, ACL, Localization, ...            │
└─────────────────────────────────────────────────────────────────┘
```

---

### Core columns

Included in every table automatically; cannot be disabled.

| Column | Type | Description | Enforcement |
|--------|------|-------------|-------------|
| `_id` | UUID v7 | Primary key, time-ordered | Physical |
| `_created_at` | Timestamp | Creation time, immutable | Physical (DB trigger) |
| `_updated_at` | Timestamp | Last modification time | Physical (DB trigger) |

**Rationale**:
- `_id`: the PK is the heart of PostgreSQL's own optimizations — indexing, JOIN planning
- Timestamps: DB-level triggers stay trustworthy independent of application bugs

---

### Standard columns

On by default; can be explicitly disabled at table creation.

| Column | Type | Description | Enforcement |
|--------|------|-------------|-------------|
| `_version` | Integer | Optimistic-lock version | Physical (column) + virtual (validation logic) |
| `_created_by` | UUID | Creator id | Virtual (injected by the system layer) |
| `_updated_by` | UUID | Last modifier id | Virtual (injected by the system layer) |

**Rationale**:
- `_version`: essential for concurrent-modification detection; the column is physical, the
  validation lives in the system layer
- User audit: the user context exists only at the API layer

---

### Optional columns

Enabled explicitly, per table, where the capability is needed.

#### Soft Delete

| Column | Type | Description |
|--------|------|-------------|
| `_deleted_at` | Timestamp | Deletion time (NULL means active) |
| `_deleted_by` | UUID | Deleter id |

- Every SELECT gets the filter automatically (`_deleted_at IS NULL`)
- An option to include deleted rows is available where needed

#### Ownership

| Column | Type | Description |
|--------|------|-------------|
| `_owner_id` | UUID | Record owner |

- The basis for row-level access control
- Supports automatic owner-based filtering

#### Hierarchy

| Column | Type | Description |
|--------|------|-------------|
| `_parent_id` | UUID | Parent record reference |
| `_sort_order` | Integer | Ordering among siblings |

- Supports tree-shaped data (folders, categories, pages, ...)
- Supports drag-and-drop reordering

#### Source Tracking

| Column | Type | Description |
|--------|------|-------------|
| `_source_id` | Text | Origin id in an external system |

- Identifies the origin when integrating external systems
- Used for upserts and sync conflict resolution

#### Row-State (v0.12.0+)

| Column | Type | Description |
|--------|------|-------------|
| `_row_state` | Enum | Row state: `draft`, `valid`, `error` |
| `_row_errors` | JSONB | Validation error detail (for the `error` state) |

- **draft**: stored, validation skipped (NOT NULL/CHECK not applied)
- **valid**: a complete record; every constraint validated
- **error**: stored, with validation errors

**Scenario**:
```
paste a table → draft insert (no validation) → edit in the UI → finalize
                                                        ↓
                                              valid (pass) / error (fail)
```

**`_row_errors` shape**:
```json
[
  { "column": "email", "error": "required", "message": "email is required" },
  { "column": "age", "error": "check_failed", "message": "violates age > 0" }
]
```

- Supports spreadsheet-style pasting
- Supports column-first input
- Bulk data can land first and validate later, in one pass

---

### Extension columns (v1.x and later)

Registered plugin-style and applied to the tables that need them. Defined ahead of time; built when
demand arrives.

| Extension | Columns | Purpose |
|-----------|---------|---------|
| **Workflow** | `_status`, `_published_at`, `_archived_at` | Content state management |
| **Temporal** | `_effective_from`, `_effective_until`, `_expires_at` | Point-in-time data, TTL |
| **Search** | `_tags`, `_search_vector`, `_embedding` | Search optimization, AI search |
| **Analytics** | `_view_count`, `_last_accessed_at` | Usage statistics |
| **ACL** | `_acl`, `_visibility`, `_shared_with` | Fine-grained access control |
| **Localization** | `_locale`, `_translation_of` | Multi-language support |
| **Compliance** | `_retention_until`, `_data_classification` | Regulatory compliance |

---

## Constraint Enforcement Strategy

### Physical vs virtual

Aligned with the constitution's constraint boundary (2026-07-22): **only CHECK is virtual**; the
integrity constraints are physical, with pipeline validation in front of each translating caller
mistakes into 4xx.

| Constraint / capability | Physical | Virtual (pipeline) | Rationale |
|-------------------------|:--------:|:------------------:|-----------|
| Primary Key | ✓ | | Performance; the core of integrity |
| Foreign Key | ✓ | ✓ (pre-check) | Integrity under concurrency; `TABLE_HAS_DEPENDENTS` is public contract |
| NOT NULL | ✓ | ✓ (pre-check) | The DB backstop holds; the pipeline answers the kinder 400 first |
| UNIQUE | ✓ (partial index over active rows) | ✓ (pre-check) | Soft-deleted tombstones must not occupy names |
| CHECK | | ✓ (sole enforcement) | The expression lives in logical-name space; grammar = `CheckGrammar` (declaration-gated) |
| DEFAULT | ✓ | ✓ (applier) | Context-free defaults are physical; context-bearing ones applied in the pipeline |
| Index | ✓ | (managed) | Performance |
| Cascade | ✓ (relations) | ✓ (table retirement sweep) | Predictability; soft-delete compatible |

### What pipeline-side validation buys

1. **Friendly failures first**: a 400 with the column name, before the DB says it colder
2. **Bulk-operation tuning**: validation can be deferred or reduced for mass imports
3. **Conditional rules**: e.g. uniqueness scoped to non-deleted rows
4. **Cross-table checks**: rules that reference other tables

---

## Recommended Configuration by Use Case

| Use case | Core | Standard | Optional |
|----------|:----:|:--------:|----------|
| **Basic CRUD** | ✓ | ✓ | — |
| **Collaborative documents** | ✓ | ✓ | Soft Delete, Ownership |
| **Hierarchical data** | ✓ | ✓ | Hierarchy |
| **External integration** | ✓ | ✓ | Source Tracking |
| **CMS** | ✓ | ✓ | Soft Delete + Workflow extension |
| **File system** | ✓ | ✓ | Hierarchy, Ownership |

---

## Naming Rules

- Every system column carries the `_` prefix
- System columns keep the same logical and physical name (no hash translation)
- User-defined columns may not use the `_` prefix

Which gives:

- A clear boundary between system and user columns
- Instant identification of system columns in the physical table while debugging
- Easy filtering of system fields in API responses

---

## Implementation Map (v0.12.0+)

| Component | File | Role |
|-----------|------|------|
| TableMetadata | `Core/Models/TableMetadata.cs` | System-column flags |
| SystemColumns | `Core/Models/SystemColumns.cs` | System-column constants and the `RowStateValue` enum |
| IdApplier | `Pipeline/Transformers/IdApplier.cs` | `_id` |
| TimestampApplier | `Pipeline/Transformers/TimestampApplier.cs` | `_created_at`, `_updated_at` |
| VersionApplier | `Pipeline/Transformers/VersionApplier.cs` | `_version` |
| AuditFieldApplier | `Pipeline/Transformers/AuditFieldApplier.cs` | `_created_by`, `_updated_by` |
| SoftDeleteApplier | `Pipeline/Transformers/SoftDeleteApplier.cs` | `_deleted_at` |
| OwnerApplier | `Pipeline/Transformers/OwnerApplier.cs` | `_owner_id` |
| SortOrderApplier | `Pipeline/Transformers/SortOrderApplier.cs` | `_sort_order` |
| RowStateApplier | `Pipeline/Transformers/RowStateApplier.cs` | `_row_state`, `_row_errors` |
| DefaultValueApplier | `Pipeline/Transformers/DefaultValueApplier.cs` | Column defaults |

---

## Future Considerations

1. **Extension registry**: plugin registration and management (v1.x)
2. **Migration path**: data migration when an Optional tier graduates to an Extension
3. **Performance monitoring**: measuring the overhead system columns introduce
4. **API exposure control**: configuring how much of the system columns the API surfaces
