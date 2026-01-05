# MorphDB Roadmap

> **Single Source of Truth**: 모든 개발 작업은 이 로드맵을 기준으로 진행됩니다.
> **범위**: `src/` (Server) + `sdk/` (Client SDKs) + `desk/` (Desktop App)

---

## Version History

| Version | Component | Description | Status |
|---------|-----------|-------------|--------|
| 0.1.0 | src/ | Core functionality | ✅ Complete |
| 0.2.0 | src/ | API layer (GraphQL, OData) | ✅ Complete |
| 0.3.0 | src/ | Real-time features | ✅ Complete |
| 0.4.0 | src/ | Bulk & SDKs | ✅ Complete |
| 0.5.0 | src/ | Production Ready (Beta) | ✅ Complete |
| 0.6.0 | src/ | Enterprise Hardening | ✅ Complete |
| 0.7.0 | src/ | Schema Architecture | ✅ Complete |
| 0.7.5 | src/ | Virtual Constraints | ✅ Complete |
| 0.8.0 | src/ | System Columns + Audit + Rate Limiting | ✅ Complete |
| 0.9.0 | src/ | Organization + SSO | ✅ Complete |
| 0.9.5 | src/ | Philosophy Alignment | ✅ Complete |
| 0.10.0 | src/ | Production Hardening | ✅ Complete |
| **0.11.0** | **src/** | **Admin Dashboard** | ✅ Complete |
| **0.12.0** | **src/** | **Data Features Enhancement** | ✅ Complete |
| **0.13.0** | **sdk/** | **SDK Testing & Stabilization** | ✅ Complete |
| **0.14.0** | **desk/** | **Testing & UX Foundations** | ✅ Complete |
| **0.15.0** | **ALL** | **Cross-Component Integration** | ✅ Complete |
| **0.16.0** | **ALL** | **Documentation & E2E Tests** | 📋 Planned |
| **1.0.0-rc** | **ALL** | **Release Candidate** | 📋 Planned |
| **1.0.0** | **ALL** | **General Availability** | 📋 Planned |

### Data Features (Extended Roadmap - 통합 완료)

> 핵심 데이터 기능은 메인 코드베이스에 이미 통합되어 있습니다.

| Extended Version | Feature | Main Integration | Status |
|------------------|---------|------------------|--------|
| v0.10.x | Views & Computed Columns | ✅ Included | ✅ Complete |
| v0.11.x | Lookup Fields | ✅ Included | ✅ Complete |
| v0.12.x | Rollup Fields | ✅ Included | ✅ Complete |
| v0.13.x | Formula Fields | ✅ Included | ✅ Complete |
| v0.14.x | Aggregation API | ✅ Included | ✅ Complete |
| v0.15.x | Client SDK Aggregation | ✅ Included | ✅ Complete |
| v0.16.x | Materialized Views | → v0.12.0 | ✅ Complete |
| v0.17.x | Advanced Relations | → v0.12.0 | ✅ Complete |

---

## v1.0.0 Release Path

> **철학 문서**: [PHILOSOPHY.md](./PHILOSOPHY.md) 참조
> **철학 검토 결과**: 100% 준수 (v0.9.5에서 4개 개선 항목 완료)
> **통합 범위**: Server (`src/`) + SDKs (`sdk/`) + Desktop App (`desk/`)

```
                          src/ Server 핵심 기능 완성
                                    │
v0.9.5 ──→ v0.10.0 ──→ v0.11.0 ──→ v0.12.0 (Data Features Enhancement)
(완료)      (완료)      (완료)     (Materialized Views, Advanced Relations)
                                    │
                                    ▼
                     ┌──────────────┴──────────────┐
                     │                             │
              sdk/ Python + TS              desk/ Desktop App
                     │                             │
                 v0.13.0                       v0.14.0
              (SDK 테스트/안정화)              (Phase 5 완료)
                     │                             │
                     └──────────────┬──────────────┘
                                    │
                                    ▼
                            v0.15.0 (통합)
                                    │
                            v0.16.0 (문서/E2E)
                                    │
                            v1.0.0-rc
                                    │
                            v1.0.0 GA
```

### Runtime Data Layer 기능 현황

```
✅ 완료된 핵심 기능 (앱 빌더를 위한 런타임 데이터 조작):
├── Linked Records ─────────────────────── FK + Virtual Constraints (관계 동적 정의)
├── Lookup Fields ──────────────────────── Auto-JOIN Expansion (관계 데이터 자동 조회)
├── Rollup Fields ──────────────────────── Subquery Aggregation (1:N 자동 집계)
├── Count Fields ───────────────────────── Rollup with COUNT (관련 레코드 수)
├── Formula Fields ─────────────────────── Expression Parser (계산 필드 런타임 정의)
├── Computed Columns ───────────────────── PostgreSQL GENERATED (저장/가상 계산)
├── Views ──────────────────────────────── Virtual Tables (데이터 뷰 동적 생성)
├── Aggregation API ────────────────────── Server-side GroupBy (복잡 집계 지원)
└── Conditional Rollup ─────────────────── Filtered Aggregation (조건부 집계)

✅ v0.12.0 완료된 기능 (Phase 28: Transaction & Row-State):
├── Cross-Entity Transaction ──────────── 다중 테이블 원자적 작업 ($ref 참조) ✅
├── Row-State (_row_state) ────────────── draft/valid/error 상태 관리 ✅
├── Draft Mode Bulk Insert ────────────── 유효성 검증 지연 (표 붙여넣기 지원) ✅
└── Finalize API ──────────────────────── 상태 전환 + 일괄 검증 ✅

✅ v0.12.0 완료된 기능 (Phase 26-27: Materialized Views + Advanced Relations):
├── Materialized Views ─────────────────── Cached Query Results, CONCURRENTLY refresh ✅
├── Staleness Detection ────────────────── 조인된 테이블 변경 추적 ✅
├── Self-Referential Relations ─────────── IsSelfReferential, MaxHierarchyDepth ✅
├── Many-to-Many Relations ─────────────── Auto Junction Table 생성 ✅
├── Hierarchy Query API ────────────────── HierarchyController (ancestors/descendants/path/siblings) ✅
└── Cycle Detection ────────────────────── WouldCreateCycleAsync, DetectCyclesAsync ✅
```

---

## Current Focus

**Active Version**: v0.16.0 (Documentation & E2E Tests)

### Completed in v0.15.0 (Cross-Component Integration)

| Phase | Task | Status |
|-------|------|--------|
| ALL | docker-compose.test.yml (통합 테스트 환경) | ✅ Complete |
| SDK | Python SDK 통합 테스트 (conftest, schema, data, workflow) | ✅ Complete |
| SDK | TypeScript SDK 통합 테스트 (schema, data, workflow) | ✅ Complete |
| Desk | Desk ↔ Server 통합 테스트 (Playwright E2E) | ✅ Complete |
| Docs | 버전 호환성 매트릭스 (COMPATIBILITY.md) | ✅ Complete |

### Completed in v0.14.0 (Testing & UX Foundations)

| Phase | Task | Status |
|-------|------|--------|
| Desk | Button/Input 컴포넌트 단위 테스트 (71 tests) | ✅ Complete |
| Desk | E2E Critical Path 테스트 (Connection → Navigation) | ✅ Complete |
| Desk | 반응형 레이아웃 (Sidebar collapse, Ctrl+B 단축키) | ✅ Complete |
| Desk | ErrorBoundary 개선 (Route-level, dev mode stack trace) | ✅ Complete |
| Desk | 문서화 (USER_GUIDE.md, KEYBOARD_SHORTCUTS.md 업데이트) | ✅ Complete |

### Completed in v0.13.0 (SDK Testing & Stabilization)

| Phase | Task | Status |
|-------|------|--------|
| SDK | Python SDK 테스트 인프라 (pytest, conftest.py) | ✅ Complete |
| SDK | Python SDK 단위 테스트 (schema, data, bulk, webhook, client) | ✅ Complete |
| SDK | TypeScript SDK 테스트 인프라 (vitest 설정) | ✅ Complete |
| SDK | TypeScript SDK 단위 테스트 (http, client, schema, data, bulk, webhook) | ✅ Complete |
| SDK | SDK 버전 동기화 (0.1.0 → 0.13.0) | ✅ Complete |

### Completed in v0.12.0 (Data Features Enhancement)

| Phase | Task | Status |
|-------|------|--------|
| 26 | Materialized View DDL/Refresh (PostgresViewManager) | ✅ Complete |
| 26 | CONCURRENTLY refresh with UNIQUE INDEX | ✅ Complete |
| 26 | Staleness detection (joined table tracking) | ✅ Complete |
| 27 | Self-Referential Relations (IsSelfReferential) | ✅ Complete |
| 27 | Many-to-Many Junction Table Auto-Generation | ✅ Complete |
| 27 | Hierarchy Query API (HierarchyController) | ✅ Complete |
| 27 | Cycle Detection (CTE-based) | ✅ Complete |
| 28 | Cross-Entity Transaction API | ✅ Complete |
| 28 | $ref Reference System | ✅ Complete |
| 28 | _row_state/_row_errors System Columns | ✅ Complete |
| 28 | Draft Mode Bulk Insert + Finalize API | ✅ Complete |

### Completed in v0.11.0 (Admin Dashboard)

| Phase | Task | Status |
|-------|------|--------|
| 25 | Admin API Foundation (시스템 상태, 테넌트 조회) | ✅ Complete |
| 25 | Admin Authorization (Admin 역할 기반 접근 제어) | ✅ Complete |
| 25 | Metrics Dashboard API (쿼리/연결 통계 집계) | ✅ Complete |
| 25 | Schema Administration API (스키마 개요/매핑 조회) | ✅ Complete |
| 25 | Activity & Audit API (활동 로그/감사 조회) | ✅ Complete |
| 25 | Static Admin Dashboard (정적 HTML + JS) | ✅ Complete |

### v0.11.0 Completed Tasks (Admin Dashboard - src/)

> Admin API 및 정적 대시보드 완료

| Priority | Task | Status | Notes |
|----------|------|--------|-------|
| 🔴 Critical | Admin API Foundation | ✅ Complete | `AdminController` - /api/admin/system/*, /api/admin/tenants/* |
| 🔴 Critical | Admin Authorization | ✅ Complete | Program.cs Admin policy - role/scope/system claims |
| 🟡 High | Metrics Dashboard API | ✅ Complete | /api/admin/metrics/* - queries, connections, performance |
| 🟡 High | Schema Administration API | ✅ Complete | /api/admin/schema/* - overview, tenant tables, mappings |
| 🟡 High | Activity & Audit API | ✅ Complete | /api/admin/activity/* - logs, stats, cross-tenant |
| 🟢 Normal | Static Admin Dashboard | ✅ Complete | wwwroot/admin/index.html - 단일 파일 대시보드 |

---

## v0.12.0 Data Features Enhancement (src/)

> 고급 데이터 모델링 기능 추가 - SDK/Desktop 이전에 서버 핵심 기능 완성

### Phase 26: Materialized Views ✅

| Priority | Task | Status | Notes |
|----------|------|--------|-------|
| 🔴 Critical | Materialized View 메타데이터 모델 | ✅ Complete | IViewManager, ViewMetadata with IsMaterialized |
| 🔴 Critical | Materialized View DDL 구현 | ✅ Complete | CREATE/DROP MATERIALIZED VIEW in PostgresViewManager |
| 🟡 High | Refresh Strategy (Manual/Scheduled) | ✅ Complete | RefreshMaterializedViewAsync with CONCURRENTLY option |
| 🟡 High | Concurrent Refresh 지원 | ✅ Complete | UNIQUE INDEX 자동 생성, CONCURRENTLY 지원 |
| 🟡 High | Staleness Detection | ✅ Complete | IsMaterializedViewStaleAsync - 조인된 테이블 변경 추적 |
| 🟢 Normal | Materialized View API 엔드포인트 | ✅ Complete | ViewController: /api/schema/views/* |

### Phase 27: Advanced Relations ✅

| Priority | Task | Status | Notes |
|----------|------|--------|-------|
| 🔴 Critical | Self-Referential Relation 지원 | ✅ Complete | IsSelfReferential, MaxHierarchyDepth in RelationMetadata |
| 🔴 Critical | Many-to-Many 자동 Junction Table | ✅ Complete | CreateRelationAsync auto-generates junction table |
| 🟡 High | Hierarchy Query API | ✅ Complete | HierarchyController with ancestors/descendants/path/siblings/subtree |
| 🟡 High | Cycle Detection | ✅ Complete | WouldCreateCycleAsync, DetectCyclesAsync using recursive CTE |
| 🟢 Normal | Hierarchy Service DI | ✅ Complete | PostgresHierarchyQueryService registered in ServiceCollectionExtensions |

### Phase 28: Transaction & Row-State ✅

| Priority | Task | Status | Notes |
|----------|------|--------|-------|
| 🔴 Critical | Cross-Entity Transaction API | ✅ Complete | `POST /api/batch/transaction` - 다중 테이블 원자적 작업 |
| 🔴 Critical | $ref 참조 시스템 | ✅ Complete | 이전 operation 결과 참조 (`$order._id`) |
| 🔴 Critical | `_row_state` 시스템 컬럼 | ✅ Complete | draft/valid/error 상태 관리 |
| 🟡 High | `_row_errors` 컬럼 | ✅ Complete | JSONB - 유효성 오류 상세 저장 |
| 🟡 High | Draft 모드 Bulk Insert | ✅ Complete | `?mode=draft` - 유효성 검증 건너뜀 |
| 🟡 High | Finalize API | ✅ Complete | `PATCH /{id}/finalize` - 유효성 검증 후 상태 전환 |
| 🟢 Normal | Row-State 쿼리 필터 | ✅ Complete | `?state=valid` - 상태별 조회 |

**Cross-Entity Transaction 예시**:
```json
{
  "operations": [
    { "method": "INSERT", "table": "orders", "data": {...}, "ref": "$order" },
    { "method": "INSERT", "table": "order_items", "data": { "order_id": "$order._id", ... } }
  ]
}
```

**Row-State 활용 시나리오**:
```
표 붙여넣기 → Draft Insert (검증 없음) → UI 편집 → Finalize (검증 실행)
                                                    ↓
                                          valid (성공) / error (실패+오류상세)
```

### Difficulty: ★★★☆☆ (중급)

> **Prerequisites**: v0.11.0 (Admin Dashboard) 완료
> **Goals**: 앱 빌더를 위한 고급 데이터 모델링 기능 제공
> **후속 작업**: v0.13.0 SDK → v0.14.0 Desktop → v0.15.0 통합

---

### Completed in Previous Version (v0.10.0)

| Phase | Task | Status |
|-------|------|--------|
| 24 | Slow Query 로깅 (QueryDiagnostics) | ✅ Complete |
| 24 | Connection Pool 메트릭 엔드포인트 | ✅ Complete |
| 24 | Backup 자동 검증 (VerifyBackupAsync) | ✅ Complete |
| 24 | Graceful Shutdown (Request Draining) | ✅ Complete |
| 24 | Production Hardening 테스트 (35+ tests) | ✅ Complete |

### v0.10.0 Completed Tasks (Production Hardening)

> 프로덕션 환경 안정성 및 운영 준비 완료

| Priority | Task | Status | Notes |
|----------|------|--------|-------|
| 🟡 High | Slow Query 로깅 | ✅ Complete | `QueryDiagnosticsService`, 임계값 기반 감지 (default 1s), P95/P99 통계 |
| 🟡 High | Connection Pool 모니터링 | ✅ Complete | `DiagnosticsController`, 헬스체크 + /metrics (OpenTelemetry) |
| 🔴 Critical | Backup 자동 검증 | ✅ Complete | `VerifyBackupAsync`, 체크섬/압축해제 검증 |
| 🟢 Normal | Graceful Shutdown | ✅ Complete | `GracefulShutdownService`, request draining 지원 |
| 🟢 Normal | Production Hardening 테스트 | ✅ Complete | GracefulShutdownServiceTests, QueryDiagnosticsServiceTests, QueryExecutionScopeTests |

---

### Completed in v0.9.5 (Philosophy Alignment)

| Phase | Task | Status |
|-------|------|--------|
| 23 | 감사 로그 PII 마스킹 구현 | ✅ Complete |
| 23 | API 응답 물리명 노출 검증 및 수정 | ✅ Complete |
| 23 | CHECK 표현식 AND/OR 복합 지원 확장 | ✅ Complete |
| 23 | WriteOptions 문서화 (API.md) | ✅ Complete |
| 23 | 철학 준수 자동 검증 테스트 (21 tests) | ✅ Complete |

### v0.9.5 Completed Tasks (Philosophy Alignment)

> 철학 검토에서 발견된 미비점 해결 완료 (91% → 100%)

| Priority | Task | Status | Notes |
|----------|------|--------|-------|
| 🔴 Critical | 감사 로그 민감정보 마스킹 | ✅ Complete | `PiiMaskingFilter` 구현 (email, phone, name, address 등) |
| 🔴 Critical | API 응답 물리명 노출 검증 | ✅ Complete | DynamicQuery.cs, ApiModels.cs 수정 |
| 🟡 High | CHECK 표현식 AND/OR 지원 | ✅ Complete | 복합/중첩 표현식 파싱 (CheckValidator.cs) |
| 🟡 High | 제약 검증 기본값 문서화 | ✅ Complete | API.md WriteOptions 섹션 추가 |
| 🟢 Normal | 철학 준수 자동 검증 테스트 | ✅ Complete | PhilosophyComplianceTests.cs (21 tests) |

---

## Phase Details

### ✅ Completed Phases

<details>
<summary><strong>v0.1.0 - Core Foundation</strong></summary>

| Phase | Description | Key Files |
|-------|-------------|-----------|
| 0 | **Foundation** - Project structure, Core abstractions | `MorphDB.Core/` |
| 1 | **Schema Management** - DDL operations, metadata tables | `PostgresSchemaManager.cs` |
| 2 | **Data Service** - CRUD operations, DML | `PostgresDataService.cs` |
| 3 | **Query Engine** - Filtering, sorting, pagination | `MorphQueryBuilder.cs` |

</details>

<details>
<summary><strong>v0.2.0 - API Layer</strong></summary>

| Phase | Description | Key Files |
|-------|-------------|-----------|
| 4 | **Advanced Query** - SqlKata-based complex queries | `MorphQueryBuilder.cs` |
| 5 | **GraphQL** - HotChocolate dynamic schema | `GraphQL/` |
| 6 | **OData** - v4 protocol support | `OData/` |

</details>

<details>
<summary><strong>v0.3.0 - Real-time</strong></summary>

| Phase | Description | Key Files |
|-------|-------------|-----------|
| 7 | **Real-time** - SignalR WebSocket hub | `Realtime/MorphHub.cs`, `PostgresChangeListener.cs` |
| 8 | **Webhook** - Event subscriptions | `Webhook/` |

**Implementation Details**:
- SignalR Hub at `/hubs/morph`
- PostgreSQL LISTEN/NOTIFY for change detection
- Trigger-based detection for all changes
- Group-based routing: `table:{tenantId}:{tableName}`

</details>

<details>
<summary><strong>v0.4.0 - Bulk & SDKs</strong></summary>

| Phase | Description | Key Files |
|-------|-------------|-----------|
| 9 | **Bulk Operations** - Import/Export (CSV, JSON, XLSX) | `PostgresBulkOperationService.cs` |
| 10 | **Client SDKs** - .NET, TypeScript, Python | `sdk/`, `MorphDB.Client/` |

</details>

<details>
<summary><strong>v0.5.0 - Production Ready</strong></summary>

| Phase | Description | Key Files |
|-------|-------------|-----------|
| 11 | **Security** - API Keys, JWT, RLS | `ApiKeyAuthenticationHandler.cs` |
| 12 | **Deployment** - Docker, Kubernetes, Observability | `docker-compose.yml`, `k8s/` |

</details>

<details>
<summary><strong>v0.6.0 - Enterprise Hardening</strong></summary>

| Phase | Description | Key Files |
|-------|-------------|-----------|
| 13 | **Encryption** - Data encryption, key rotation | `Encryption/` |
| 14 | **Query Builder** - JOIN completion | `MorphQueryBuilder.cs` |
| 15 | **Webhook Reliability** - DLQ, retry logic | `Webhook/` |
| 16 | **Performance** - Caching optimization | `Caching/` |

</details>

<details>
<summary><strong>v0.7.0 - Schema Architecture</strong></summary>

| Phase | Description | Key Files |
|-------|-------------|-----------|
| 17 | **Schema Separation** - Layer isolation | Multiple |
| 18 | **Migration** - Schema provisioning | `Migrations/` |

</details>

<details>
<summary><strong>v0.7.5 - Virtual Constraints</strong></summary>

| Phase | Description | Key Files |
|-------|-------------|-----------|
| 18.5 | **Virtual Constraints** - Application-layer constraint enforcement | `Pipeline/` |

**Architecture**:
```
Write Request → Transformers → Validators → Executor → PostgreSQL
```

| Constraint | Physical | Virtual | Rationale |
|------------|----------|---------|-----------|
| Primary Key | ✅ | | Performance-critical |
| Index | ✅ | | Query performance |
| Foreign Key | | ✅ | Schema flexibility |
| NOT NULL | | ✅ | Configurable per-column |
| UNIQUE | | ✅ | Application-layer check |
| CHECK | | ✅ | Expression evaluation |
| DEFAULT | | ✅ | Context-based values |

**Components**:
- **Transformers**: DefaultValueApplier, TimestampApplier, VersionApplier, AuditFieldApplier, SoftDeleteApplier
- **Validators**: RequiredValidator, UniqueValidator, ForeignKeyValidator, CheckValidator
- **Executor**: PostgresWriteExecutor

</details>

---

### ✅ Complete: v0.8.0 (Audit + Rate Limiting + System Columns)

| Phase | Description | Status | Tasks |
|-------|-------------|--------|-------|
| 18.6 | **System Columns** | ✅ Complete | Core/Standard/Optional layers |
| 19 | **Audit Logging** | ✅ Complete | API integration tests, middleware |
| 20 | **Rate Limiting** | ✅ Complete | Quota API tests, rate limit headers |

#### Phase 18.6: System Columns Architecture ✅

**Goal**: 4계층 시스템 컬럼 구조로 일관된 데이터 관리 제공

**Design Document**: `docs/SYSTEM_COLUMNS.md`

| Layer | Columns | Description | Status |
|-------|---------|-------------|--------|
| Core | `_id`, `_created_at`, `_updated_at` | 모든 테이블 필수, 비활성화 불가 | ✅ |
| Standard | `_version`, `_created_by`, `_updated_by` | 기본 활성화, 비활성화 가능 | ✅ |
| Optional | Soft Delete, Ownership, Sort Order, Source | 명시적 활성화 필요 | ✅ |
| Extension | Workflow, Search, Analytics, ACL 등 | 플러그인 방식 확장 (v1.x) | 📋 |

| Task | Priority | Status | Notes |
|------|----------|--------|-------|
| System columns design doc 작성 | 🔴 Critical | ✅ | `docs/SYSTEM_COLUMNS.md` |
| Core 컬럼 자동 생성 (DdlBuilder) | 🔴 Critical | ✅ | `_id` UUID v7, timestamps |
| Standard 컬럼 옵션화 | 🟡 High | ✅ | TableMetadata.SystemColumnOptions |
| SystemColumnOptions API 노출 | 🟡 High | ✅ | CreateTableRequest 확장 |
| Optional 컬럼 Transformer 추가 | 🟢 Normal | ✅ | IdApplier, OwnerApplier, SortOrderApplier |
| `_` prefix 검증 (사용자 컬럼 차단) | 🟡 High | ✅ | ColumnMetadata validation |
| 통합 테스트 및 API 호환성 | 🟡 High | ✅ | 330/333 tests passing |

**Physical vs Virtual 처리**:
| Column | Physical | Virtual | Rationale |
|--------|:--------:|:-------:|-----------|
| `_id` | ✓ | | PK, 인덱스 최적화 핵심 |
| `_created_at`, `_updated_at` | ✓ | | DB 트리거로 신뢰성 보장 |
| `_version` | ✓ (컬럼) | ✓ (검증) | 동시성 제어 |
| `_created_by`, `_updated_by` | | ✓ | API 컨텍스트 의존 |
| `_deleted_at`, `_deleted_by` | | ✓ | Soft delete 연동 |
| `_owner_id`, `_sort_order` 등 | | ✓ | 비즈니스 로직 의존 |

**Key Implementation Details**:
- **Core columns**: Auto-generated in `PostgresSchemaManager.CreateTableAsync()` via `DdlBuilder`
- **Transformers**: `IdApplier`, `TimestampApplier`, `VersionApplier`, `AuditFieldApplier`, `OwnerApplier`, `SortOrderApplier`
- **API Models**: `SystemColumnOptions` in `CreateTableApiRequest`, response includes system column config
- **GraphQL/OData**: Dynamic schema generation respects `_id` as primary key

---

#### Phase 19: Audit Logging ✅

**Goal**: Comprehensive audit trail for compliance and debugging

| Task | Priority | Status | Notes |
|------|----------|--------|-------|
| Audit log schema design | 🔴 Critical | ✅ | `_audit_logs` in system schema |
| Audit interceptor middleware | 🔴 Critical | ✅ | `AuditMiddleware`, async queue |
| Async audit writer service | 🟡 High | ✅ | Channel-based batch writes |
| Audit log query API | 🟡 High | ✅ | Time range, actor, event type filters |
| Integration tests | 🟡 High | ✅ | `AuditApiTests.cs` - 17 tests |
| Log retention policies | 🟢 Normal | 📋 | Tier-based (7d/30d/90d/1y) |

**Audit Event Schema** (from Enterprise Research):
```json
{
  "id": "uuid-v7",
  "timestamp": "ISO8601",
  "event_type": "schema.modified",
  "event_category": "data|auth|admin",
  "severity": "info|warning|error|critical",
  "actor": { "type": "user|api_key|system", "id": "uuid", "ip": "..." },
  "resource": { "type": "schema|collection|document", "id": "uuid", "name": "..." },
  "action": { "method": "POST|PUT|DELETE", "changes": { "before": {}, "after": {} } },
  "result": { "status": "success|failure", "error_code": null }
}
```

#### Phase 20: Rate Limiting ✅

**Goal**: Protect API from abuse and ensure fair usage

| Task | Priority | Status | Notes |
|------|----------|--------|-------|
| Token bucket algorithm implementation | 🔴 Critical | ✅ | `MemoryRateLimiter` |
| Rate limit middleware | 🔴 Critical | ✅ | Per-tenant, per-endpoint |
| X-RateLimit-* response headers | 🟡 High | ✅ | Standard headers |
| Quota status API endpoint | 🟡 High | ✅ | `GET /api/projects/{id}/quota` |
| Integration tests | 🟡 High | ✅ | `QuotaApiTests.cs` - 14 tests |
| Dashboard integration | 🟢 Normal | 📋 | Usage visualization |

**Rate Limit Tiers** (from Enterprise Research):
| Tier | RPS | Daily | Burst |
|------|-----|-------|-------|
| Free | 10 | 10K | 20 |
| Pro | 100 | 1M | 200 |
| Team | 500 | 10M | 1000 |
| Enterprise | Custom | Unlimited | Custom |

---

### ✅ Complete: v0.9.0 (Organization + SSO)

| Phase | Description | Status | Tasks |
|-------|-------------|--------|-------|
| 21 | **Organization** | ✅ Complete | Team management, roles, hierarchy |
| 22 | **SSO Integration** | ✅ Complete | OIDC provider support |

#### Phase 21: Organization Management ✅

**Goal**: Multi-tenant organization hierarchy with RBAC

| Task | Priority | Status | Notes |
|------|----------|--------|-------|
| Organization entity and API | 🔴 Critical | ✅ | CRUD, stats, slug-based lookup |
| Project hierarchy (Org → Projects) | 🔴 Critical | ✅ | OrganizationId in Project |
| RBAC roles | 🔴 Critical | ✅ | Owner/Admin/Member for Org, Admin/Developer/Viewer for Project |
| Team member management | 🟡 High | ✅ | Add, update role, remove members |
| Team member invitation flow | 🟢 Normal | ✅ | Create, revoke, list invitations |
| Integration tests | 🟡 High | ✅ | 16 tests in OrganizationApiTests.cs |

**Key Implementation Details**:
- **Organization API**: `/api/organizations/*` with CRUD, members, invitations, stats
- **Repositories**: OrganizationRepository, MembershipRepository (Dapper)
- **RBAC**: PermissionService with cached permissions, role-based access control
- **Controllers**: OrganizationController with [Authorize] attribute

#### Phase 22: SSO Integration ✅

**Goal**: Enterprise SSO with OIDC support

| Task | Priority | Status | Notes |
|------|----------|--------|-------|
| SSO configuration CRUD | 🔴 Critical | ✅ | Create, read, update, delete configs |
| OIDC provider support | 🔴 Critical | ✅ | Generic OIDC, EntraId, Google, Okta, Auth0, Keycloak |
| Provider-specific presets | 🟡 High | ✅ | SsoProviderType enum with 6 providers |
| Claim mappings | 🟡 High | ✅ | Configurable subject, email, name, groups claims |
| Domain restrictions | 🟢 Normal | ✅ | AllowedDomains for email filtering |
| SSO config activation/deactivation | 🟡 High | ✅ | With OIDC discovery validation |
| SSO config testing | 🟡 High | ✅ | Validates OIDC discovery document |
| Integration tests | 🟡 High | ✅ | 19 tests in SsoApiTests.cs |

**Key Implementation Details**:
- **SSO API**: `/api/sso/*` for config management, `/api/sso/login/{orgSlug}` for login flow
- **Repositories**: SsoConfigurationRepository (Dapper, encrypted client secrets)
- **Services**: SsoConfigurationService, SsoAuthenticationService with PKCE flow
- **OIDC Discovery**: Validates authority before activation

**Note**: SAML 2.0 support is deferred to a future version.

---

### ✅ Complete: v0.9.5 (Philosophy Alignment)

| Phase | Description | Status | Tasks |
|-------|-------------|--------|-------|
| 23 | **Philosophy Alignment** | ✅ Complete | PII masking, Physical name fix, CHECK AND/OR, WriteOptions docs, Philosophy tests |

#### Phase 23: Philosophy Alignment ✅

> **목표**: 철학 검토에서 발견된 미비점 해결, 핵심 원칙 100% 준수

**달성**: 철학 준수율 91% → 100%

| Task | Priority | Status | Notes |
|------|----------|--------|-------|
| 감사 로그 PII 마스킹 | 🔴 Critical | ✅ | `PiiMaskingFilter` - email, phone, name, address 마스킹 |
| API 응답 물리명 검증 | 🔴 Critical | ✅ | DynamicQuery.cs, ApiModels.cs 수정 |
| CHECK 표현식 확장 | 🟡 High | ✅ | AND/OR 복합 표현식, 중첩 괄호 지원 |
| WriteOptions 문서화 | 🟡 High | ✅ | API.md에 WriteOptions 섹션 추가 |
| 철학 준수 테스트 | 🟢 Normal | ✅ | PhilosophyComplianceTests.cs (21 tests), CheckValidatorTests.cs (20 tests) |

<details>
<summary>구현 상세</summary>

**PII 마스킹 구현** (`PostgresAuditService.cs`):
```csharp
public class PiiMaskingFilter : IAuditFilter
{
    private static readonly string[] SensitivePatterns =
        ["email", "phone", "name", "address", "ssn", "credit_card", "password"];

    public AuditEvent Filter(AuditEvent evt)
    {
        // Metadata에서 민감 필드 자동 마스킹
        // email: ***@***.com, phone: ***-***-1234
    }
}
```

**물리명 노출 방지 테스트** (`PhilosophyComplianceTests.cs`):
```csharp
[Fact]
public void TableApiResponse_ShouldNotExposePhysicalName()
{
    var response = TableApiResponse.FromMetadata(table);
    var json = JsonSerializer.Serialize(response);

    json.Should().NotContain("tbl_"); // No physical table name
    json.Should().NotContain("col_"); // No physical column name
}
```

**CHECK 표현식 AND/OR 지원** (`CheckValidator.cs`):
```csharp
// 지원 표현식 예시:
// "price > 0 AND quantity >= 1"
// "status = 'active' OR status = 'pending'"
// "(price > 0 AND price < 100) OR quantity = 0"
```

</details>

---

### 📋 Planned Phases: v0.12.0 → v1.0.0

---

#### ✅ v0.10.0: Production Hardening (Phase 24) - Complete

> **목표**: 프로덕션 환경 안정성 및 운영 준비 완료
> **컴포넌트**: `src/`

**상태**: ✅ 완료

| Task | Priority | Status | Implementation |
|------|----------|--------|----------------|
| Slow Query 로깅 | 🟡 High | ✅ Complete | `QueryDiagnosticsService`, P95/P99 통계 |
| 연결 풀 모니터링 | 🟡 High | ✅ Complete | `DiagnosticsController`, OpenTelemetry |
| 백업 자동 검증 | 🔴 Critical | ✅ Complete | `VerifyBackupAsync`, 체크섬/압축 검증 |
| 그레이스풀 셧다운 | 🟢 Normal | ✅ Complete | `GracefulShutdownService`, request draining |
| Production Hardening 테스트 | 🟢 Normal | ✅ Complete | 35+ 단위 테스트 추가 |

---

#### ✅ v0.11.0: Admin API Layer (Phase 25) - Complete

> **목표**: 관리 API 및 최소 대시보드, 핵심 운영 기능
> **컴포넌트**: `src/`
> **철학 정렬**: Query Console 제거 (철학 위반), React SPA → 정적 HTML (스코프 관리)

**상태**: ✅ 완료

| Task | Priority | Description | Status |
|------|----------|-------------|--------|
| Admin API Foundation | 🔴 Critical | 시스템 상태, 테넌트 조회 API | ✅ Complete |
| Admin Authorization | 🔴 Critical | Admin 역할 기반 접근 제어 | ✅ Complete |
| Metrics Dashboard API | 🟡 High | 쿼리/연결 통계 집계 API | ✅ Complete |
| Schema Administration API | 🟡 High | 스키마 개요/매핑 조회 API | ✅ Complete |
| Activity & Audit API | 🟡 High | 활동 로그/감사 조회 API | ✅ Complete |
| Static Admin Dashboard | 🟢 Normal | 정적 HTML + JS 대시보드 | ✅ Complete |

**철학 정렬 결정**:
- ❌ Query Console 제거: "디버깅용으로도 SQL 콘솔 제공하지 않음" 원칙 준수
- ❌ React SPA 제거: 백엔드 서비스 집중, 스코프 크립 방지
- ✅ API-Only 접근: 프로그래매틱 관리 우선
- ✅ 정적 대시보드: 최소 UI, wwwroot 내장

<details>
<summary>Admin API 엔드포인트</summary>

```
/api/admin
├── /system
│   ├── GET /status         # 시스템 상태 종합
│   └── GET /config         # 설정 조회 (민감정보 마스킹)
├── /tenants
│   ├── GET /               # 테넌트 목록
│   └── GET /{id}/usage     # 테넌트별 사용량
├── /metrics
│   ├── GET /queries        # 쿼리 통계 집계
│   ├── GET /connections    # 연결 풀 상태
│   └── GET /performance    # P95/P99 레이턴시
├── /schema
│   ├── GET /overview       # 전체 스키마 개요
│   ├── GET /tables/{tenant}  # 테넌트별 테이블
│   ├── GET /mappings       # Logical-Physical 매핑
│   └── GET /changelog      # DDL 변경 이력
└── /activity
    ├── GET /recent         # 최근 활동 로그
    └── GET /search         # 활동 검색
```

</details>

<details>
<summary>정적 대시보드 구조</summary>

```
wwwroot/admin/
└── index.html          # 메인 대시보드 (단일 파일, CSS/JS 내장)
```

</details>

---

#### 📋 v0.12.0: Data Features Enhancement (Phase 26-28)

> **목표**: Materialized Views, Advanced Relations, Transaction & Row-State 구현 - 서버 핵심 기능 완성
> **컴포넌트**: `src/`
> **기간**: 3 주
> **난이도**: ★★★☆☆ (중급)

| Task | Priority | Description | Acceptance Criteria |
|------|----------|-------------|---------------------|
| Materialized View DDL | 🔴 Critical | CREATE MATERIALIZED VIEW 지원 | 물리명 변환 동작 |
| Materialized View API | 🔴 Critical | CRUD 및 Refresh API | /api/schema/materialized-views/* |
| Refresh Scheduling | 🟡 High | Manual/Scheduled 새로고침 | Background job 통합 |
| Concurrent Refresh | 🟡 High | 무중단 새로고침 지원 | UNIQUE INDEX 자동 생성 |
| Staleness Detection | 🟡 High | 기반 테이블 변경 감지 | _morph_views.is_stale 컬럼 |
| Self-Referential Relations | 🟡 High | 계층 구조 지원 | parent_id 패턴 |
| Many-to-Many 자동화 | 🔴 Critical | Junction Table 자동 생성 | 연결 테이블 관리 |
| Hierarchy Query API | 🟢 Normal | ancestors/descendants 조회 | CTE 기반 재귀 쿼리 |
| Cross-Entity Transaction | 🔴 Critical | 다중 테이블 원자적 작업 | /api/batch/transaction |
| $ref 참조 시스템 | 🔴 Critical | 이전 op 결과 참조 | `$order._id` 패턴 |
| `_row_state` 시스템 컬럼 | 🔴 Critical | draft/valid/error 상태 | Optional 시스템 컬럼 |
| Draft 모드 Bulk Insert | 🟡 High | 유효성 검증 건너뜀 | `?mode=draft` |
| Finalize API | 🟡 High | 상태 전환 + 검증 | `PATCH /{id}/finalize` |
| Data Features 통합 테스트 | 🟢 Normal | 전체 기능 검증 | E2E 테스트 통과 |

**Materialized Views 아키텍처**:
```yaml
materialized_view_schema:
  view_id: UUID
  definition: ViewDefinition
  refresh_policy:
    type: "manual" | "scheduled" | "concurrent"
    schedule: "0 * * * *"  # cron (scheduled)
  last_refreshed: timestamp
  is_stale: boolean
```

**Advanced Relations 패턴**:
```yaml
relation_patterns:
  self_referential:
    - parent_id: 동일 테이블 FK
    - CTE로 ancestors/descendants 조회
    - 순환 참조 방지 로직

  many_to_many:
    - Junction table 자동 생성: {table1}_{table2}_link
    - 양방향 Lookup/Rollup 지원
    - Cascade delete 옵션
```

**Transaction & Row-State 아키텍처**:
```yaml
cross_entity_transaction:
  endpoint: POST /api/batch/transaction
  features:
    - 단일 DB 트랜잭션 래핑
    - $ref 참조로 이전 결과 사용
    - 전체 성공 or 전체 롤백

row_state_system:
  columns:
    _row_state: "draft | valid | error"
    _row_errors: "JSONB - 유효성 오류 상세"
  workflow:
    - Draft Insert (검증 스킵)
    - UI 편집
    - Finalize (검증 실행 → valid/error)
  use_cases:
    - 스프레드시트 붙여넣기
    - 컬럼 순서 입력
    - 대량 데이터 임시 저장
```

---

#### ✅ v0.13.0: SDK Testing & Stabilization - Complete

> **목표**: Python/TypeScript SDK 기능 검증 및 테스트 완료
> **컴포넌트**: `sdk/`
> **상태**: ✅ 완료

| Task | Priority | Description | Status |
|------|----------|-------------|--------|
| Python SDK 단위 테스트 | 🔴 Critical | pytest 기반 테스트 스위트 | ✅ Complete |
| TypeScript SDK 단위 테스트 | 🔴 Critical | vitest 기반 테스트 | ✅ Complete |
| Python SDK 통합 테스트 | 🟡 High | 실제 서버 연동 테스트 | 📋 v0.15.0 통합 단계로 이동 |
| TypeScript SDK 통합 테스트 | 🟡 High | 실제 서버 연동 테스트 | 📋 v0.15.0 통합 단계로 이동 |
| SDK API 버전 호환성 | 🟡 High | v0.12.0 서버와 호환 확인 | ✅ Complete |
| SDK README 업데이트 | 🟢 Normal | 최신 API 반영 | ✅ Complete |
| SDK 패키지 버전 동기화 | 🟢 Normal | package.json/pyproject.toml 버전 | ✅ Complete (0.13.0) |

**구현 상세**:
- **Python SDK**: `sdk/python/tests/` - conftest.py, test_client.py, test_schema_client.py, test_data_client.py, test_webhook_client.py, test_bulk_client.py
- **TypeScript SDK**: `sdk/typescript/tests/` - test-utils.ts, client.test.ts, schema.test.ts, data.test.ts, webhook.test.ts, bulk.test.ts, http.test.ts
- **테스트 프레임워크**: Python (pytest + pytest-asyncio), TypeScript (vitest)
- **통합 테스트**: v0.15.0 Cross-Component Integration 단계에서 실제 서버 연동 테스트 수행 예정

**테스트 범위**:
```yaml
sdk_test_coverage:
  schema_client:
    - getTables, getTable, createTable, deleteTable
    - addColumn, alterColumn, dropColumn

  data_client:
    - query, getById, insert, update, delete
    - batch operations (insertMany, deleteMany)

  bulk_client:
    - importCsv, importJson
    - exportCsv, exportJson, exportXlsx
    - job status tracking

  realtime_client:
    - subscribe, unsubscribe
    - connection management

  webhook_client:
    - CRUD operations
    - delivery tracking
```

---

#### ✅ v0.14.0: Testing & UX Foundations - Complete

> **목표**: desk/ 앱 테스트 및 UX 기반 완료
> **컴포넌트**: `desk/`
> **상태**: ✅ 완료

| Task | Priority | Description | Status |
|------|----------|-------------|--------|
| 핵심 컴포넌트 단위 테스트 | 🟡 High | Button, Input 테스트 (71 tests) | ✅ Complete |
| E2E Critical Path 테스트 | 🔴 Critical | Connection → Navigation → Settings | ✅ Complete |
| 반응형 레이아웃 | 🟡 High | Sidebar collapse, Ctrl+B 단축키 | ✅ Complete |
| Error Boundary 개선 | 🟡 High | Route-level 에러 처리, dev mode 상세 | ✅ Complete |
| 문서화 | 🟢 Normal | USER_GUIDE.md, KEYBOARD_SHORTCUTS.md | ✅ Complete |

**v0.14.0 완료된 항목**:
- ✅ Dark/Light 테마 토글 (시스템 설정 연동)
- ✅ Command Palette (Cmd/Ctrl+K)
- ✅ 키보드 단축키 (Ctrl+B 사이드바 토글 추가)
- ✅ Vitest 단위 테스트 (71 tests - Button, Input 컴포넌트)
- ✅ Playwright E2E Critical Path 테스트
- ✅ Toast 알림 시스템
- ✅ API 타입 중앙화 및 필드명 정렬
- ✅ 반응형 사이드바 (layoutStore, Tooltip 컴포넌트)
- ✅ Route-level ErrorBoundary (ComponentErrorBoundary, InlineError)
- ✅ USER_GUIDE.md 생성
- ✅ KEYBOARD_SHORTCUTS.md 업데이트

---

#### ✅ v0.15.0: Cross-Component Integration - Complete

> **목표**: src/, sdk/, desk/ 간 통합 테스트 및 호환성 검증
> **컴포넌트**: `ALL (src/ + sdk/ + desk/)`
> **상태**: ✅ 완료

| Task | Priority | Description | Status |
|------|----------|-------------|--------|
| SDK ↔ Server 통합 테스트 | 🔴 Critical | Python/TS SDK가 서버와 완전 호환 | ✅ Complete |
| Desk ↔ Server 통합 테스트 | 🔴 Critical | desk 앱이 서버 API 완전 호환 | ✅ Complete |
| 통합 테스트 환경 구성 | 🔴 Critical | docker-compose.test.yml | ✅ Complete |
| 버전 호환성 매트릭스 | 🟢 Normal | 컴포넌트 간 지원 버전 문서화 | ✅ Complete |

**구현 상세**:
- **통합 테스트 환경**: `docker-compose.test.yml` - API 5000, PostgreSQL 5433
- **Python SDK**: `sdk/python/tests/integration/` - conftest.py, test_schema_integration.py, test_data_integration.py, test_workflow_integration.py
- **TypeScript SDK**: `sdk/typescript/tests/integration/` - test-utils.ts, schema.integration.test.ts, data.integration.test.ts, workflow.integration.test.ts
- **Desk**: `desk/e2e/integration/` - server.integration.spec.ts (Connection, Schema, Data, Real-time, Error handling)
- **호환성 문서**: `docs/COMPATIBILITY.md` - 버전 매트릭스, 기능 호환성, 업그레이드 가이드

**통합 테스트 시나리오**:
```yaml
integration_scenarios:
  full_workflow:
    - SDK(Python): Create table via API
    - desk: Verify table appears in UI
    - SDK(TypeScript): Insert 1000 rows
    - desk: Query and display data
    - Server: Verify audit logs

  realtime_sync:
    - desk: Subscribe to table changes
    - SDK(Python): Insert record
    - desk: Verify real-time notification
    - SDK(TypeScript): Update record
    - desk: Verify updated data display

  bulk_operations:
    - desk: Export 10K rows to CSV
    - SDK(Python): Import CSV back
    - desk: Verify row count
    - Server: Verify job completion
```

---

#### 📋 v0.16.0: Documentation & E2E Tests

> **목표**: 완전한 문서화 및 종단간 테스트 스위트
> **컴포넌트**: `ALL`
> **기간**: 2 주
> **난이도**: ★★☆☆☆ (초급-중급)

| Task | Priority | Description | Acceptance Criteria |
|------|----------|-------------|---------------------|
| API 문서 완성 (OpenAPI 3.1) | 🔴 Critical | 모든 엔드포인트 명세 | 100% 커버리지 |
| SDK 문서 완성 | 🔴 Critical | Python/TypeScript 가이드 | 모든 기능 예제 |
| desk 사용자 가이드 | 🟡 High | 기능별 상세 설명 | 스크린샷 포함 |
| Data Features 문서 | 🟡 High | Lookup/Rollup/Formula 가이드 | 사용 예제 포함 |
| 운영 가이드 | 🟡 High | K8s, Docker Compose 배포 | 프로덕션 설정 |
| 마이그레이션 가이드 | 🟡 High | 0.x → 1.0 업그레이드 경로 | Breaking changes |
| 튜토리얼 | 🟢 Normal | 시작하기 가이드 | 10분 내 첫 테이블 |
| E2E 테스트 스위트 | 🔴 Critical | 전체 사용자 시나리오 | 핵심 플로우 100% |

<details>
<summary>E2E 테스트 시나리오</summary>

```yaml
e2e_scenarios:
  onboarding:
    - Create organization
    - Create project
    - Create first table
    - Insert data via API
    - Query via GraphQL

  enterprise_flow:
    - SSO login
    - Create team member
    - Assign roles
    - Audit log verification

  data_operations:
    - Bulk import (10K rows)
    - Complex query with JOIN
    - Real-time subscription
    - Backup and restore

  sdk_integration:
    - Python SDK full CRUD
    - TypeScript SDK full CRUD
    - Real-time subscriptions
    - Webhook deliveries

  desk_workflows:
    - Connection management
    - Schema management (tables, columns, indexes)
    - Data CRUD operations
    - Import/Export workflows
```

</details>

---

#### 📋 v1.0.0-rc: Release Candidate

> **목표**: 릴리스 준비 완료, 최종 검증
> **컴포넌트**: `ALL`
> **기간**: 1-2 주
> **난이도**: ★★★★☆ (고급)
> **전제 조건**: v0.12.0 (Data Features) + v0.13.0 (SDK) + v0.14.0 (Desktop) + v0.15.0 (통합) + v0.16.0 (문서) 완료

| Task | Priority | Description | Acceptance Criteria |
|------|----------|-------------|---------------------|
| 성능 벤치마크 | 🔴 Critical | 부하 테스트 완료 | 1000 RPS, p99 < 100ms |
| 보안 감사 | 🔴 Critical | 취약점 스캔 및 수정 | Critical/High 0개 |
| 호환성 테스트 | 🟡 High | PostgreSQL 버전 호환 | 14, 15, 16, 17 지원 확인 |
| Data Features 검증 | 🔴 Critical | Lookup/Rollup/Formula 프로덕션 검증 | 모든 기능 안정 |
| Materialized Views 검증 | 🟡 High | Refresh 안정성 확인 | Concurrent refresh 성공 |
| 의존성 감사 | 🟡 High | 라이선스/취약점 확인 | 문제 있는 의존성 제거 |
| SDK 패키지 게시 테스트 | 🟡 High | npm/PyPI 테스트 게시 | 설치 검증 |
| desk 빌드 검증 | 🟡 High | Windows/macOS/Linux 빌드 | 모든 플랫폼 동작 |
| 릴리스 노트 | 🟡 High | 변경사항 문서화 | Breaking changes 명시 |
| 롤백 플랜 | 🟢 Normal | 문제 시 롤백 절차 | 검증된 롤백 스크립트 |

<details>
<summary>성능 벤치마크 기준</summary>

```yaml
performance_targets:
  throughput:
    read: 1000 RPS
    write: 500 RPS

  latency:
    p50: < 10ms
    p95: < 50ms
    p99: < 100ms

  concurrency:
    connections: 100 concurrent

  stress_test:
    duration: 1 hour
    error_rate: < 0.1%
```

</details>

---

#### 📋 v1.0.0: General Availability

> **목표**: 프로덕션 릴리스
> **컴포넌트**: `ALL (src/ + sdk/ + desk/)`

**체크리스트**:

| Category | Component | Requirement | Status |
|----------|-----------|-------------|--------|
| **기능** | src/ | 모든 API 엔드포인트 안정 | 📋 |
| **기능** | src/ | GraphQL/OData/REST 완전 호환 | 📋 |
| **기능** | src/ | 실시간 동기화 안정 | 📋 |
| **보안** | src/ | 인증/인가 완전 구현 | 📋 |
| **보안** | src/ | 감사 로그 완전 기능 | 📋 |
| **보안** | ALL | 보안 취약점 0개 | 📋 |
| **운영** | src/ | 모니터링/알림 구축 | 📋 |
| **운영** | src/ | 백업/복구 검증 완료 | 📋 |
| **운영** | src/ | 그레이스풀 배포 지원 | 📋 |
| **문서** | src/ | API 문서 100% 완성 | 📋 |
| **문서** | ALL | 운영 가이드 완성 | 📋 |
| **문서** | sdk/ | SDK 문서 완성 | 📋 |
| **문서** | desk/ | 사용자 가이드 완성 | 📋 |
| **테스트** | src/ | 단위 테스트 > 80% 커버리지 | 📋 |
| **테스트** | sdk/ | SDK 테스트 > 80% 커버리지 | 📋 |
| **테스트** | desk/ | E2E 테스트 통과 | 📋 |
| **테스트** | ALL | 성능 테스트 통과 | 📋 |
| **철학** | src/ | 물리명 노출 0개 | 📋 |
| **철학** | src/ | 직접 SQL 경로 0개 | 📋 |
| **철학** | src/ | 감사 가능성 100% | 📋 |
| **배포** | sdk/ | npm/PyPI 패키지 게시 | 📋 |
| **배포** | desk/ | GitHub Releases 빌드 | 📋 |

---

### 📅 예상 일정

```
2025 Q1 (완료)
├── v0.9.5  (완료)   ████████████████ Philosophy Alignment ✅
├── v0.10.0 (완료)   ████████████████ Production Hardening ✅ (src/)
└── v0.11.0 (완료)   ████████████████ Admin Dashboard ✅ (src/)
    │
    └── [Data Features 통합 완료] Views, Lookup, Rollup, Formula, Aggregation ✅

2025 Q1-Q2 (현재 ~ 진행 예정)
├── v0.12.0 (완료)   ████████████████ Data Features Enhancement (src/) ✅
│   │                                  - Materialized Views ✅
│   │                                  - Advanced Relations (Self-ref, M:N) ✅
│   │                                  - Hierarchy Query API ✅
│   │                                  - Transaction & Row-State ✅
│   └────────────────────────────────────────────────────────────────────
├── v0.13.0 (완료)   ████████████████ SDK Testing & Stabilization (sdk/) ✅
│   │                                  - Python SDK 테스트 스위트 ✅
│   │                                  - TypeScript SDK 테스트 스위트 ✅
│   │                                  - SDK 버전 동기화 (0.13.0) ✅
│   └────────────────────────────────────────────────────────────────────
├── v0.14.0 (완료)   ████████████████ Testing & UX Foundations (desk/) ✅
│   │                                  - 71 단위 테스트 (Button, Input) ✅
│   │                                  - E2E Critical Path 테스트 ✅
│   │                                  - 반응형 레이아웃 (Sidebar collapse) ✅
│   │                                  - ErrorBoundary 개선 ✅
│   └────────────────────────────────────────────────────────────────────
├── v0.15.0 (완료)   ████████████████ Cross-Component Integration (ALL) ✅
└── v0.16.0          ██░░░░░░░░░░░░░░ Documentation & E2E Tests (ALL) ← 현재

2025 Q2
├── v1.0.0-rc        ██░░░░░░░░░░░░░░ Release Candidate (ALL)
└── v1.0.0 GA        █░░░░░░░░░░░░░░░ General Availability (ALL)
```

### 다단계 난이도 프로그레션

```
Phase 1-2 (v0.1.0-v0.4.0 완료): 기초 + API
└── 난이도: ★★☆☆☆ (Core functionality, GraphQL, OData, Real-time)

Phase 3-4 (v0.5.0-v0.11.0 완료): 데이터 기능 + 보안 + 운영
└── 난이도: ★★★☆☆ (Lookup, Rollup, Formula, Audit, RLS, Admin)

Phase 5 (v0.12.0): 고급 데이터 기능 ★ 서버 핵심 완성
└── 난이도: ★★★☆☆ (Materialized Views, Advanced Relations, Hierarchy API)

Phase 6 (v0.13.0-v0.14.0): 클라이언트 컴포넌트
└── 난이도: ★★☆☆☆ ~ ★★★☆☆ (SDK 테스트, Desktop 완성)

Phase 7 (v0.15.0-v0.16.0): 통합 + 문서
└── 난이도: ★★★☆☆ (컴포넌트 통합, 문서화, E2E 테스트)

Phase 8 (v1.0.0-rc → GA): 품질 + 릴리스
└── 난이도: ★★★★☆ (성능/보안 감사, 최종 검증)
```

---

### Post-1.0 Backlog

> [Enterprise Features Research](./ENTERPRISE_FEATURES_RESEARCH.md) 참조

---

## Future Backlog (Post 1.0)

> Derived from [Enterprise Features Research](./ENTERPRISE_FEATURES_RESEARCH.md)

### Multi-Tenancy Enhancements

| Feature | Priority | Effort | Description |
|---------|----------|--------|-------------|
| Database-per-Tenant | High | High | Dedicated database isolation |
| Resource quotas per tenant | High | Medium | CPU, memory, storage limits |
| Private endpoints | Medium | High | VPC peering support |
| Instance-per-Tenant | Low | Very High | Maximum isolation |

### Compliance & Security

| Feature | Priority | Effort | Description |
|---------|----------|--------|-------------|
| ISO 27001 certification | Medium | High | Security management standard |
| PCI DSS controls | Low | High | Payment card industry |
| Custom compliance reports | Medium | Medium | PDF/CSV generation |
| MFA enforcement options | High | Medium | TOTP, WebAuthn |

### Advanced Features

| Feature | Priority | Effort | Description |
|---------|----------|--------|-------------|
| SIEM log drains | Medium | Medium | Datadog, Splunk, CloudWatch |
| White-label options | Low | High | Custom branding |
| On-premises deployment | Low | Very High | Air-gapped environments |
| Advanced analytics | Medium | Medium | Query patterns, usage insights |

---

## Development Workflow

### Task Lifecycle

```
📋 Planned → 🔄 In Progress → ✅ Complete
```

### Adding New Tasks

1. **All tasks MUST be added to this ROADMAP.md**
2. Determine appropriate version/phase
3. Assign priority: 🔴 Critical | 🟡 High | 🟢 Normal
4. Update status as work progresses
5. Document completion in phase details

### Version Milestones

- **Patch (0.x.y)**: Bug fixes, minor improvements
- **Minor (0.x.0)**: New features, completed phases
- **Major (1.0.0)**: Production-ready, enterprise features

---

## Research & References

| Document | Purpose |
|----------|---------|
| [Extended Roadmap (v0.9-v0.30)](./ROADMAP_v0.9-v0.30.md) | Data features: Views, Lookup, Rollup, Formula, Aggregation |
| [Enterprise Features Research](./ENTERPRISE_FEATURES_RESEARCH.md) | Industry analysis, compliance requirements |
| [Architecture](./ARCHITECTURE.md) | System design, layer responsibilities |
| [API Documentation](./API.md) | Endpoint specifications |

---

## 🎯 Runtime Data Layer (런타임 데이터 레이어)

> **MorphDB 포지셔닝**: Runtime RDB - PostgreSQL의 ACID/성능 + 런타임 스키마 유연성
>
> **핵심 가치**: 앱 개발자가 데이터 모델을 런타임에 정의/수정하면서도 RDB의 강력함을 유지
>
> **목표**: 데이터 기반 응용 앱 빌드를 극도로 용이하게

### Runtime RDB 핵심 기능

| Feature | Status | Implementation | App Builder 가치 |
|---------|--------|----------------|------------------|
| **Linked Records** | ✅ Complete | RelationMetadata + Virtual FK | 테이블 간 관계 동적 정의 |
| **Lookup Fields** | ✅ Complete | Auto-JOIN expansion | 관계 데이터 자동 조회 |
| **Rollup Fields** | ✅ Complete | Subquery aggregation | 1:N 집계 자동 계산 |
| **Count Fields** | ✅ Complete | Rollup with COUNT | 관련 레코드 수 자동 집계 |
| **Formula Fields** | ✅ Complete | Expression parser + SQL | 계산 필드 런타임 정의 |
| **Computed Columns** | ✅ Complete | PostgreSQL GENERATED | 저장/가상 계산 컬럼 |
| **Virtual Tables (Views)** | ✅ Complete | Query-based views | 데이터 뷰 동적 생성 |
| **Conditional Aggregation** | ✅ Complete | Filtered rollup | 조건부 집계 지원 |
| **Hierarchical Data** | ✅ v0.12.0 | Self-referential FK + CTE | 트리/계층 구조 지원 |
| **M:N Relations** | ✅ v0.12.0 | Auto junction table | 다대다 관계 자동화 |
| **Materialized Views** | ✅ v0.12.0 | Cached query results | 복잡 쿼리 성능 최적화 |

### Runtime RDB vs Traditional Approaches

```
┌─────────────────────────────────────────────────────────────────────────┐
│                    Why Runtime RDB?                                      │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│  Traditional RDB        NoSQL/Document DB       MorphDB (Runtime RDB)   │
│  ─────────────────     ─────────────────       ─────────────────────    │
│  ✅ ACID               ❌ Weak consistency      ✅ Full ACID             │
│  ✅ Complex queries    ❌ Limited queries       ✅ Full SQL power        │
│  ✅ Referential int.   ❌ No FK enforcement     ✅ Virtual + Physical FK │
│  ❌ Static schema      ✅ Flexible schema       ✅ Runtime schema        │
│  ❌ Migration hell     ✅ Schema-less           ✅ Zero-migration        │
│  ❌ DBA required       ✅ Dev-friendly          ✅ API-first             │
│                                                                          │
│  Result: MorphDB = RDB Power + Runtime Flexibility                      │
└─────────────────────────────────────────────────────────────────────────┘
```

### Data Features Architecture

```
┌─────────────────────────────────────────────────────────────────────────┐
│                     MorphDB Data Features Layer                          │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐    │
│  │   Lookup    │  │   Rollup    │  │   Formula   │  │    View     │    │
│  │   Fields    │  │   Fields    │  │   Fields    │  │   Engine    │    │
│  └──────┬──────┘  └──────┬──────┘  └──────┬──────┘  └──────┬──────┘    │
│         │                │                │                │            │
│         └────────────────┼────────────────┼────────────────┘            │
│                          ▼                ▼                              │
│              ┌───────────────────────────────────────┐                  │
│              │        Query Transformation Layer      │                  │
│              │   (Logical → Physical Translation)     │                  │
│              └───────────────────────────────────────┘                  │
│                                    │                                     │
│                                    ▼                                     │
│              ┌───────────────────────────────────────┐                  │
│              │     PostgreSQL Execution Engine        │                  │
│              │   (ACID, Performance, Reliability)     │                  │
│              └───────────────────────────────────────┘                  │
└─────────────────────────────────────────────────────────────────────────┘
```

### Completed Data Features (✅)

#### 1. Lookup Fields (v0.11.x)
```
Lookup = FK 관계를 통한 자동 필드 참조

orders.customer_id → customers._id (FK)
orders.customer_name (lookup) ← customers.name (자동 조회)

API:
POST /api/schema/tables/{table}/columns
{
  "name": "customer_name",
  "type": "lookup",
  "lookupConfig": {
    "relation": "customer_id",
    "targetColumn": "name"
  }
}
```

#### 2. Rollup Fields (v0.12.x)
```
Rollup = 1:N 관계의 N측 데이터 집계

customers._id ← orders.customer_id (1:N)
customers.total_orders (rollup) = COUNT(orders)
customers.total_spent (rollup) = SUM(orders.amount)

지원 집계 함수:
- COUNT: 레코드 수
- SUM/AVG: 숫자 집계
- MIN/MAX: 최소/최대값
- ARRAY_AGG: 배열 수집
- STRING_AGG: 문자열 연결

Conditional Rollup:
{ "filter": { "status": "completed" } }  // 조건부 집계
```

#### 3. Formula Fields (v0.13.x)
```
Formula = 표현식 기반 계산 필드

지원 함수 카테고리:
- Math: ABS, ROUND, POWER, SQRT, MOD
- Text: CONCAT, LEFT, RIGHT, LEN, UPPER, LOWER, TRIM
- Date: NOW, TODAY, DATEADD, DATEDIFF, FORMAT_DATE
- Logic: IF, SWITCH, AND, OR, NOT, COALESCE
- Array: ARRAY_JOIN, ARRAY_CONTAINS, ARRAY_LENGTH

예시:
{ "expression": "CONCAT(first_name, ' ', last_name)" }
{ "expression": "total * 1.1" }
{ "expression": "IF(status == 'active', 'Yes', 'No')" }
```

#### 4. Aggregation API (v0.14.x)
```
서버사이드 집계 쿼리 API

POST /api/data/{table}/aggregate
{
  "aggregations": [
    { "function": "COUNT", "alias": "total" },
    { "function": "SUM", "column": "amount", "alias": "total_amount" }
  ],
  "groupBy": ["status", "category"],
  "filter": { "created_at": { "$gte": "2024-01-01" } }
}

GraphQL 통합:
query {
  orders_aggregate(where: { status: { _eq: "completed" } }) {
    aggregate { count, sum { amount } }
  }
}
```

### Planned Data Features (📋)

#### v0.16.x: Materialized Views
```
캐시된 쿼리 결과로 성능 최적화

새로고침 전략:
- Manual: REFRESH MATERIALIZED VIEW
- Scheduled: Cron-based (Hangfire)
- Concurrent: REFRESH CONCURRENTLY (무중단)

사용 사례:
- 대시보드 집계
- 복잡한 JOIN 캐싱
- 통계 데이터 사전 계산
```

#### v0.17.x: Advanced Relations
```
복잡한 관계 모델링

1. Self-Referential (계층 구조):
   - categories.parent_id → categories._id
   - GET /api/data/categories/{id}/ancestors
   - GET /api/data/categories/tree

2. Many-to-Many:
   - students ↔ courses (junction: enrollments)
   - 자동 junction 테이블 생성
   - 추가 컬럼 지원 (enrolled_at, grade)

3. Polymorphic Relations:
   - entity_type + entity_id 패턴
   - 다형성 참조 지원
```

### Data Features Progress Summary

| Feature | Extended Version | Status | Priority |
|---------|------------------|--------|----------|
| Views & Computed Columns | v0.10.x | ✅ Complete | Core |
| Lookup Fields | v0.11.x | ✅ Complete | Core |
| Rollup Fields | v0.12.x | ✅ Complete | Core |
| Formula Fields | v0.13.x | ✅ Complete | Core |
| Aggregation API | v0.14.x | ✅ Complete | Core |
| Client SDK Aggregation | v0.15.x | ✅ Complete | Integration |
| Materialized Views | v0.16.x | ✅ Complete | Performance |
| Advanced Relations | v0.17.x | ✅ Complete | Extended |
| File Attachments | v0.18.x | 📋 Planned | Extended |
| Full-Text Search | v0.19.x | 📋 Planned | Extended |
| Workflow Automation | v0.20.x | 📋 Planned | Enterprise |

> **Note**: Extended Roadmap 버전 (v0.10.x~v0.30.x)은 데이터 기능 개발 순서를 나타냅니다.
> 메인 Roadmap 버전 (v0.11.0~v1.0.0)은 릴리스 마일스톤입니다.
> 데이터 기능은 이미 메인 코드베이스에 통합되어 있습니다.

See [Extended Roadmap](./ROADMAP_v0.9-v0.30.md) for full implementation details.

---

## MorphDB Desk (Desktop UI)

> **Location**: `desk/`
> **Documentation**: [`desk/docs/GAP_ANALYSIS.md`](../desk/docs/GAP_ANALYSIS.md), [`desk/docs/DEVELOPMENT_ROADMAP.md`](../desk/docs/DEVELOPMENT_ROADMAP.md)

MorphDB의 모든 기능을 관리할 수 있는 Electron 기반 데스크탑 애플리케이션입니다.

### API Coverage Status

| Controller | Endpoints | desk/ Status | Target Phase |
|------------|-----------|--------------|--------------|
| SchemaController | 10 | ✅ 100% | Phase 1 ✅ |
| DataController | 5 | ✅ 100% | Phase 1 ✅ |
| ProjectController | 10 | ✅ 100% | Phase 2 ✅ |
| AggregationController | 1 | ✅ 100% | Phase 2 ✅ |
| BatchController | 5 | ✅ 100% | Phase 2 ✅ |
| BulkController | 13 | ✅ 100% | Phase 2 ✅ |
| ViewController | 8 | ✅ 100% | Phase 3 ✅ |
| WebhookController | 12 | ✅ 100% | Phase 3 ✅ |
| OrganizationController | 12 | ✅ 100% | Phase 3 ✅ |
| BackupController | 6 | ✅ 100% | Phase 3 ✅ |
| SecurityController | 11 | ✅ 100% | Phase 4 ✅ |
| SsoController | 12 | ✅ 100% | Phase 4 ✅ |
| AuditController | 3 | ✅ 100% | Phase 4 ✅ |
| QuotaController | 4 | ✅ 100% | Phase 4 ✅ |

### Development Phases

| Phase | Version | Focus | Key Features |
|-------|---------|-------|--------------|
| 1 | v0.2.x | Foundation | ✅ Routing, Schema 100%, Data 100%, Project CRUD |
| 2 | v0.3.x | Data Operations | ✅ Aggregation, Batch ops, Import/Export |
| 3 | v0.4.x | Enterprise | ✅ Views, Webhooks, Organizations, Backups |
| 4 | v0.5.x | Security | ✅ SSO config, API keys, RLS, Audit viewer, Quota |
| 5 | v1.0.x | Polish | Performance, UX, Testing, Docs |

**Current**: v0.5.x (Phase 4 Complete) - 100% API coverage (100/100 endpoints)
**Target**: v1.0.x - Full feature parity with polish

### Phase 1 Completion Details (v0.2.x)

| Component | Features | Status |
|-----------|----------|--------|
| Architecture | React Router v6, Layout system, Navigation | ✅ |
| Schema - Tables | List, Create, Rename, Delete | ✅ |
| Schema - Columns | Add, Update, Delete | ✅ |
| Schema - Indexes | Create (btree/hash/gin/gist), Delete | ✅ |
| Schema - Relations | Create (1:1, 1:N, N:1, N:M), Delete, Cascade options | ✅ |
| Data - Query | Pagination, Sorting, OData filter builder | ✅ |
| Data - CRUD | Create, Read, Update (inline edit), Delete | ✅ |
| Projects | Full lifecycle: Create, Read, Update, Delete | ✅ |
| Projects | Status management: Active, Suspended, Archived | ✅ |
| Projects | Health validation and reporting | ✅ |

### Phase 2 Completion Details (v0.3.x)

| Component | Features | Status |
|-----------|----------|--------|
| Aggregation | Visual query builder (COUNT, SUM, AVG, MIN, MAX) | ✅ |
| Aggregation | GROUP BY column picker, custom aliases | ✅ |
| Aggregation | Results table with live execution | ✅ |
| Batch Operations | Multi-operation executor (INSERT, UPDATE, DELETE) | ✅ |
| Batch Operations | Color-coded operation types, per-op results | ✅ |
| Bulk Update | Filter-based multi-record update | ✅ |
| Bulk Delete | Safe delete with filter & confirmation | ✅ |
| Import | CSV/JSON/NDJSON with format-specific options | ✅ |
| Import | Auto-detect format, progress tracking | ✅ |
| Export | CSV/JSON/XLSX with column selection | ✅ |
| Export | Filter & row limit, format options | ✅ |

### Phase 3 Completion Details (v0.4.x)

| Component | Features | Status |
|-----------|----------|--------|
| Views | List views, Create (query/virtual), Execute, Delete | ✅ |
| Views | Virtual table support, Query editor | ✅ |
| Webhooks | List, Create, Update, Delete subscriptions | ✅ |
| Webhooks | Event selection (insert/update/delete), Headers | ✅ |
| Webhooks | DLQ management, Replay, Resolve, Archive | ✅ |
| Organizations | List, Create, Update, Delete organizations | ✅ |
| Organizations | Member management with role assignment | ✅ |
| Organizations | Invitation create and revoke | ✅ |
| Backups | Project-scoped backup list | ✅ |
| Backups | Create backup (Full/SchemaOnly/DataOnly) | ✅ |
| Backups | Restore with target project selection | ✅ |
| Backups | Download, Delete, Expiration settings | ✅ |

### Phase 4 Completion Details (v0.5.x)

| Component | Features | Status |
|-----------|----------|--------|
| Audit Logs | Log viewer with time range filtering | ✅ |
| Audit Logs | Event type and severity filters | ✅ |
| Audit Logs | Actor, resource, action details | ✅ |
| Audit Logs | Statistics dashboard | ✅ |
| Quota | Usage overview (storage, rows, API calls) | ✅ |
| Quota | Rate limit status and headers | ✅ |
| Quota | Tier-based limits display | ✅ |
| Security | API Keys list, create, revoke | ✅ |
| Security | API Key scope management | ✅ |
| Security | RLS Policies CRUD | ✅ |
| Security | Encryption status and key rotation | ✅ |
| SSO | Organization-scoped config list | ✅ |
| SSO | Provider setup (OIDC, EntraID, Google, Okta, Auth0, Keycloak) | ✅ |
| SSO | Claim mappings and domain restrictions | ✅ |
| SSO | Config test, activate, deactivate | ✅ |

### Phase 5 Progress Details (v0.14.0 - v1.0.x)

| Component | Features | Status |
|-----------|----------|--------|
| Theme System | Dark/Light mode toggle | ✅ |
| Theme System | System preference detection | ✅ |
| Theme System | CSS variables with OKLCH colors | ✅ |
| Theme System | Persistent theme storage | ✅ |
| Command Palette | Cmd/Ctrl+K activation | ✅ |
| Command Palette | Navigation, theme, connection switching | ✅ |
| Keyboard Shortcuts | Global shortcuts (navigation, actions) | ✅ |
| Keyboard Shortcuts | Shortcuts help dialog (? key) | ✅ |
| Keyboard Shortcuts | Sidebar toggle (Ctrl+B) | ✅ |
| Unit Testing | Vitest setup with testing-library | ✅ |
| Unit Testing | 71 passing tests (Button, Input, stores, hooks) | ✅ |
| E2E Testing | Playwright setup with config | ✅ |
| E2E Testing | Critical path tests (connection, navigation, settings) | ✅ |
| Error Handling | Toast notification system | ✅ |
| Error Handling | Route-level ErrorBoundary with dev mode details | ✅ |
| Error Handling | ComponentErrorBoundary for wrapping | ✅ |
| Error Handling | InlineError for compact display | ✅ |
| Documentation | Keyboard shortcuts reference (KEYBOARD_SHORTCUTS.md) | ✅ |
| Documentation | User guide (USER_GUIDE.md) | ✅ |
| API Types | Centralized type definitions (api-types.ts) | ✅ |
| API Types | Type converters (api-converters.ts) | ✅ |
| API Types | Field name alignment with server | ✅ |
| Responsive Layout | Sidebar collapse with layoutStore | ✅ |
| Responsive Layout | Tooltip component for collapsed items | ✅ |
| Accessibility | WCAG 2.1 AA compliance | 📋 Planned |
| Storybook | Component documentation | 📋 Planned |

---

## Contributing

1. Check this ROADMAP for planned work
2. Pick a task from current version's phase
3. Create feature branch: `feature/{phase}-{task}`
4. Implement with tests
5. Update ROADMAP status
6. Submit PR

See [CONTRIBUTING.md](../CONTRIBUTING.md) for detailed guidelines.
