# Cycle 02: UpdateColumn 스키마 진화 — DataType/Nullable/Unique/Check 변경 지원
Date: 2026-03-03

## Continuity
- Previous: cycle-01 — Quick Wins
- Inherited Issues: DataController 에러 핸들링 통합 테스트 부재 (DB 필요)
- Inherited Decisions: None
- How Addressed: 통합 테스트는 DB 의존성으로 이번 사이클 범위 외. 추후 해결.

## Scope
- #1 (Critical): UpdateColumn에 DataType, IsNullable, IsUnique, CheckExpression 변경 지원
- DdlBuilder에 BuildAlterColumnType, BuildDropUniqueConstraint 추가
- TypeMapper에 IsTypeCastSafe 타입 호환성 검증 추가
- PostgresSchemaManager.UpdateColumnAsync 확장 — DDL 변경 포함
- MetadataRepository.UpdateColumnMetadataAsync 새 메서드 추가
- SchemaController 매핑 업데이트, ValidationException 처리 추가
- Client SDK AlterColumnRequest에 IsUnique, CheckExpression 추가
- 단위 테스트: DdlBuilder 3개, TypeMapper 4개 추가

## Philosophy Alignment
| Dimension | Score |
|-----------|-------|
| Core Mission Fit | 5/5 |
| Scope Boundaries | 5/5 |
| Architecture Patterns | 4/5 |
| Dependency Direction | 5/5 |

## Research Summary
- PostgreSQL ALTER TABLE ALTER COLUMN TYPE 문법 확인: USING 절로 타입 캐스트
- MorphDB 가상 제약조건 철학: IsNullable/IsUnique는 메타데이터에서 가상으로 관리
- 안전한 타입 변환: integer→bigint→numeric (widening), *→text (모든 타입을 텍스트로)

## Implementation
### Files Changed (10 files)
- `src/MorphDB.Core/Abstractions/ISchemaManager.cs` — UpdateColumnRequest에 DataType?, IsNullable?, IsUnique?, CheckExpression? 추가
- `src/MorphDB.Npgsql/Ddl/DdlBuilder.cs` — BuildAlterColumnType, BuildDropUniqueConstraint 추가
- `src/MorphDB.Npgsql/Infrastructure/TypeMapper.cs` — IsTypeCastSafe 타입 호환성 검증 추가
- `src/MorphDB.Npgsql/Repositories/IMetadataRepository.cs` — UpdateColumnMetadataAsync 인터페이스 추가
- `src/MorphDB.Npgsql/Repositories/MetadataRepository.cs` — UpdateColumnMetadataAsync 구현
- `src/MorphDB.Npgsql/Services/PostgresSchemaManager.cs` — UpdateColumnAsync 확장 + ApplyColumnDdlChangesAsync 새 메서드
- `src/MorphDB.Service/Controllers/SchemaController.cs` — 매핑 + ValidationException 처리
- `src/MorphDB.Service/Models/Api/ApiModels.cs` — UpdateColumnApiRequest에 Type, Nullable, Unique, Check 추가
- `src/MorphDB.Client/Models/SchemaModels.cs` — AlterColumnRequest에 IsUnique, CheckExpression 추가
- `tests/MorphDB.Tests/Unit/DdlBuilderTests.cs` — 3개 테스트 추가
- `tests/MorphDB.Tests/Unit/TypeMapperTests.cs` — 새 파일, 4개 테스트

### Key Decisions
- 안전하지 않은 타입 변환 (text→int 등)은 ValidationException으로 거부
- IsNullable 변경은 물리적 NOT NULL이 아닌 가상 제약조건이므로 메타데이터만 갱신
- IsUnique 변경은 물리적 UNIQUE 제약조건을 추가/제거 (물리적 DDL)
- CheckExpression은 가상 제약조건이므로 메타데이터만 갱신
- 시스템 컬럼의 타입/제약조건 변경 시도는 ValidationException으로 차단

## Test Results
- Build: PASSED (0 Warning, 0 Error)
- Unit Tests: 309 passed, 0 failed (기존 292 + 새 17)

## Evaluation
| Criterion | Score | Notes |
|-----------|-------|-------|
| Correctness | 8/10 | DDL 변경 로직 완성. 실제 DB 검증은 통합 테스트 필요. |
| Architecture | 9/10 | 기존 패턴 준수, 가상/물리적 변경 분리 |
| Philosophy Alignment | 10/10 | 논리-물리 분리 원칙 준수 |
| Test Quality | 7/10 | 단위 테스트 추가했으나 통합 테스트 부재 |
| Documentation | 7/10 | API.md에 UpdateColumn 새 필드 문서 미갱신 |
| Code Quality | 9/10 | 깔끔한 구현, 트랜잭션 보호 |
| **Average** | **8.3/10** | |

## Carry-Forward
- Unresolved Issues: API.md에 UpdateColumn 새 필드 문서 추가 필요; 통합 테스트 부재
- Pending Human Decisions: None
- Next Scope Recommendation: Cycle 3 — Schema Changelog API (#2 Critical)
