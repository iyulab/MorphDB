# Cycle 03: Schema Changelog API
Date: 2026-03-03

## Continuity
- Previous: cycle-02 — UpdateColumn 스키마 진화
- Inherited Issues: API.md UpdateColumn 문서 미갱신; 통합 테스트 부재
- Inherited Decisions: None
- How Addressed: API.md에 Changelog 엔드포인트 문서 추가 (UpdateColumn 문서는 여전히 미갱신)

## Scope
- #2 (Critical): Schema 변경 이력 API 추가
- `GET /api/schema/tables/{name}/history` — 테이블별 변경 이력
- `GET /api/schema/changelog?limit=50&offset=0` — 전체 변경 이력
- IChangeLogger에 GetChangelogAsync 메서드 추가
- SchemaController에 IChangeLogger DI 및 2개 엔드포인트 추가
- SchemaChangeApiResponse 응답 모델 추가
- API.md에 changelog 엔드포인트 문서 추가

## Philosophy Alignment
| Dimension | Score |
|-----------|-------|
| Core Mission Fit | 5/5 |
| Scope Boundaries | 5/5 |
| Architecture Patterns | 5/5 |
| Dependency Direction | 5/5 |

## Research Summary
- 기존 _morph_changelog 테이블 구조 및 ChangeLogger 구현 분석
- IChangeLogger에 이미 GetHistoryAsync(tableId) 존재 확인
- ITenantContextAccessor.TenantIdOrNull 사용 패턴 확인

## Implementation
### Files Changed (5 files)
- `src/MorphDB.Npgsql/Services/IChangeLogger.cs` — GetChangelogAsync 인터페이스 추가
- `src/MorphDB.Npgsql/Services/ChangeLogger.cs` — GetChangelogAsync 구현 (페이지네이션)
- `src/MorphDB.Service/Controllers/SchemaController.cs` — IChangeLogger DI, GetTableHistory/GetChangelog 엔드포인트
- `src/MorphDB.Service/Models/Api/ApiModels.cs` — SchemaChangeApiResponse 모델
- `docs/API.md` — Changelog 엔드포인트 문서

### Key Decisions
- 글로벌 changelog는 limit/offset 페이지네이션 (최대 500개)
- 테이블별 히스토리는 테넌트 컨텍스트 필수 (테이블 조회를 통해 tableId 확보)
- SchemaChangeApiResponse에서 Operation을 문자열로 노출 (열거형이 아닌)

## Test Results
- Build: PASSED (0 Warning, 0 Error)
- Unit Tests: 309 passed, 0 failed

## Evaluation
| Criterion | Score | Notes |
|-----------|-------|-------|
| Correctness | 8/10 | API 구현 완료. DB 통합 테스트 없음. |
| Architecture | 9/10 | 기존 IChangeLogger 인프라 활용, 깔끔한 추가 |
| Philosophy Alignment | 10/10 | 감사 가능성 원칙 강화 |
| Test Quality | 6/10 | changelog 단위 테스트 추가 안 됨 (DB 의존적) |
| Documentation | 9/10 | API.md 갱신 완료 |
| Code Quality | 9/10 | 깔끔한 구현 |
| **Average** | **8.5/10** | |

## Carry-Forward
- Unresolved Issues: API.md UpdateColumn 새 필드 문서; 통합 테스트 전반 부재
- Pending Human Decisions: None
- Next Scope Recommendation: Cycle 4 — Write Pipeline 단위 테스트 (#10)
