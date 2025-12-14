# MorphDB Development Roadmap

## Overview

MorphDB는 12개 Phase로 개발됩니다. 각 Phase는 독립적으로 테스트 가능한 기능 단위입니다.

## Phase 0: Foundation ✅ Completed

**목표**: 솔루션 구조 및 개발 환경 구성

- [x] Solution structure (MorphDB.sln)
- [x] Central Package Management (Directory.Packages.props)
- [x] MorphDB.Core - Core abstractions and interfaces
- [x] MorphDB.Npgsql - PostgreSQL provider skeleton
- [x] MorphDB.Service - ASP.NET Core Web API skeleton
- [x] MorphDB.Tests - xUnit test framework
- [x] Docker Compose development environment
- [x] GitHub Actions CI/CD pipeline
- [x] Code style (.editorconfig)

**핵심 구성요소**:
- `ISchemaManager`, `ISchemaMapping`, `INameHasher` 인터페이스
- `IMorphQueryBuilder`, `IMorphDataService` 쿼리 추상화
- `Sha256NameHasher` - 논리명→물리명 해시 생성
- `PostgresAdvisoryLockManager` - DDL 직렬화를 위한 Advisory Lock

---

## Phase 1: Core Schema Management ✅ Completed

**목표**: 동적 스키마 생성 및 관리

### 1.1 SchemaManager 구현
- [x] `ISchemaManager` 구현 (`PostgresSchemaManager`)
- [x] 테이블 CRUD (CREATE, ALTER, DROP)
- [x] 컬럼 CRUD (ADD, MODIFY, DROP)
- [x] 시스템 테이블 동기화 (`MetadataRepository`)

### 1.2 DDL 안전성
- [x] Advisory Lock 통합 (`PostgresAdvisoryLockManager`)
- [x] 트랜잭션 기반 DDL
- [x] DdlBuilder - DDL SQL 생성

### 1.3 변경 로깅
- [x] `ChangeLogger` - _morph_changelog 기록
- [x] `SchemaChangeEntry` - 변경 이력 모델

### 1.4 테스트
- [x] 단위 테스트 (`DdlBuilderTests`)
- [x] 통합 테스트 (`SchemaManagerTests`, `MetadataRepositoryTests`)

---

## Phase 2: Data Operations ✅ Completed

**목표**: 기본 CRUD 데이터 조작

### 2.1 DataService 구현
- [x] `IMorphDataService` 구현 (`PostgresDataService`)
- [x] INSERT, UPDATE, DELETE 작업
- [x] 배치 DML 지원 (InsertBatchAsync)
- [x] Upsert 지원 (INSERT ... ON CONFLICT)

### 2.2 논리명→물리명 변환
- [x] DmlBuilder - DML SQL 생성
- [x] 자동 네이밍 변환 (논리명 ↔ 물리명)
- [x] tenant_id 자동 주입

### 2.3 타입 매핑
- [x] MorphDataType → PostgreSQL 변환 (`TypeMapper`)
- [x] JSONB 타입 직렬화/역직렬화
- [x] 값 검증 및 변환

### 2.4 테스트
- [x] 단위 테스트 (`DmlBuilderTests`)
- [x] 통합 테스트 (`DataServiceTests`)

---

## Phase 3: Query Builder

**목표**: 논리적 쿼리 인터페이스

### 3.1 MorphQueryBuilder 구현
- [ ] `IMorphQueryBuilder` 구현
- [ ] SELECT, WHERE, JOIN, ORDER BY
- [ ] 집계 함수 (COUNT, SUM, AVG)

### 3.2 SqlKata 통합
- [ ] 물리적 쿼리 생성
- [ ] 파라미터 바인딩

### 3.3 페이징
- [ ] Offset 기반 페이징
- [ ] Cursor 기반 페이징

---

## Phase 4: REST API

**목표**: RESTful API 엔드포인트

### 4.1 Schema API
- [ ] POST /api/schema/tables
- [ ] GET/PATCH/DELETE /api/schema/tables/{name}
- [ ] 컬럼, 관계, 인덱스 관리

### 4.2 Data API
- [ ] GET /api/data/{table}
- [ ] POST/PATCH/DELETE /api/data/{table}/{id}
- [ ] 필터, 정렬, 페이징

### 4.3 Batch API
- [ ] POST /api/schema/batch
- [ ] POST /api/data/batch

---

## Phase 5: GraphQL

**목표**: HotChocolate 기반 GraphQL

### 5.1 동적 스키마 생성
- [ ] 테이블 → GraphQL Type 매핑
- [ ] Query, Mutation 자동 생성

### 5.2 관계 해석
- [ ] FK → GraphQL 관계 필드
- [ ] DataLoader 통합

### 5.3 Subscription
- [ ] GraphQL Subscription 지원
- [ ] 변경 이벤트 스트리밍

---

## Phase 6: OData

**목표**: OData v4 프로토콜 지원

### 6.1 EDM 모델
- [ ] 동적 $metadata 생성
- [ ] 엔티티 타입 매핑

### 6.2 쿼리 옵션
- [ ] $filter, $orderby, $top, $skip
- [ ] $select, $expand
- [ ] $count

### 6.3 CUD 작업
- [ ] POST, PATCH, DELETE
- [ ] 배치 요청

---

## Phase 7: Real-time (WebSocket)

**목표**: 실시간 데이터 동기화

### 7.1 SignalR Hub
- [ ] MorphHub 구현
- [ ] 테이블 구독/해제

### 7.2 변경 감지
- [ ] PostgreSQL LISTEN/NOTIFY
- [ ] 변경 이벤트 브로드캐스트

### 7.3 필터링
- [ ] 구독 필터 지원
- [ ] 선택적 필드 전송

---

## Phase 8: Webhook

**목표**: 외부 시스템 연동

### 8.1 웹훅 관리
- [ ] 웹훅 등록/삭제
- [ ] 이벤트 필터링

### 8.2 전송
- [ ] HTTP 콜백 전송
- [ ] HMAC 서명
- [ ] 재시도 로직

### 8.3 모니터링
- [ ] 전송 이력
- [ ] 실패 알림

---

## Phase 9: Bulk Operations

**목표**: 대량 데이터 처리

### 9.1 Import
- [ ] CSV, JSON, NDJSON 파싱
- [ ] 스트리밍 처리
- [ ] Upsert 모드

### 9.2 Export
- [ ] CSV, JSON, XLSX 생성
- [ ] 필터 기반 내보내기

### 9.3 진행률
- [ ] 진행률 추적
- [ ] 취소 지원

---

## Phase 10: Client SDKs

**목표**: 클라이언트 라이브러리

### 10.1 .NET SDK
- [ ] MorphDB.Client 패키지
- [ ] 타입 안전 쿼리 빌더
- [ ] 실시간 구독

### 10.2 TypeScript SDK
- [ ] @morphdb/client 패키지
- [ ] React Query 통합

### 10.3 Python SDK
- [ ] morphdb-python 패키지
- [ ] 비동기 지원

---

## Phase 11: Security

**목표**: 인증 및 접근 제어

### 11.1 API Key 시스템
- [ ] anon-key: 공개 읽기 전용
- [ ] service-key: 전체 접근
- [ ] 키 관리 API

### 11.2 JWT 인증
- [ ] JWT Bearer 토큰
- [ ] 클레임 기반 권한

### 11.3 Row-Level Security
- [ ] RLS 정책 정의
- [ ] 테넌트 격리

---

## Phase 12: Multi-tenant & Deployment

**목표**: 멀티테넌트 및 배포

### 12.1 테넌트 격리
- [ ] 스키마 기반 격리
- [ ] 테넌트별 커넥션 풀

### 12.2 감사 로깅
- [ ] 작업 이력
- [ ] 사용자 추적

### 12.3 배포 구성
- [ ] SaaS Docker Compose (PostgreSQL + Redis + pgAdmin)
- [ ] On-premise Docker Compose (외부 DB 연결)
- [ ] Kubernetes Helm 차트

---

## Version Milestones

| Version | Phases | 목표 |
|---------|--------|------|
| 0.1.0 | 0-3 | Core 기능 완성 |
| 0.2.0 | 4-6 | API 레이어 완성 |
| 0.3.0 | 7-8 | Real-time 기능 |
| 0.4.0 | 9-10 | Bulk & SDKs |
| 0.5.0 | 11-12 | Production Ready (Beta) |

---

## Contributing

각 Phase는 feature branch에서 개발 후 PR로 병합합니다.

```bash
git checkout -b feature/phase-1-schema-manager
# ... 개발 ...
git push origin feature/phase-1-schema-manager
# PR 생성
```
