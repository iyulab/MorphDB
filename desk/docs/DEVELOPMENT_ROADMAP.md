# MorphDB Desk - Development Roadmap

> Target: MorphDB의 모든 기능을 UI로 관리할 수 있는 완전한 데스크탑 도구
> Based on: Gap Analysis 2025-12-30

---

## Phase 1: Foundation & Core Enhancement (v0.2.x)

**목표**: 기존 기능 완성 및 확장 가능한 아키텍처 구축

### 1.1 Architecture Refactoring
```
📁 src/renderer/
├── 📁 routes/           # NEW: React Router pages
├── 📁 layouts/          # NEW: Layout components
├── 📁 features/         # NEW: Feature-based modules
│   ├── schema/
│   ├── data/
│   └── connections/
├── 📁 shared/           # NEW: Shared utilities
│   ├── hooks/
│   ├── services/
│   └── utils/
└── 📁 components/       # Existing: Refactored
```

**Tasks:**
- [ ] React Router 통합 (react-router-dom)
- [ ] Layout 시스템 구현 (Header, Sidebar, Main)
- [ ] Feature-based 모듈 구조 리팩토링
- [ ] Global state 관리 개선 (zustand persist)
- [ ] API client 리팩토링 (react-query 통합)

### 1.2 Schema Management Completion
**Endpoint Coverage: 70% → 100%**

**Tasks:**
- [ ] Relations 관리 UI
  - [ ] Create relation dialog
  - [ ] Relation list in table details
  - [ ] Delete relation with confirmation
  - [ ] ERD viewer (react-flow)
- [ ] Index 관리 UI
  - [ ] Create index dialog (columns selection)
  - [ ] Index list in table details
  - [ ] Delete index with confirmation

**API Client 추가:**
```typescript
// api.ts additions
async createRelation(tableName: string, data: CreateRelationRequest): Promise<RelationResponse>
async deleteRelation(tableName: string, relationId: string): Promise<void>
async createIndex(tableName: string, data: CreateIndexRequest): Promise<IndexResponse>
async deleteIndex(tableName: string, indexName: string): Promise<void>
```

### 1.3 Data Management Enhancement
**Endpoint Coverage: 80% → 100%**

**Tasks:**
- [ ] Advanced Filter Builder
  - [ ] Visual filter construction UI
  - [ ] Support for AND/OR operators
  - [ ] Save/load filter presets
- [ ] Inline Cell Editing
  - [ ] Double-click to edit
  - [ ] Type-aware editors (text, number, date, boolean)
  - [ ] Validation with error display
- [ ] Bulk Selection
  - [ ] Checkbox column
  - [ ] Select all / Select page
  - [ ] Bulk delete selected rows

**New Components:**
```
components/
├── FilterBuilder/
│   ├── FilterBuilder.tsx
│   ├── FilterCondition.tsx
│   └── FilterOperator.tsx
├── CellEditors/
│   ├── TextEditor.tsx
│   ├── NumberEditor.tsx
│   ├── DateEditor.tsx
│   └── BooleanEditor.tsx
└── DataGrid/
    └── SelectionColumn.tsx
```

### 1.4 Project Management
**Endpoint Coverage: 30% → 100%**

**Tasks:**
- [ ] Project CRUD 완전 구현
  - [ ] Create project dialog
  - [ ] Edit project dialog
  - [ ] Delete project confirmation
- [ ] Project Lifecycle
  - [ ] Archive project action
  - [ ] Suspend project action
  - [ ] Reactivate project action
- [ ] Project Dashboard
  - [ ] Project stats display
  - [ ] Health status indicator
  - [ ] Quick actions menu

**Routes:**
```
/projects                    # Project list
/projects/new               # Create project
/projects/:id               # Project dashboard
/projects/:id/settings      # Project settings
/projects/:id/tables        # Tables in project
```

---

## Phase 2: Data Operations (v0.3.x)

**목표**: 대량 데이터 처리 및 분석 기능

### 2.1 Aggregation Module
**신규 기능**

**Tasks:**
- [ ] Aggregation Query Builder
  - [ ] Function selector (COUNT, SUM, AVG, MIN, MAX)
  - [ ] Column selector
  - [ ] GROUP BY clause builder
  - [ ] HAVING clause support
- [ ] Result Visualization
  - [ ] Table view for results
  - [ ] Bar chart (recharts)
  - [ ] Pie chart for distributions
  - [ ] Line chart for trends

**New Components:**
```
features/aggregation/
├── AggregationBuilder.tsx
├── FunctionSelector.tsx
├── GroupBySelector.tsx
├── ResultsTable.tsx
└── charts/
    ├── BarChart.tsx
    ├── PieChart.tsx
    └── LineChart.tsx
```

**API Client:**
```typescript
interface AggregationRequest {
  tableName: string;
  functions: AggregateFunction[];
  groupBy?: string[];
  having?: string;
  filter?: string;
}

async aggregate(request: AggregationRequest): Promise<AggregationResult[]>
```

### 2.2 Batch Operations
**신규 기능**

**Tasks:**
- [ ] Bulk Insert
  - [ ] JSON array input
  - [ ] Validation before submit
  - [ ] Progress indicator
- [ ] Bulk Update
  - [ ] Filter-based selection
  - [ ] Field updates form
  - [ ] Preview affected rows
- [ ] Bulk Delete
  - [ ] Filter-based deletion
  - [ ] Confirmation with row count
- [ ] Upsert Operation
  - [ ] Key columns selection
  - [ ] Data input form

**New Components:**
```
features/batch/
├── BatchOperations.tsx
├── BulkInsertDialog.tsx
├── BulkUpdateDialog.tsx
├── BulkDeleteDialog.tsx
└── UpsertDialog.tsx
```

### 2.3 Import/Export Module
**신규 기능**

**Tasks:**
- [ ] Import Wizard
  - [ ] File upload (drag & drop)
  - [ ] Format detection (CSV, JSON, NDJSON)
  - [ ] Column mapping UI
  - [ ] Preview data
  - [ ] Duplicate handling options
  - [ ] Progress tracking
  - [ ] Error reporting
- [ ] Export Module
  - [ ] Format selection (CSV, JSON, XLSX)
  - [ ] Column selection
  - [ ] Filter options
  - [ ] Download progress

**New Components:**
```
features/import-export/
├── ImportWizard/
│   ├── ImportWizard.tsx
│   ├── FileUpload.tsx
│   ├── FormatDetection.tsx
│   ├── ColumnMapping.tsx
│   ├── DataPreview.tsx
│   └── ImportProgress.tsx
├── ExportDialog/
│   ├── ExportDialog.tsx
│   ├── FormatSelector.tsx
│   └── ColumnPicker.tsx
└── JobMonitor/
    ├── JobList.tsx
    └── JobProgress.tsx
```

**API Client:**
```typescript
async importCsv(tableName: string, file: File, options: ImportOptions): Promise<ImportJob>
async importJson(tableName: string, file: File, options: ImportOptions): Promise<ImportJob>
async exportCsv(tableName: string, options: ExportOptions): Promise<ExportJob>
async exportJson(tableName: string, options: ExportOptions): Promise<ExportJob>
async exportXlsx(tableName: string, options: ExportOptions): Promise<ExportJob>
async getJobProgress(jobId: string): Promise<JobProgress>
async downloadExport(jobId: string): Promise<Blob>
```

---

## Phase 3: Enterprise Features (v0.4.x)

**목표**: 팀 협업 및 운영 기능

### 3.1 Organization Management
**신규 기능**

**Tasks:**
- [ ] Organization Module
  - [ ] Create organization
  - [ ] Organization dashboard
  - [ ] Organization settings
- [ ] Member Management
  - [ ] Invite members
  - [ ] Role assignment (Owner, Admin, Member, Viewer)
  - [ ] Remove members
  - [ ] Pending invitations list

**Routes:**
```
/organizations              # Org list (if multi-org)
/org/:orgId                # Org dashboard
/org/:orgId/members        # Member management
/org/:orgId/invitations    # Pending invitations
/org/:orgId/settings       # Org settings
```

### 3.2 View Management
**신규 기능**

**Tasks:**
- [ ] View Builder
  - [ ] Source table selection
  - [ ] Column selection & aliases
  - [ ] Join configuration
  - [ ] Filter builder
  - [ ] ORDER BY configuration
  - [ ] Aggregation support
  - [ ] Materialized view options
- [ ] View Operations
  - [ ] List views
  - [ ] View data query
  - [ ] Refresh materialized view
  - [ ] Stale status indicator

**New Components:**
```
features/views/
├── ViewBuilder/
│   ├── ViewBuilder.tsx
│   ├── SourceSelector.tsx
│   ├── ColumnBuilder.tsx
│   ├── JoinBuilder.tsx
│   └── MaterializedOptions.tsx
├── ViewList.tsx
└── ViewDataGrid.tsx
```

### 3.3 Webhook Management
**신규 기능**

**Tasks:**
- [ ] Webhook Configuration
  - [ ] Create webhook dialog
  - [ ] Event type selection (Insert, Update, Delete)
  - [ ] Table subscription
  - [ ] URL & headers configuration
  - [ ] Secret management
- [ ] Webhook Monitoring
  - [ ] Delivery history
  - [ ] Success/failure indicators
  - [ ] Retry controls
- [ ] DLQ Management
  - [ ] Failed message list
  - [ ] Replay functionality
  - [ ] Resolve/Archive actions

**Routes:**
```
/webhooks                  # Webhook list
/webhooks/:id              # Webhook details
/webhooks/:id/history      # Delivery history
/webhooks/:id/dlq          # Dead letter queue
```

### 3.4 Backup & Restore
**신규 기능**

**Tasks:**
- [ ] Backup Management
  - [ ] Create backup dialog
  - [ ] Backup list with status
  - [ ] Download backup
  - [ ] Delete old backups
- [ ] Restore Module
  - [ ] Restore target selection
  - [ ] Drop existing data option
  - [ ] Restore progress

**New Components:**
```
features/backups/
├── BackupList.tsx
├── CreateBackupDialog.tsx
├── BackupDetails.tsx
└── RestoreDialog.tsx
```

---

## Phase 4: Security & Compliance (v0.5.x)

**목표**: 엔터프라이즈급 보안 및 규정 준수

### 4.1 SSO Configuration
**신규 기능**

**Tasks:**
- [ ] SSO Provider Setup
  - [ ] Provider type selection (OIDC, EntraId, Google, Okta, Auth0, Keycloak)
  - [ ] Authority/Client configuration
  - [ ] Scope configuration
  - [ ] Claim mappings
  - [ ] Domain restrictions
- [ ] SSO Operations
  - [ ] Test configuration
  - [ ] Activate/Deactivate
  - [ ] Status monitoring

**Routes:**
```
/settings/sso              # SSO configuration list
/settings/sso/new          # Create SSO config
/settings/sso/:id          # SSO config details
```

### 4.2 API Key Management
**신규 기능**

**Tasks:**
- [ ] API Key Operations
  - [ ] Create API key (with expiry)
  - [ ] List API keys
  - [ ] Revoke API key
  - [ ] Rotate API key
- [ ] Key Display
  - [ ] Show key prefix only
  - [ ] Copy to clipboard
  - [ ] Last used timestamp

### 4.3 Security Policies (RLS)
**신규 기능**

**Tasks:**
- [ ] Policy Builder
  - [ ] Policy expression editor
  - [ ] Table scope selection
  - [ ] Policy type (Row, Column)
- [ ] Policy Management
  - [ ] List policies per table
  - [ ] Enable/Disable policies
  - [ ] Priority ordering

### 4.4 Audit Log Viewer
**신규 기능**

**Tasks:**
- [ ] Audit Search
  - [ ] Date range filter
  - [ ] Actor filter
  - [ ] Action type filter
  - [ ] Resource filter
  - [ ] Full-text search
- [ ] Audit Display
  - [ ] Timeline view
  - [ ] Detail panel
  - [ ] Stats dashboard

**Routes:**
```
/audit                     # Audit log viewer
/audit/:logId             # Log entry details
/audit/stats              # Audit statistics
```

### 4.5 Quota Monitoring
**신규 기능**

**Tasks:**
- [ ] Usage Dashboard
  - [ ] Current limits display
  - [ ] Usage meters (storage, requests, bandwidth)
  - [ ] Usage trends chart
- [ ] Rate Limit Status
  - [ ] Current window status
  - [ ] Remaining requests
  - [ ] Reset time

**Routes:**
```
/settings/quota            # Quota overview
/settings/quota/usage      # Detailed usage
```

---

## Phase 5: Polish & Performance (v1.0.x)

**목표**: Production-ready 품질

### 5.1 Performance Optimization
- [x] Virtual scrolling for large datasets (@tanstack/react-virtual 구현됨)
- [x] Query result caching (@tanstack/react-query 구현됨)
- [ ] Optimistic updates
  - [ ] Data mutations에 useMutation + optimisticUpdate 적용
  - [ ] Rollback on error 처리
- [x] Lazy loading for routes (React Router 구현됨)
- [ ] Bundle optimization
  - [ ] Vite bundle analyzer 추가
  - [ ] Tree shaking 확인
  - [ ] Chunk splitting 최적화

### 5.2 UX Enhancement
- [x] Keyboard shortcuts (custom hook)
  - [x] Global shortcuts: Cmd/Ctrl+K (command palette), Cmd/Ctrl+Shift+N (new connection), Cmd/Ctrl+Shift+T (toggle theme)
  - [x] Navigation: Cmd/Ctrl+1-8 (sidebar items)
  - [x] Help dialog: ? key to toggle keyboard shortcuts help
- [x] Command palette (cmdk)
  - [x] 패키지 설치: cmdk
  - [x] CommandPalette 컴포넌트 생성
  - [x] Actions: Navigate, Theme toggle, Switch connections, Quick actions
- [x] Dark/Light theme toggle
  - [x] ThemeProvider (zustand + persist)
  - [x] CSS variables 기반 테마 시스템 (OKLCH colors)
  - [x] localStorage 테마 저장
  - [x] System preference 감지 및 자동 적용
- [ ] Responsive layout
  - [ ] Sidebar collapse on mobile/narrow
  - [ ] DataGrid horizontal scroll
  - [ ] Modal/Dialog 반응형
- [ ] Accessibility (WCAG 2.1 AA)
  - [ ] Focus management
  - [ ] ARIA labels
  - [ ] Screen reader 지원
  - [ ] Color contrast 검증

### 5.3 Testing & Quality
- [x] Unit tests (Vitest)
  - [x] 패키지 설치: vitest, @testing-library/react, @testing-library/user-event, jsdom
  - [x] vitest.config.ts 설정
  - [x] Store 테스트 (themeStore, toastStore)
  - [x] Hook 테스트 (useKeyboardShortcuts)
  - [ ] 핵심 컴포넌트 테스트 (Button, Input, DataGrid)
  - [ ] API client 테스트
- [x] E2E tests (Playwright)
  - [x] 패키지 설치: @playwright/test, playwright
  - [x] playwright.config.ts 설정
  - [x] App loading 테스트
  - [x] Navigation 테스트
  - [x] Command palette 테스트
  - [x] Keyboard shortcuts 테스트
  - [ ] Critical path 테스트: Connection → Table → Data CRUD
- [ ] Storybook for components
  - [ ] 패키지 설치: @storybook/react-vite
  - [ ] UI 컴포넌트 stories
  - [ ] Design system 문서화
- [ ] Error boundary & recovery
  - [ ] Global ErrorBoundary 개선
  - [ ] Route-level error boundaries
  - [ ] Retry 메커니즘
- [x] Comprehensive error messages
  - [x] Toast 알림 시스템 (toastStore + ToastContainer)
  - [x] API 에러 핸들링 유틸리티 (api-error.ts)
  - [ ] Validation 에러 표시

### 5.4 Documentation
- [ ] User guide (desk/docs/USER_GUIDE.md)
  - [ ] Getting started
  - [ ] Feature walkthroughs
  - [ ] Screenshots
- [ ] Keyboard shortcuts reference (desk/docs/KEYBOARD_SHORTCUTS.md)
- [ ] Troubleshooting guide (desk/docs/TROUBLESHOOTING.md)
- [ ] Release notes (desk/CHANGELOG.md)

---

## Timeline Summary

| Phase | Version | Focus | Est. Effort |
|-------|---------|-------|-------------|
| 1 | v0.2.x | Foundation & Core | 4-6 weeks |
| 2 | v0.3.x | Data Operations | 4-5 weeks |
| 3 | v0.4.x | Enterprise Features | 6-8 weeks |
| 4 | v0.5.x | Security & Compliance | 4-6 weeks |
| 5 | v1.0.x | Polish & Performance | 3-4 weeks |

**Total: 21-29 weeks for full feature parity**

---

## Technical Stack

### Current
- Electron 32.x
- React 18.x
- TypeScript 5.x
- Tailwind CSS 3.x
- Zustand (state)
- Radix UI (components)

### Already Implemented
- react-router-dom v6 (routing) ✅
- @tanstack/react-query (data fetching) ✅
- @tanstack/react-table (table) ✅
- @tanstack/react-virtual (virtualization) ✅

### Additions for Phase 5
- cmdk (command palette) ✅
- Custom keyboard shortcuts hook ✅
- vitest + @testing-library/react (unit testing) ✅
- @playwright/test (E2E testing) ✅
- @storybook/react-vite (component docs) - Pending
- rollup-plugin-visualizer (bundle analysis) - Pending

---

## Success Metrics

### Phase 1 ✅
- [x] 100% Schema API coverage
- [x] 100% Data API coverage
- [x] Clean navigation structure

### Phase 2 ✅
- [x] Aggregation with visualization
- [x] Import 10k+ rows successfully
- [x] Export to all formats

### Phase 3 ✅
- [x] Multi-org support working
- [x] Webhooks with monitoring
- [x] Backup/restore cycle tested

### Phase 4 ✅
- [x] SSO config UI working
- [x] RLS policies UI working
- [x] Audit log searchable
- [x] Quota dashboard

### Phase 5 (In Progress)
- [ ] <100ms UI response time (Lighthouse CI)
- [ ] 80%+ unit test coverage
- [x] Unit tests setup with 22 passing tests
- [x] E2E test setup with Playwright
- [ ] WCAG 2.1 AA compliant (axe-core validation)
- [x] Command palette fully functional
- [x] Dark/Light theme working with system preference
- [x] Keyboard shortcuts with help dialog
- [x] Toast notification system
