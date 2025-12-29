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
| 0.9.0 | 21-22: Organization + SSO | 📋 Planned |
| 1.0.0 | 23-24: Enterprise Ready | 📋 Planned |

---

## Current Focus

**Active Version**: v0.9.0 (Organization + SSO)

### Completed in Previous Version (v0.8.0)

| Phase | Task | Status |
|-------|------|--------|
| 18.6 | System Columns (Core/Standard/Optional) | ✅ Complete |
| 19 | Audit Logging (API, middleware, integration tests) | ✅ Complete |
| 20 | Rate Limiting (Quota API, rate limit headers) | ✅ Complete |

### Immediate Tasks

| Priority | Task | Status | Assigned |
|----------|------|--------|----------|
| 🔴 Critical | Organization entity and API (Phase 21) | 📋 Planned | - |
| 🔴 Critical | RBAC implementation (Phase 21) | 📋 Planned | - |
| 🟡 High | OIDC provider support (Phase 22) | 📋 Planned | - |
| 🟡 High | SAML 2.0 for enterprise (Phase 22) | 📋 Planned | - |
| 🟢 Normal | Team member invitation flow | 📋 Planned | - |

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

### 📋 Planned Phases

#### v0.9.0: Organization + SSO

| Phase | Description | Tasks |
|-------|-------------|-------|
| 21 | **Organization** | Team management, roles, hierarchy |
| 22 | **SSO Integration** | SAML 2.0, OIDC |

<details>
<summary>Task Breakdown</summary>

**Phase 21: Organization Management**
- [ ] Organization entity and API
- [ ] Project hierarchy (Org → Projects → Environments)
- [ ] RBAC: enterprise_admin, org_admin, project_admin, developer, viewer
- [ ] Team member invitation flow
- [ ] Activity tracking per member

**Phase 22: SSO Integration**
- [ ] OIDC provider support (Google, Microsoft, Auth0, Okta)
- [ ] SAML 2.0 for enterprise (Okta, Azure AD, ADFS)
- [ ] Just-in-time user provisioning
- [ ] Attribute mapping configuration
- [ ] Session management (timeout, concurrent limits)

</details>

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
| [Enterprise Features Research](./ENTERPRISE_FEATURES_RESEARCH.md) | Industry analysis, compliance requirements |
| [Architecture](./ARCHITECTURE.md) | System design, layer responsibilities |
| [API Documentation](./API.md) | Endpoint specifications |

---

## Contributing

1. Check this ROADMAP for planned work
2. Pick a task from current version's phase
3. Create feature branch: `feature/{phase}-{task}`
4. Implement with tests
5. Update ROADMAP status
6. Submit PR

See [CONTRIBUTING.md](../CONTRIBUTING.md) for detailed guidelines.
