# MorphDB Dogfooding 개선 로드맵

**기준 이슈**: `ISSUE-MorphDB-20260303-dogfooding-gaps.md`
**기준 버전**: v0.2.2
**총 사이클**: 9

---

## Phase 1: 완료 (Cycle 1-5)

- [x] Cycle 1: #13, #8, #9 — Quick Wins
- [x] Cycle 2: #1 — UpdateColumn 스키마 진화
- [x] Cycle 3: #2 — Schema Changelog API
- [x] Cycle 4: #10 — Write Pipeline 단위 테스트
- [x] Cycle 5: #5 — .NET Client SDK 보완

## Phase 2: Accept 항목 (Cycle 6-9)

## Cycle 6: Batch DDL 트랜잭션 원자성 (#3 Critical)

**대상 이슈**: #3
- Schema batch DDL 트랜잭션 래핑 검증
- 부분 실패 시 전체 롤백 보장
- 테스트 추가

## Cycle 7: REST 필터 OR 조건 (#4 High)

**대상 이슈**: #4
- `POST /api/data/{table}/query` 엔드포인트에 JSON 기반 복합 필터 지원
- `{"and": [{"or": [...]}]}` 구조 지원

## Cycle 8: Attachment 타입 구현 (#6 High)

**대상 이슈**: #6
- Attachment 타입을 JSONB 기반 메타데이터(url, filename, size, mimeType) 저장으로 구현
- 파이프라인 검증 추가

## Cycle 9: Data Seeding API (#7 High)

**대상 이슈**: #7
- `POST /api/data/{table}/seed` idempotent bulk upsert
- 기존 Upsert의 편의 래퍼

---

## Deferred (v1.x)

→ `claudedocs/issues/deferred/`로 이동
- #11 Workflow 상태 관리
- #12 Temporal/Snapshot 데이터
