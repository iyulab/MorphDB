# #12 Temporal/Snapshot 데이터 지원

**Priority**: Low (v1.x 로드맵)
**Deferred reason**: Extension 컬럼 `_effective_from`, `_effective_until` 계획 존재하나 v1.x 이후로 보류. Point-in-time query(`AS OF`) 미지원.

## 현황
- Temporal 관련 Extension 컬럼 설계만 존재
- 구현 및 쿼리 지원 미착수

## 요구사항
- `_effective_from`, `_effective_until` Extension 컬럼 활성화
- Point-in-time 쿼리 지원 (`AS OF timestamp`)
- 히스토리 테이블 자동 생성/관리
