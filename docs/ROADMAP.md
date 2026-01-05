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
| **0.12.0** | **sdk/** | **SDK Testing & Stabilization** | 📋 Planned |
| **0.13.0** | **desk/** | **Desktop Phase 5 Completion** | 📋 Planned |
| **0.14.0** | **ALL** | **Cross-Component Integration** | 📋 Planned |
| **0.15.0** | **ALL** | **Documentation & E2E Tests** | 📋 Planned |
| **1.0.0-rc** | **ALL** | **Release Candidate** | 📋 Planned |
| **1.0.0** | **ALL** | **General Availability** | 📋 Planned |

---

## v1.0.0 Release Path

> **철학 문서**: [PHILOSOPHY.md](./PHILOSOPHY.md) 참조
> **철학 검토 결과**: 100% 준수 (v0.9.5에서 4개 개선 항목 완료)
> **통합 범위**: Server (`src/`) + SDKs (`sdk/`) + Desktop App (`desk/`)

```
src/ Server:
v0.9.5 ──→ v0.10.0 ──→ v0.11.0 ──────────────────────────────────┐
(완료)      (완료)      (완료)                                    │
                                                                  │
sdk/ Python + TypeScript:                                         │
─────────────────────── v0.12.0 ─────────────────────────────────┼──┐
                       (SDK 테스트/안정화)                          │  │
                                                                  │  │
desk/ Desktop App:                                                │  │
Phase 1-4 완료 ───────────────── v0.13.0 ────────────────────────┼──┼──┐
                               (Phase 5 완료)                     │  │  │
                                                                  ▼  ▼  ▼
                                                              v0.14.0 (통합)
                                                                  │
                                                              v0.15.0 (문서/E2E)
                                                                  │
                                                              v1.0.0-rc
                                                                  │
                                                              v1.0.0 GA
```

---

## Current Focus

**Active Version**: v0.12.0 (SDK Testing & Stabilization)

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

#### 📋 v0.12.0: SDK Testing & Stabilization

> **목표**: Python/TypeScript SDK 기능 검증 및 테스트 완료
> **컴포넌트**: `sdk/`
> **기간**: 1-2 주

| Task | Priority | Description | Acceptance Criteria |
|------|----------|-------------|---------------------|
| Python SDK 단위 테스트 | 🔴 Critical | pytest 기반 테스트 스위트 | >80% 커버리지 |
| TypeScript SDK 단위 테스트 | 🔴 Critical | vitest/jest 기반 테스트 | >80% 커버리지 |
| Python SDK 통합 테스트 | 🟡 High | 실제 서버 연동 테스트 | 핵심 플로우 검증 |
| TypeScript SDK 통합 테스트 | 🟡 High | 실제 서버 연동 테스트 | 핵심 플로우 검증 |
| SDK API 버전 호환성 | 🟡 High | v0.11.0 서버와 호환 확인 | Breaking change 0개 |
| SDK README 업데이트 | 🟢 Normal | 최신 API 반영 | 모든 기능 문서화 |
| SDK 패키지 버전 동기화 | 🟢 Normal | package.json/pyproject.toml 버전 | 0.12.0으로 통일 |

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

#### 📋 v0.13.0: Desktop Phase 5 Completion

> **목표**: desk/ 앱 Phase 5 완료 (Production-Ready 품질)
> **컴포넌트**: `desk/`
> **기간**: 2-3 주
> **참조**: [`desk/docs/DEVELOPMENT_ROADMAP.md`](../desk/docs/DEVELOPMENT_ROADMAP.md)

| Task | Priority | Description | Acceptance Criteria |
|------|----------|-------------|---------------------|
| 반응형 레이아웃 | 🟡 High | Sidebar collapse, mobile 대응 | 모든 뷰포트 지원 |
| 접근성 (WCAG 2.1 AA) | 🟡 High | Focus 관리, ARIA labels | axe-core 검증 통과 |
| 핵심 컴포넌트 단위 테스트 | 🟡 High | Button, Input, DataGrid 테스트 | 주요 컴포넌트 커버 |
| E2E Critical Path 테스트 | 🔴 Critical | Connection → Table → Data CRUD | Playwright 테스트 |
| Storybook 컴포넌트 문서 | 🟢 Normal | UI 컴포넌트 문서화 | 주요 컴포넌트 stories |
| 번들 최적화 | 🟢 Normal | Tree shaking, chunk splitting | 초기 로드 < 2MB |
| Error Boundary 개선 | 🟡 High | Route-level 에러 처리 | Retry 메커니즘 |
| 사용자 가이드 | 🟢 Normal | desk/docs/USER_GUIDE.md | 시작하기 ~ 고급 기능 |

**현재 완료된 Phase 5 항목**:
- ✅ Dark/Light 테마 토글 (시스템 설정 연동)
- ✅ Command Palette (Cmd/Ctrl+K)
- ✅ 키보드 단축키 (도움말 다이얼로그 포함)
- ✅ Vitest 단위 테스트 설정 (22 tests)
- ✅ Playwright E2E 테스트 설정
- ✅ Toast 알림 시스템
- ✅ API 타입 중앙화 및 필드명 정렬

---

#### 📋 v0.14.0: Cross-Component Integration

> **목표**: src/, sdk/, desk/ 간 통합 테스트 및 호환성 검증
> **컴포넌트**: `ALL (src/ + sdk/ + desk/)`
> **기간**: 1-2 주

| Task | Priority | Description | Acceptance Criteria |
|------|----------|-------------|---------------------|
| SDK ↔ Server 통합 테스트 | 🔴 Critical | Python/TS SDK가 서버와 완전 호환 | 모든 API 호출 성공 |
| Desk ↔ Server 통합 테스트 | 🔴 Critical | desk 앱이 서버 API 완전 호환 | 100% 기능 동작 |
| SDK → Desk 타입 공유 검토 | 🟡 High | TypeScript 타입 재사용 가능성 | 공통 타입 패키지 검토 |
| Real-time 통합 테스트 | 🟡 High | SignalR 구독 3개 컴포넌트 검증 | 실시간 동기화 안정 |
| Bulk Operations 통합 테스트 | 🟡 High | 대용량 데이터 처리 검증 | 10K rows 성공 |
| 버전 호환성 매트릭스 | 🟢 Normal | 컴포넌트 간 지원 버전 문서화 | 명확한 호환성 표 |

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

#### 📋 v0.15.0: Documentation & E2E Tests

> **목표**: 완전한 문서화 및 종단간 테스트 스위트
> **컴포넌트**: `ALL`
> **기간**: 2 주

| Task | Priority | Description | Acceptance Criteria |
|------|----------|-------------|---------------------|
| API 문서 완성 (OpenAPI 3.1) | 🔴 Critical | 모든 엔드포인트 명세 | 100% 커버리지 |
| SDK 문서 완성 | 🔴 Critical | Python/TypeScript 가이드 | 모든 기능 예제 |
| desk 사용자 가이드 | 🟡 High | 기능별 상세 설명 | 스크린샷 포함 |
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

| Task | Priority | Description | Acceptance Criteria |
|------|----------|-------------|---------------------|
| 성능 벤치마크 | 🔴 Critical | 부하 테스트 완료 | 1000 RPS, p99 < 100ms |
| 보안 감사 | 🔴 Critical | 취약점 스캔 및 수정 | Critical/High 0개 |
| 호환성 테스트 | 🟡 High | PostgreSQL 버전 호환 | 14, 15, 16 지원 확인 |
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

2025 Q1-Q2 (현재 ~ 진행 예정)
├── v0.12.0          ████░░░░░░░░░░░░ SDK Testing & Stabilization (sdk/) ← 현재
├── v0.13.0          ██░░░░░░░░░░░░░░ Desktop Phase 5 Completion (desk/)
├── v0.14.0          ██░░░░░░░░░░░░░░ Cross-Component Integration (ALL)
└── v0.15.0          ██░░░░░░░░░░░░░░ Documentation & E2E Tests (ALL)

2025 Q2
├── v1.0.0-rc        ██░░░░░░░░░░░░░░ Release Candidate (ALL)
└── v1.0.0 GA        █░░░░░░░░░░░░░░░ General Availability (ALL)
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

## Data Features Progress (Extended Roadmap)

| Version | Feature | Status |
|---------|---------|--------|
| v0.10.x | Views & Computed Columns | ✅ Complete |
| v0.11.x | Lookup Fields | ✅ Complete |
| v0.12.x | Rollup Fields | ✅ Complete |
| v0.13.x | Formula Fields | ✅ Complete |
| v0.14.x | Aggregation API | ✅ Complete |
| v0.15.x | Client SDK Aggregation | ✅ Complete |
| v0.16.x | Materialized Views | 📋 Planned |

See [Extended Roadmap](./ROADMAP_v0.9-v0.30.md) for full details.

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

### Phase 5 Progress Details (v1.0.x)

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
| Unit Testing | Vitest setup with testing-library | ✅ |
| Unit Testing | 22 passing tests (stores, hooks) | ✅ |
| E2E Testing | Playwright setup with config | ✅ |
| E2E Testing | App loading and navigation tests | ✅ |
| Error Handling | Toast notification system | ✅ |
| Error Handling | API error handling utility | ✅ |
| Documentation | Keyboard shortcuts reference | ✅ |
| API Types | Centralized type definitions (api-types.ts) | ✅ |
| API Types | Type converters (api-converters.ts) | ✅ |
| API Types | Field name alignment with server | ✅ |
| Responsive Layout | Sidebar collapse, mobile support | 📋 Planned |
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
