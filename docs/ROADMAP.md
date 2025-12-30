# MorphDB Roadmap

> **Single Source of Truth**: 모든 개발 작업은 이 로드맵을 기준으로 진행됩니다.

---

## Version History

| Version | Phases | Status |
|---------|--------|--------|
| 0.1.0 | 0-3: Core functionality | ✅ Complete |
| 0.2.0 | 4-6: API layer (GraphQL, OData) | ✅ Complete |
| 0.3.0 | 7-8: Real-time features | ✅ Complete |
| 0.4.0 | 9-10: Bulk & SDKs | ✅ Complete |
| 0.5.0 | 11-12: Production Ready (Beta) | ✅ Complete |
| 0.6.0 | 13-16: Enterprise Hardening | ✅ Complete |
| 0.7.0 | 17-18: Schema Architecture | ✅ Complete |
| 0.7.5 | 18.5: Virtual Constraints | ✅ Complete |
| 0.8.0 | 18.6-20: System Columns + Audit + Rate Limiting | ✅ Complete |
| 0.9.0 | 21-22: Organization + SSO | ✅ Complete |
| 1.0.0 | 23-24: Enterprise Ready | 📋 Planned |

---

## Current Focus

**Active Version**: v1.0.0 (Enterprise Ready)

### Completed in Previous Version (v0.9.0)

| Phase | Task | Status |
|-------|------|--------|
| 21 | Organization entity, API, RBAC, Members, Invitations | ✅ Complete |
| 22 | SSO Configuration CRUD, OIDC provider support | ✅ Complete |
| 21-22 | Integration tests (35 tests: 16 Org + 19 SSO) | ✅ Complete |

### Immediate Tasks

| Priority | Task | Status | Assigned |
|----------|------|--------|----------|
| 🔴 Critical | Admin Dashboard - Phase 23 | 📋 Planned | - |
| 🔴 Critical | Health status overview | 📋 Planned | - |
| 🟡 High | Enterprise Features - Phase 24 | 📋 Planned | - |
| 🟡 High | Backup and Recovery (PITR) | 📋 Planned | - |
| 🟢 Normal | Compliance controls (SOC 2, HIPAA, GDPR) | 📋 Planned | - |

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

### 📋 Planned Phases

#### v1.0.0: Enterprise Ready

| Phase | Description | Tasks |
|-------|-------------|-------|
| 23 | **Admin Dashboard** | Management UI |
| 24 | **Enterprise Features** | Backup, recovery, HA |

<details>
<summary>Task Breakdown</summary>

**Phase 23: Admin Dashboard**
- [ ] Health status overview
- [ ] Schema explorer with visual viewer
- [ ] Query console with history
- [ ] Performance monitoring (latency, connections)
- [ ] Alerting system (threshold, anomaly detection)
- [ ] Team management UI
- [ ] Billing & usage dashboard

**Phase 24: Enterprise Features**
- [ ] Point-in-Time Recovery (PITR)
- [ ] Cross-region backup
- [ ] Disaster recovery automation
- [ ] SOC 2 Type II compliance controls
- [ ] HIPAA technical safeguards (BAA support)
- [ ] GDPR data subject rights API

</details>

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
| ProjectController | 10 | ✅ 100% | Phase 1 ✅ |
| AggregationController | 1 | ❌ 0% | Phase 2 |
| BatchController | 5 | ❌ 0% | Phase 2 |
| BulkController | 13 | ❌ 0% | Phase 2 |
| ViewController | 8 | ❌ 0% | Phase 3 |
| WebhookController | 12 | ❌ 0% | Phase 3 |
| OrganizationController | 12 | ❌ 0% | Phase 3 |
| BackupController | 6 | ❌ 0% | Phase 3 |
| SecurityController | 11 | ❌ 0% | Phase 4 |
| SsoController | 12 | ❌ 0% | Phase 4 |
| AuditController | 3 | ❌ 0% | Phase 4 |
| QuotaController | 4 | ❌ 0% | Phase 4 |

### Development Phases

| Phase | Version | Focus | Key Features |
|-------|---------|-------|--------------|
| 1 | v0.2.x | Foundation | ✅ Routing, Schema 100%, Data 100%, Project CRUD |
| 2 | v0.3.x | Data Operations | Aggregation, Batch ops, Import/Export |
| 3 | v0.4.x | Enterprise | Org management, Views, Webhooks, Backup |
| 4 | v0.5.x | Security | SSO config, API keys, RLS, Audit viewer |
| 5 | v1.0.x | Polish | Performance, UX, Testing, Docs |

**Current**: v0.2.x (Phase 1 Complete) - 25% API coverage
**Target**: v1.0.x - 100% API coverage with full feature parity

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

---

## Contributing

1. Check this ROADMAP for planned work
2. Pick a task from current version's phase
3. Create feature branch: `feature/{phase}-{task}`
4. Implement with tests
5. Update ROADMAP status
6. Submit PR

See [CONTRIBUTING.md](../CONTRIBUTING.md) for detailed guidelines.
