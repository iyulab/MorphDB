# MorphDB System Columns 설계

> **Status**: Phase 18.6 - Accepted
> **Author**: Project Team
> **Created**: 2025-12-29

## 개요

MorphDB는 런타임 스키마 유연성을 제공하면서도 일관된 데이터 관리를 위해 시스템 컬럼을 도입한다. 이 문서는 시스템 컬럼의 구조, 처리 방식, 그리고 확장 전략을 정의한다.

## 설계 철학

### Virtual Constraint 접근법

MorphDB는 일부 웹 프레임워크가 Virtual DOM을 활용하는 것처럼, 물리적 제약과 시스템 레이어 처리를 전략적으로 분리한다.

| 처리 방식 | 적용 기준 | 예시 |
|-----------|-----------|------|
| **Physical** | 성능에 직접 기여, 데이터 무결성에 치명적, 변경 빈도 낮음 | PK, 인덱스, 타임스탬프 |
| **Virtual** | 유연성 필요, 런타임 변경 빈번, 비즈니스 로직 의존 | FK, NOT NULL, UNIQUE, CHECK |

이 접근법의 핵심 이점:

- **락 최소화**: FK 등 물리적 제약으로 인한 DDL/DML 락 회피
- **스키마 변경 자유도**: 런타임에 제약 조건 추가/제거 가능
- **Bulk 작업 최적화**: 대량 import 시 검증 일시 비활성화 가능
- **Soft Delete 통합**: 애플리케이션 레벨 삭제와 자연스러운 호환

---

## 시스템 컬럼 구조

### 계층 구조

```
┌─────────────────────────────────────────────────────────────────┐
│  Core (항상 존재)                                               │
│  ───────────────                                                │
│  _id, _created_at, _updated_at                                  │
├─────────────────────────────────────────────────────────────────┤
│  Standard (기본 활성화, 비활성화 가능)                          │
│  ─────────────────────────────────                              │
│  _version, _created_by, _updated_by                             │
├─────────────────────────────────────────────────────────────────┤
│  Optional (명시적 활성화 필요)                                  │
│  ────────────────────────────                                   │
│  Soft Delete, Ownership, Hierarchy, Source Tracking             │
├─────────────────────────────────────────────────────────────────┤
│  Extension (플러그인 방식 확장) - v1.x 이후                     │
│  ─────────────────────────────                                  │
│  Workflow, Search, Analytics, ACL, Localization 등              │
└─────────────────────────────────────────────────────────────────┘
```

---

### Core 컬럼

모든 테이블에 자동 포함되며 비활성화할 수 없다.

| 컬럼 | 타입 | 설명 | 처리 방식 |
|------|------|------|-----------|
| `_id` | UUID v7 | Primary Key, 시간순 정렬 가능 | Physical |
| `_created_at` | Timestamp | 생성 시각, 불변 | Physical (DB 트리거) |
| `_updated_at` | Timestamp | 최종 수정 시각 | Physical (DB 트리거) |

**설계 근거**:
- `_id`: PK는 인덱스, JOIN 최적화 등 PostgreSQL 내부 최적화의 핵심
- 타임스탬프: DB 레벨 트리거가 애플리케이션 버그와 무관하게 신뢰성 보장

---

### Standard 컬럼

기본적으로 활성화되며, 테이블 생성 시 명시적으로 비활성화할 수 있다.

| 컬럼 | 타입 | 설명 | 처리 방식 |
|------|------|------|-----------|
| `_version` | Integer | 낙관적 락 버전 | Physical (컬럼) + Virtual (검증 로직) |
| `_created_by` | UUID | 생성자 ID | Virtual (시스템 레이어 주입) |
| `_updated_by` | UUID | 최종 수정자 ID | Virtual (시스템 레이어 주입) |

**설계 근거**:
- `_version`: 동시 수정 충돌 감지에 필수, 컬럼 자체는 물리적이나 검증은 시스템 레이어
- User Audit: 사용자 컨텍스트는 API 레이어에서만 확보 가능

---

### Optional 컬럼

특정 기능이 필요한 테이블에만 명시적으로 활성화한다.

#### Soft Delete

| 컬럼 | 타입 | 설명 |
|------|------|------|
| `_deleted_at` | Timestamp | 삭제 시각 (NULL이면 활성) |
| `_deleted_by` | UUID | 삭제자 ID |

- 모든 SELECT에 자동 필터 적용 (`_deleted_at IS NULL`)
- 필요 시 삭제된 데이터 포함 조회 옵션 제공

#### Ownership

| 컬럼 | 타입 | 설명 |
|------|------|------|
| `_owner_id` | UUID | 레코드 소유자 |

- Row-level 권한 제어의 기반
- 소유자 기반 자동 필터링 지원

#### Hierarchy

| 컬럼 | 타입 | 설명 |
|------|------|------|
| `_parent_id` | UUID | 부모 레코드 참조 |
| `_sort_order` | Integer | 동일 부모 내 정렬 순서 |

- 트리 구조 데이터 지원 (폴더, 카테고리, 페이지 등)
- 드래그앤드롭 정렬 지원

#### Source Tracking

| 컬럼 | 타입 | 설명 |
|------|------|------|
| `_source_id` | Text | 외부 시스템 원본 ID |

- 외부 시스템 연동 시 원본 식별
- Upsert 및 동기화 충돌 해결에 활용

#### Row-State (v0.12.0+)

| 컬럼 | 타입 | 설명 |
|------|------|------|
| `_row_state` | Enum | 행 상태: `draft`, `valid`, `error` |
| `_row_errors` | JSONB | 유효성 오류 상세 (error 상태용) |

- **draft**: 저장됨, 유효성 검증 건너뜀 (NOT NULL/CHECK 미적용)
- **valid**: 완전한 레코드, 모든 제약 검증 통과
- **error**: 저장됨, 유효성 오류 있음

**활용 시나리오**:
```
표 붙여넣기 → Draft Insert (검증 없음) → UI 편집 → Finalize
                                                    ↓
                                          valid (성공) / error (실패)
```

**_row_errors 형식**:
```json
[
  { "column": "email", "error": "required", "message": "이메일은 필수입니다" },
  { "column": "age", "error": "check_failed", "message": "age > 0 위반" }
]
```

- 스프레드시트 스타일 붙여넣기 지원
- 컬럼 순서 입력 (Column-first Input) 지원
- 대량 데이터 임시 저장 후 일괄 검증

---

### Extension 컬럼 (v1.x 이후)

플러그인 방식으로 등록하여 필요한 테이블에 적용한다. 초기 버전에서는 정의만 해두고, 수요에 따라 구현한다.

| Extension | 컬럼 | 용도 |
|-----------|------|------|
| **Workflow** | `_status`, `_published_at`, `_archived_at` | 컨텐츠 상태 관리 |
| **Temporal** | `_effective_from`, `_effective_until`, `_expires_at` | 시점 데이터, TTL |
| **Search** | `_tags`, `_search_vector`, `_embedding` | 검색 최적화, AI 검색 |
| **Analytics** | `_view_count`, `_last_accessed_at` | 사용 통계 |
| **ACL** | `_acl`, `_visibility`, `_shared_with` | 세분화된 접근 제어 |
| **Localization** | `_locale`, `_translation_of` | 다국어 지원 |
| **Compliance** | `_retention_until`, `_data_classification` | 규정 준수 |

---

## 제약 조건 처리 전략

### Physical vs Virtual 매트릭스

| 제약/기능 | Physical | Virtual | 결정 근거 |
|-----------|:--------:|:-------:|-----------|
| Primary Key | ✓ | | 성능, 무결성 핵심 |
| Foreign Key | | ✓ | 락 회피, 유연성 |
| NOT NULL | △ | ✓ | 마이그레이션 용이성 |
| UNIQUE | △ (인덱스) | ✓ (검증) | 조건부 유니크 지원 |
| CHECK | | ✓ | 비즈니스 로직 의존 |
| DEFAULT | △ (DB 함수) | ✓ | 컨텍스트 의존 여부 |
| Index | ✓ | (관리) | 성능 목적 |
| Cascade | | ✓ | 예측 가능성, soft delete 호환 |

△ = 상황에 따라 선택적 적용

### Virtual 제약의 장점

1. **런타임 유연성**: 서비스 중단 없이 제약 조건 변경
2. **Bulk 작업 최적화**: 대량 import 시 검증 지연 또는 비활성화
3. **조건부 적용**: Soft delete 제외 UNIQUE 등 복잡한 조건 지원
4. **Cross-table 검증**: 다른 테이블 참조하는 복잡한 규칙 구현 가능

---

## Use Case별 권장 구성

| Use Case | Core | Standard | Optional |
|----------|:----:|:--------:|----------|
| **기본 CRUD** | ✓ | ✓ | - |
| **협업 문서** | ✓ | ✓ | Soft Delete, Ownership |
| **계층 데이터** | ✓ | ✓ | Hierarchy |
| **외부 연동** | ✓ | ✓ | Source Tracking |
| **CMS** | ✓ | ✓ | Soft Delete + Workflow Extension |
| **파일 시스템** | ✓ | ✓ | Hierarchy, Ownership |

---

## 명명 규칙

- 모든 시스템 컬럼은 `_` prefix 사용
- 시스템 컬럼은 논리명과 물리명이 동일 (해시 변환 없음)
- 사용자 정의 컬럼은 `_` prefix 사용 불가

이를 통해:
- 시스템 컬럼과 사용자 컬럼의 명확한 구분
- 디버깅 시 물리 테이블에서 시스템 컬럼 즉시 식별 가능
- API 응답에서 시스템 필드 필터링 용이

---

## 구현 연관 파일

### 현재 구현 (v0.12.0)

| 컴포넌트 | 파일 | 역할 |
|----------|------|------|
| TableMetadata | `Core/Models/TableMetadata.cs` | 시스템 컬럼 플래그 |
| SystemColumns | `Core/Models/SystemColumns.cs` | 시스템 컬럼 상수 및 RowStateValue enum |
| TimestampApplier | `Pipeline/Transformers/TimestampApplier.cs` | `_created_at`, `_updated_at` |
| VersionApplier | `Pipeline/Transformers/VersionApplier.cs` | `_version` |
| AuditFieldApplier | `Pipeline/Transformers/AuditFieldApplier.cs` | `_created_by`, `_updated_by` |
| SoftDeleteApplier | `Pipeline/Transformers/SoftDeleteApplier.cs` | `_deleted_at` |
| RowStateApplier | `Pipeline/Transformers/RowStateApplier.cs` | `_row_state`, `_row_errors` |

### 추가 구현 필요 (Phase 18.6)

| 컴포넌트 | 작업 | 우선순위 |
|----------|------|----------|
| DdlBuilder | Core 컬럼 자동 생성 | 🔴 Critical |
| ColumnMetadata | `_` prefix 검증 | 🟡 High |
| CreateTableRequest | SystemColumnOptions 노출 | 🟡 High |
| OwnershipApplier | `_owner_id` 처리 | 🟢 Normal |
| HierarchyApplier | `_parent_id`, `_sort_order` | 🟢 Normal |
| SourceTrackingApplier | `_source_id` 처리 | 🟢 Normal |

---

## 향후 확장 고려사항

1. **Extension Registry**: 플러그인 등록 및 관리 체계 (v1.x)
2. **Migration Path**: Optional → Extension 전환 시 데이터 마이그레이션
3. **Performance Monitoring**: 시스템 컬럼으로 인한 오버헤드 측정
4. **API Exposure Control**: 시스템 컬럼의 API 노출 수준 설정
