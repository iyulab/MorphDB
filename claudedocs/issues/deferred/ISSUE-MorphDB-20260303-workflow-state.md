# #11 Workflow 상태 관리 (승인 워크플로우)

**Priority**: Low (v1.x 로드맵)
**Deferred reason**: 현재 `_row_state`는 draft/valid/error 3상태만 지원. 커스텀 상태 전이(submitted → reviewing → approved → published)는 v1.x 이후 범위.

## 현황
- Extension 컬럼의 Workflow(`_status`, `_published_at`)가 v1.x로 보류 중
- 현재 `_row_state`로 기본적인 초안/유효/에러 상태만 관리 가능

## 요구사항
- 커스텀 상태 전이 규칙 정의 API
- 상태 전이 시 검증/훅 실행
- 상태 기반 RLS (Row-Level Security) 연동
