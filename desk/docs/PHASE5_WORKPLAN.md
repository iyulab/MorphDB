# Phase 5 Work Plan (v1.0.x)

> **Created**: 2025-12-30
> **Target**: Production-ready MorphDB Desk
> **Philosophy**: 데스크탑 앱 사용자 경험 최적화 + 품질 보증

---

## Project Philosophy Alignment

### MorphDB Core Philosophy
- Runtime-flexible database with schema abstraction
- Multi-project architecture
- Production-ready, enterprise-grade

### Desk Philosophy
- **완전한 기능 커버리지**: 100% API coverage (✅ 달성)
- **프로덕션 품질**: 테스트, 안정성, 성능
- **데스크탑 표준 UX**: 키보드 중심, 빠른 접근성

---

## Sub-Phase Breakdown

### Phase 5.1: Theme System (Commit #1)
**Priority**: 🔴 High - 가장 많이 요청되는 기능

**Tasks**:
1. ThemeProvider 구현
   - 파일: `src/renderer/providers/ThemeProvider.tsx`
   - CSS variables 기반 light/dark 토큰
   - localStorage 저장 + system preference 감지
2. Theme toggle 컴포넌트
   - 파일: `src/renderer/components/ui/ThemeToggle.tsx`
   - Settings 페이지에 추가
3. CSS 변수 시스템
   - 파일: `src/renderer/styles/themes.css`
   - Tailwind 설정 업데이트

**Dependencies**: 없음
**Commit Message**: `feat(desk): Phase 5.1 - Add dark/light theme toggle`

---

### Phase 5.2: Command Palette (Commit #2)
**Priority**: 🔴 High - 파워 유저 필수 기능

**Tasks**:
1. cmdk 패키지 설치
   ```bash
   npm install cmdk
   ```
2. CommandPalette 컴포넌트 생성
   - 파일: `src/renderer/components/CommandPalette.tsx`
   - Cmd/Ctrl+K로 열기
   - 카테고리: Navigation, Actions, Search
3. 액션 정의
   - Navigate to: Explorer, Projects, Views, Webhooks, etc.
   - Create: Table, Project, Webhook
   - Quick actions: Refresh, Export, Import

**Dependencies**: Phase 5.1 (theme 적용)
**Commit Message**: `feat(desk): Phase 5.2 - Add command palette (Cmd/Ctrl+K)`

---

### Phase 5.3: Keyboard Shortcuts (Commit #3)
**Priority**: 🟡 Medium - 데스크탑 앱 표준

**Tasks**:
1. react-hotkeys-hook 패키지 설치
   ```bash
   npm install react-hotkeys-hook
   ```
2. Global shortcuts 구현
   - 파일: `src/renderer/hooks/useGlobalShortcuts.ts`
   - Cmd/Ctrl+N: New (context-aware)
   - Cmd/Ctrl+S: Save (when editing)
   - Cmd/Ctrl+1-9: Navigation
3. DataGrid shortcuts
   - 파일: `src/renderer/components/grid/DataGrid.tsx` 수정
   - Arrow keys: Cell navigation
   - Enter: Edit mode
   - Escape: Cancel
   - Delete: Delete row (with confirmation)
4. Shortcuts 참조 다이얼로그
   - 파일: `src/renderer/components/ShortcutsDialog.tsx`
   - ? 키로 열기

**Dependencies**: Phase 5.2 (command palette에서 shortcuts 표시)
**Commit Message**: `feat(desk): Phase 5.3 - Add keyboard shortcuts`

---

### Phase 5.4: Testing Infrastructure (Commit #4)
**Priority**: 🟡 Medium - 품질 보증 기반

**Tasks**:
1. Vitest 설정
   ```bash
   npm install -D vitest @testing-library/react @testing-library/user-event jsdom @vitejs/plugin-react
   ```
   - 파일: `vitest.config.ts`
   - 파일: `src/renderer/test/setup.ts`
2. 핵심 컴포넌트 테스트
   - 파일: `src/renderer/components/ui/__tests__/Button.test.tsx`
   - 파일: `src/renderer/components/ui/__tests__/Input.test.tsx`
3. API client 테스트
   - 파일: `src/renderer/lib/__tests__/api.test.ts`
   - Mock fetch, error handling
4. Store 테스트
   - 파일: `src/renderer/stores/__tests__/connectionStore.test.ts`

**Dependencies**: 없음 (병렬 가능)
**Commit Message**: `test(desk): Phase 5.4 - Add Vitest unit testing infrastructure`

---

### Phase 5.5: E2E Testing (Commit #5)
**Priority**: 🟢 Normal - Critical path 검증

**Tasks**:
1. Playwright 설정
   ```bash
   npm install -D @playwright/test
   npx playwright install
   ```
   - 파일: `playwright.config.ts`
2. Critical path 테스트
   - 파일: `e2e/connection.spec.ts` - 연결 생성/테스트
   - 파일: `e2e/schema.spec.ts` - 테이블 CRUD
   - 파일: `e2e/data.spec.ts` - 데이터 CRUD
3. CI 통합
   - 파일: `.github/workflows/desk-e2e.yml`

**Dependencies**: Phase 5.4
**Commit Message**: `test(desk): Phase 5.5 - Add Playwright E2E tests`

---

### Phase 5.6: Error Handling & Toast (Commit #6)
**Priority**: 🟡 Medium - UX 개선

**Tasks**:
1. Toast 알림 시스템
   - 파일: `src/renderer/components/ui/Toast.tsx`
   - 파일: `src/renderer/providers/ToastProvider.tsx`
   - Success, Error, Warning, Info 타입
2. API 에러 표준화
   - 파일: `src/renderer/lib/api.ts` 수정
   - MorphDBError 클래스
   - User-friendly 메시지 매핑
3. ErrorBoundary 개선
   - 파일: `src/renderer/components/ErrorBoundary.tsx` 수정
   - Retry 버튼
   - Error reporting

**Dependencies**: 없음
**Commit Message**: `feat(desk): Phase 5.6 - Improve error handling with toast notifications`

---

### Phase 5.7: Documentation (Commit #7)
**Priority**: 🟢 Normal - 릴리스 준비

**Tasks**:
1. User Guide
   - 파일: `docs/USER_GUIDE.md`
   - Getting started, Feature walkthroughs
2. Keyboard Shortcuts Reference
   - 파일: `docs/KEYBOARD_SHORTCUTS.md`
   - 전체 shortcuts 목록
3. CHANGELOG
   - 파일: `CHANGELOG.md`
   - v0.1.0 ~ v1.0.0 변경사항
4. README 업데이트
   - 파일: `README.md`
   - 스크린샷 추가

**Dependencies**: Phase 5.3 (shortcuts 완료 후)
**Commit Message**: `docs(desk): Phase 5.7 - Add user documentation`

---

## Execution Order

```
Phase 5.1 (Theme)
    ↓
Phase 5.2 (Command Palette) ──┬── Phase 5.4 (Vitest) [병렬]
    ↓                         ↓
Phase 5.3 (Shortcuts)    Phase 5.5 (E2E)
    ↓                         ↓
Phase 5.6 (Error Handling) ───┘
    ↓
Phase 5.7 (Documentation)
```

---

## Session Planning

### Session 1: Theme + Command Palette
- Phase 5.1: Theme System
- Phase 5.2: Command Palette
- **예상 시간**: 2-3시간

### Session 2: Shortcuts + Testing
- Phase 5.3: Keyboard Shortcuts
- Phase 5.4: Vitest Setup
- **예상 시간**: 2-3시간

### Session 3: E2E + Polish
- Phase 5.5: E2E Testing
- Phase 5.6: Error Handling
- **예상 시간**: 2-3시간

### Session 4: Documentation + Release
- Phase 5.7: Documentation
- Final review & v1.0.0 tag
- **예상 시간**: 1-2시간

---

## Success Criteria

| Metric | Target | Validation |
|--------|--------|------------|
| Theme toggle | Dark/Light 전환 | Manual test |
| Command palette | Cmd+K 동작 | Manual test |
| Keyboard shortcuts | 10+ shortcuts | KEYBOARD_SHORTCUTS.md |
| Unit test coverage | 80%+ | Vitest coverage report |
| E2E critical paths | 3+ scenarios | Playwright report |
| Error handling | Toast 표시 | Manual test |
| Documentation | 3 docs | File existence |

---

## Resources

### Research References
- [React Performance Optimization 2025](https://dev.to/alex_bobes/react-performance-optimization-15-best-practices-for-2025-17l9)
- [Electron Performance](https://www.electronjs.org/docs/latest/tutorial/performance)
- [TanStack Virtual](https://tanstack.com/virtual/latest)
- [cmdk](https://cmdk.paco.me/)

### Related Files
- Current Roadmap: `desk/docs/DEVELOPMENT_ROADMAP.md`
- Main ROADMAP: `docs/ROADMAP.md`
- Package.json: `desk/package.json`
