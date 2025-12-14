# MorphDB

**Runtime-flexible relational database service for PostgreSQL**

MorphDB는 PostgreSQL의 강력함을 유지하면서 런타임에 유연한 스키마 확장을 제공하는 DB 서비스 레이어입니다. Notion Database, Airtable과 같은 동적 데이터 구조가 필요한 애플리케이션의 기반으로 설계되었습니다.

## Why MorphDB?

동적 스키마를 구현하는 일반적인 접근법들:

| 접근 방식 | 문제점 |
|-----------|--------|
| NoSQL | 관계, 트랜잭션, 복잡한 쿼리의 한계 |
| EAV 패턴 | 쿼리 복잡성, 성능 저하 |
| JSON 컬럼 | 인덱싱 제한, 타입 안전성 부재 |
| 동적 DDL | 컬럼명 변경 시 복잡한 마이그레이션 |

MorphDB는 실제 PostgreSQL 스키마 위에 추상화 레이어를 두어 RDB의 모든 이점을 유지하면서 런타임 스키마 변경을 지원합니다.

## Core Features

- **Dynamic Schema** - 런타임 테이블/컬럼 생성, 수정, 삭제
- **Logical Naming** - 물리적 해시명과 논리적 표시명 분리
- **Strong Typing** - PostgreSQL 네이티브 타입 활용
- **Auto API** - 스키마 변경 시 REST/GraphQL/OData 엔드포인트 무중단 자동 생성
- **Real-time** - WebSocket 기반 데이터 변경 구독
- **Webhook** - 외부 시스템 연동을 위한 이벤트 알림
- **Bulk Operations** - 대량 데이터 Import/Export

## Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                        Client Layer                              │
│                                                                  │
│   REST API      GraphQL      OData      WebSocket     Webhook    │
│   /api/v1/*     /graphql     /odata/*   /ws          (outbound)  │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                      MorphDB.Service                             │
│                                                                  │
│  ┌────────────┐ ┌────────────┐ ┌────────────┐ ┌──────────────┐  │
│  │ Schema API │ │  Data API  │ │  Bulk API  │ │ Realtime Hub │  │
│  │   (DDL)    │ │   (DML)    │ │            │ │              │  │
│  └────────────┘ └────────────┘ └────────────┘ └──────────────┘  │
│                              │                                   │
│  ┌───────────────────────────────────────────────────────────┐  │
│  │              Dynamic Endpoint Generator                    │  │
│  │  • 테이블 생성 → 자동으로 endpoint 활성화 (무중단)           │  │
│  │  • GraphQL schema 동적 생성                                │  │
│  │  • OData $metadata 자동 갱신                               │  │
│  └───────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                        System Layer                              │
│                                                                  │
│   _morph_tables      테이블 매핑 및 메타데이터                     │
│   _morph_columns     컬럼 매핑, 타입, 제약조건                     │
│   _morph_relations   테이블 간 관계                               │
│   _morph_indexes     인덱스 매핑                                  │
│   _morph_views       저장된 뷰/필터                               │
│   _morph_enums       열거형 옵션                                  │
│   _morph_webhooks    웹훅 구독 설정                               │
│                                                                  │
│   "고객" → tbl_a7f3b2c1                                          │
│   "이메일" → col_e9d8c7b6                                        │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                        Data Layer                                │
│                                                                  │
│   tbl_a7f3b2c1 (col_e9d8c7b6, col_f1a2b3c4, ...)                │
│                                                                  │
│   * 모든 식별자는 해시 기반                                        │
│   * System Layer 매핑으로만 해석 가능                              │
└─────────────────────────────────────────────────────────────────┘
```

## API Overview

MorphDB는 다양한 클라이언트 요구사항을 충족하기 위해 여러 프로토콜을 지원합니다.

| 프로토콜 | 용도 | 특징 |
|----------|------|------|
| **REST API** | 범용 CRUD, 스키마 관리 | 단순함, 광범위한 호환성 |
| **GraphQL** | 유연한 쿼리, 프론트엔드 | 필요한 필드만 선택, 중첩 관계 |
| **OData** | 엔터프라이즈 연동 | Excel, Power BI, SAP 호환 |
| **WebSocket** | 실시간 동기화 | 협업 편집, 라이브 업데이트 |
| **Webhook** | 외부 시스템 알림 | 자동화, Zapier/n8n 연동 |
| **Bulk API** | 대량 데이터 처리 | Import/Export, 마이그레이션 |

### REST API

```yaml
# Schema API (DDL)
POST   /api/schema/tables                    # 테이블 생성
GET    /api/schema/tables                    # 테이블 목록
GET    /api/schema/tables/{name}             # 테이블 상세
PATCH  /api/schema/tables/{name}             # 테이블 수정
DELETE /api/schema/tables/{name}             # 테이블 삭제

POST   /api/schema/tables/{name}/columns     # 컬럼 추가
PATCH  /api/schema/tables/{name}/columns/{col}  # 컬럼 수정
DELETE /api/schema/tables/{name}/columns/{col}  # 컬럼 삭제

POST   /api/schema/relations                 # 관계 생성
POST   /api/schema/indexes                   # 인덱스 생성
POST   /api/schema/batch                     # 배치 DDL

# Data API (DML) - 테이블별 자동 생성
GET    /api/data/{table}                     # 목록 조회 (필터, 정렬, 페이징)
GET    /api/data/{table}/{id}                # 단건 조회
POST   /api/data/{table}                     # 생성
PATCH  /api/data/{table}/{id}                # 수정
DELETE /api/data/{table}/{id}                # 삭제
POST   /api/data/{table}/query               # 복합 쿼리
POST   /api/data/{table}/batch               # 배치 DML
```

### GraphQL

테이블 생성 시 자동으로 GraphQL 타입과 쿼리/뮤테이션이 생성됩니다.

```graphql
# 자동 생성되는 스키마 예시
type 고객 {
  id: ID!
  이름: String!
  이메일: String
  가입일: DateTime!
  등급: CustomerGrade
  주문: [주문!]!          # 관계 자동 해석
}

type Query {
  고객(id: ID!): 고객
  고객_list(
    filter: 고객Filter
    orderBy: 고객OrderBy
    first: Int
    after: String
  ): 고객Connection!
}

type Mutation {
  고객_create(input: 고객Input!): 고객!
  고객_update(id: ID!, input: 고객Input!): 고객!
  고객_delete(id: ID!): Boolean!
}

type Subscription {
  고객_changed(filter: 고객Filter): 고객ChangeEvent!
}
```

```graphql
# 쿼리 예시
query {
  고객_list(filter: { 등급: { eq: VIP } }, first: 10) {
    edges {
      node {
        이름
        이메일
        주문 {
          주문일
          금액
        }
      }
    }
    pageInfo {
      hasNextPage
      endCursor
    }
  }
}
```

### OData

표준 OData v4 프로토콜을 지원하여 엔터프라이즈 도구와 연동됩니다.

```http
# 메타데이터
GET /odata/$metadata

# 쿼리
GET /odata/고객?$filter=등급 eq 'VIP'&$orderby=가입일 desc&$top=10
GET /odata/고객?$expand=주문&$select=이름,이메일

# CUD
POST /odata/고객
PATCH /odata/고객('id')
DELETE /odata/고객('id')
```

지원 기능: `$filter`, `$orderby`, `$top`, `$skip`, `$select`, `$expand`, `$count`

### WebSocket (Real-time)

실시간 데이터 동기화를 위한 WebSocket 연결을 제공합니다.

```javascript
// 클라이언트 연결
const ws = new WebSocket('wss://api.example.com/ws');

// 테이블 구독
ws.send(JSON.stringify({
  type: 'subscribe',
  table: '고객',
  filter: { 등급: 'VIP' }  // 선택적 필터
}));

// 변경 이벤트 수신
ws.onmessage = (event) => {
  const { type, table, operation, data, previous } = JSON.parse(event.data);
  // type: 'change'
  // operation: 'insert' | 'update' | 'delete'
  // data: 변경된 데이터
  // previous: 이전 데이터 (update/delete 시)
};

// 구독 해제
ws.send(JSON.stringify({
  type: 'unsubscribe',
  table: '고객'
}));
```

SignalR 허브도 지원합니다 (.NET 클라이언트용):

```csharp
var connection = new HubConnectionBuilder()
    .WithUrl("https://api.example.com/morphhub")
    .Build();

await connection.StartAsync();
await connection.InvokeAsync("Subscribe", "고객", filter);

connection.On<ChangeEvent>("OnChange", (e) => {
    Console.WriteLine($"{e.Operation}: {e.Data}");
});
```

### Webhook

데이터 변경 시 외부 시스템으로 HTTP 콜백을 전송합니다.

```http
# 웹훅 등록
POST /api/webhooks
{
  "name": "주문 알림",
  "table": "주문",
  "events": ["insert", "update"],
  "url": "https://external.system/callback",
  "headers": {
    "Authorization": "Bearer xxx"
  },
  "filter": {
    "상태": "완료"
  },
  "secret": "webhook-signing-secret"  // HMAC 서명용
}

# 웹훅 목록
GET /api/webhooks

# 웹훅 삭제
DELETE /api/webhooks/{id}

# 웹훅 테스트
POST /api/webhooks/{id}/test
```

웹훅 페이로드:

```json
{
  "id": "evt_xxxxx",
  "timestamp": "2024-01-15T10:30:00Z",
  "table": "주문",
  "operation": "insert",
  "data": {
    "id": "xxx",
    "고객_id": "yyy",
    "금액": 50000
  },
  "previous": null
}
```

### Bulk API

대량 데이터 처리를 위한 Import/Export 기능을 제공합니다.

```http
# CSV Import
POST /api/bulk/{table}/import
Content-Type: text/csv

이름,이메일,등급
홍길동,hong@example.com,VIP
김철수,kim@example.com,일반

# JSON Import (배열)
POST /api/bulk/{table}/import
Content-Type: application/json

[
  { "이름": "홍길동", "이메일": "hong@example.com" },
  { "이름": "김철수", "이메일": "kim@example.com" }
]

# NDJSON Import (스트리밍)
POST /api/bulk/{table}/import
Content-Type: application/x-ndjson

{"이름":"홍길동","이메일":"hong@example.com"}
{"이름":"김철수","이메일":"kim@example.com"}

# Export
GET /api/bulk/{table}/export?format=csv
GET /api/bulk/{table}/export?format=json
GET /api/bulk/{table}/export?format=xlsx

# Export with filter
GET /api/bulk/{table}/export?format=csv&filter=등급.eq.VIP
```

Import 옵션:

```http
POST /api/bulk/{table}/import?mode=upsert&key=이메일
```

| 옵션 | 설명 |
|------|------|
| `mode=insert` | 삽입만 (기본값) |
| `mode=upsert` | 있으면 업데이트, 없으면 삽입 |
| `mode=replace` | 기존 데이터 삭제 후 삽입 |
| `key={column}` | upsert 시 매칭 키 |

### Batch Operations

여러 작업을 단일 트랜잭션으로 처리합니다.

```http
# Schema Batch (DDL)
POST /api/schema/batch
{
  "transaction": true,
  "operations": [
    {
      "op": "create_table",
      "name": "주문",
      "columns": [
        { "name": "주문번호", "type": "auto_number" },
        { "name": "고객_id", "type": "uuid" },
        { "name": "금액", "type": "decimal", "precision": 10, "scale": 2 }
      ]
    },
    {
      "op": "create_relation",
      "source": { "table": "주문", "column": "고객_id" },
      "target": { "table": "고객", "column": "id" },
      "type": "many-to-one",
      "onDelete": "CASCADE"
    },
    {
      "op": "create_index",
      "table": "주문",
      "columns": ["고객_id", "주문일"],
      "name": "idx_주문_고객_일자"
    }
  ]
}

# Data Batch (DML)
POST /api/data/batch
{
  "transaction": true,
  "operations": [
    { "op": "insert", "table": "고객", "data": { "이름": "홍길동" }, "ref": "cust1" },
    { "op": "insert", "table": "주문", "data": { "고객_id": "$ref:cust1.id", "금액": 50000 } },
    { "op": "update", "table": "고객", "id": "xxx", "data": { "등급": "VIP" } },
    { "op": "delete", "table": "주문", "id": "yyy" }
  ]
}
```

## Scope & Responsibilities

MorphDB는 DB 서비스 레이어로서 명확한 책임 범위를 갖습니다.

### MorphDB 책임

| 영역 | 기능 |
|------|------|
| Schema | 테이블, 컬럼, 관계, 인덱스, 뷰 관리 |
| Naming | logical_name ↔ hash_name 매핑 |
| Type | 강타입 시스템, PostgreSQL 네이티브 타입 |
| Validation | NOT NULL, UNIQUE, CHECK, FK 제약조건 |
| Encryption | 컬럼 레벨 암/복호화 |
| Default | DEFAULT 값, auto_number, created_at/updated_at |
| Computed | GENERATED 컬럼, 계산 필드 |
| Query | 논리적 쿼리 → 물리적 쿼리 변환 |
| API | REST, GraphQL, OData 자동 생성 |
| Realtime | WebSocket 기반 변경 구독 |
| Event | Webhook 발송 |
| Bulk | Import/Export |

### 상위 프로덕트 레이어 책임

| 영역 | 기능 |
|------|------|
| UI | 렌더링, input type, placeholder, hint |
| UX Validation | 클라이언트 사이드 검증 |
| Business Logic | 워크플로우, 자동화 |
| Access Control | 권한, 접근 제어 |
| Authentication | 인증 |

### Descriptor (확장 메타데이터)

MorphDB는 Descriptor를 저장하고 전달하지만 해석하지 않습니다. 상위 레이어가 해석합니다.

```jsonc
{
  // UI Hints
  "input_type": "password",
  "placeholder": "Enter password",
  "hint": "8자 이상",
  
  // Display
  "width": 200,
  "group": "기본정보",
  
  // Format
  "format": "currency",
  "currency": "KRW",
  
  // Custom
  "x-app-specific": { }
}
```

## Data Types

### Primitive Types

| MorphDB Type | PostgreSQL Type | 설명 |
|--------------|-----------------|------|
| `text` | varchar(n), text | 문자열 |
| `integer` | int2, int4, int8 | 정수 |
| `decimal` | numeric(p,s) | 고정 소수점 |
| `float` | float4, float8 | 부동 소수점 |
| `boolean` | boolean | 참/거짓 |
| `date` | date | 날짜 |
| `time` | time | 시간 |
| `timestamp` | timestamp | 날짜시간 |
| `timestamptz` | timestamptz | 타임존 포함 |
| `uuid` | uuid | UUID |
| `json` | jsonb | JSON 데이터 |

### Extended Types

| MorphDB Type | 구현 | 설명 |
|--------------|------|------|
| `enum` | PostgreSQL enum / lookup | 단일 선택 |
| `enum[]` | array | 다중 선택 |
| `file` | uuid + meta table | 파일 참조 |
| `user` | uuid + user reference | 사용자 참조 |
| `daterange` | daterange | 날짜 범위 |
| `encrypted` | bytea + encrypt/decrypt | 암호화 텍스트 |
| `auto_number` | sequence | 자동 증가 번호 |
| `computed` | GENERATED ALWAYS AS | 계산 필드 |

### Type Constraints

| Constraint | 적용 타입 | 설명 |
|------------|----------|------|
| `max_length` | text | 최대 길이 |
| `min` / `max` | integer, decimal, date | 범위 |
| `pattern` | text | 정규식 (CHECK) |
| `precision` / `scale` | decimal | 정밀도 |

## System Tables

### _morph_tables

```sql
id              UUID PRIMARY KEY
hash_name       VARCHAR(64) UNIQUE      -- 실제 테이블명 (tbl_xxx)
logical_name    VARCHAR(255)            -- 표시명
description     TEXT
icon            VARCHAR(64)
created_at      TIMESTAMPTZ
updated_at      TIMESTAMPTZ
descriptor      JSONB                   -- 확장 메타데이터
```

### _morph_columns

```sql
id              UUID PRIMARY KEY
table_id        UUID REFERENCES _morph_tables
hash_name       VARCHAR(64)             -- 실제 컬럼명 (col_xxx)
logical_name    VARCHAR(255)            -- 표시명 (Label)
data_type       VARCHAR(64)             -- MorphDB 타입
native_type     VARCHAR(64)             -- PostgreSQL 타입
ordinal         INTEGER                 -- 순서
is_nullable     BOOLEAN
is_unique       BOOLEAN
is_primary_key  BOOLEAN
is_encrypted    BOOLEAN
default_value   TEXT
check_expr      TEXT                    -- CHECK 제약조건
created_at      TIMESTAMPTZ
updated_at      TIMESTAMPTZ
descriptor      JSONB                   -- 확장 메타데이터

UNIQUE(table_id, hash_name)
```

### _morph_relations

```sql
id                  UUID PRIMARY KEY
hash_name           VARCHAR(64)
logical_name        VARCHAR(255)
source_table_id     UUID REFERENCES _morph_tables
source_column_id    UUID REFERENCES _morph_columns
target_table_id     UUID REFERENCES _morph_tables
target_column_id    UUID REFERENCES _morph_columns
relation_type       VARCHAR(32)         -- one-to-one, one-to-many, many-to-many
on_delete           VARCHAR(32)         -- CASCADE, SET NULL, RESTRICT
on_update           VARCHAR(32)
descriptor          JSONB
```

### _morph_indexes

```sql
id              UUID PRIMARY KEY
table_id        UUID REFERENCES _morph_tables
hash_name       VARCHAR(64)
logical_name    VARCHAR(255)
column_ids      UUID[]
is_unique       BOOLEAN
index_type      VARCHAR(32)             -- btree, hash, gin, gist
where_clause    TEXT                    -- partial index
```

### _morph_views

```sql
id              UUID PRIMARY KEY
table_id        UUID REFERENCES _morph_tables
hash_name       VARCHAR(64)
logical_name    VARCHAR(255)
filter_expr     JSONB                   -- 필터 조건
sort_expr       JSONB                   -- 정렬 조건
column_ids      UUID[]                  -- 표시 컬럼
descriptor      JSONB
```

### _morph_enums

```sql
id              UUID PRIMARY KEY
name            VARCHAR(255) UNIQUE
options         JSONB                   -- [{value, label, color, order}]
descriptor      JSONB
```

### _morph_computed

```sql
id              UUID PRIMARY KEY
table_id        UUID REFERENCES _morph_tables
hash_name       VARCHAR(64)
logical_name    VARCHAR(255)
expression      TEXT                    -- SQL expression
result_type     VARCHAR(64)             -- 결과 타입
is_stored       BOOLEAN                 -- materialized vs virtual
depends_on      UUID[]                  -- 의존 컬럼
descriptor      JSONB
```

### _morph_webhooks

```sql
id              UUID PRIMARY KEY
name            VARCHAR(255)
table_id        UUID REFERENCES _morph_tables
events          VARCHAR(32)[]           -- insert, update, delete
url             TEXT
headers         JSONB
filter_expr     JSONB
secret          TEXT                    -- HMAC signing
is_active       BOOLEAN
created_at      TIMESTAMPTZ
updated_at      TIMESTAMPTZ
```

## SDK

### .NET SDK

```csharp
var morph = new MorphClient("https://api.example.com", apiKey);

// Schema
await morph.CreateTableAsync("고객", new TableDef
{
    Columns =
    {
        new ColumnDef("이름", MorphType.Text) { MaxLength = 100 },
        new ColumnDef("이메일", MorphType.Text) { IsUnique = true },
        new ColumnDef("가입일", MorphType.Timestamp) { Default = "now()" },
        new ColumnDef("등급", MorphType.Enum) { EnumName = "customer_grade" }
    }
});

// Insert
var customer = await morph.InsertAsync("고객", new
{
    이름 = "홍길동",
    이메일 = "hong@example.com"
});

// Query
var vipCustomers = await morph.Query("고객")
    .Where("등급", Op.Eq, "VIP")
    .OrderBy("가입일", Desc)
    .Select("이름", "이메일")
    .ToListAsync();

// Join
var orders = await morph.Query("주문")
    .Join("고객", "고객_id")
    .Where("주문일", Op.Gte, startDate)
    .ToListAsync();

// Realtime
await morph.SubscribeAsync("고객", change =>
{
    Console.WriteLine($"{change.Operation}: {change.Data}");
});

// Bulk
await morph.BulkImportAsync("고객", stream, new ImportOptions
{
    Format = ImportFormat.Csv,
    Mode = ImportMode.Upsert,
    KeyColumn = "이메일"
});
```

### TypeScript SDK

```typescript
import { MorphClient } from '@morphdb/client';

const morph = new MorphClient({
  url: 'https://api.example.com',
  apiKey: 'xxx'
});

// Query
const customers = await morph
  .from('고객')
  .select('이름', '이메일')
  .where('등급', 'eq', 'VIP')
  .order('가입일', 'desc')
  .limit(10);

// Insert
const newCustomer = await morph
  .from('고객')
  .insert({ 이름: '홍길동', 이메일: 'hong@example.com' });

// Realtime
const subscription = morph
  .from('고객')
  .on('*', (event) => {
    console.log(event.operation, event.data);
  })
  .subscribe();

// Cleanup
subscription.unsubscribe();
```

## Packages

```
MorphDB/
├── src/
│   ├── MorphDB.Core/           # 핵심 추상화, 타입, 인터페이스
│   ├── MorphDB.Npgsql/         # PostgreSQL 구현
│   ├── MorphDB.Service/        # ASP.NET Core 서비스
│   │   ├── Rest/               # REST API 컨트롤러
│   │   ├── GraphQL/            # HotChocolate 기반 GraphQL
│   │   ├── OData/              # OData 엔드포인트
│   │   ├── Realtime/           # WebSocket/SignalR 허브
│   │   └── Bulk/               # Import/Export 처리
│   └── MorphDB.Client/         # .NET 클라이언트 SDK
├── clients/
│   ├── typescript/             # TypeScript/JavaScript SDK
│   └── python/                 # Python SDK
└── tests/
```

## Use Cases

- Notion/Airtable 스타일 데이터베이스
- Low-code/No-code 플랫폼
- 동적 폼 빌더
- 커스텀 필드가 필요한 CRM/ERP
- 멀티테넌트 SaaS 백엔드
- 실시간 협업 애플리케이션

## Development Status

### Phase 0: Foundation ✅ Completed (2025-12-14)
- [x] Solution structure (net10.0)
- [x] Central Package Management
- [x] Core abstractions and interfaces
- [x] PostgreSQL provider skeleton
- [x] ASP.NET Core service skeleton
- [x] Unit test framework
- [x] Docker Compose dev environment
- [x] GitHub Actions CI/CD

### Phase 1: Core Schema Management (Next)
- [ ] ISchemaManager implementation
- [ ] DDL operations (CREATE/ALTER/DROP TABLE)
- [ ] Advisory lock integration
- [ ] Change logging

### Phase 2-12: See [ROADMAP.md](./ROADMAP.md)

## Quick Start

### Prerequisites
- .NET 10.0 SDK
- Docker & Docker Compose
- PostgreSQL 15+ (or use Docker)

### Development Setup

```bash
# Clone repository
git clone https://github.com/iyulab/morphdb.git
cd morphdb

# Start development environment
docker-compose up -d

# Build and test
dotnet build
dotnet test
```

### Project Structure

```
MorphDB/
├── src/
│   ├── MorphDB.Core/          # Core abstractions, models, interfaces
│   ├── MorphDB.Npgsql/        # PostgreSQL implementation
│   └── MorphDB.Service/       # ASP.NET Core Web API
├── tests/
│   └── MorphDB.Tests/         # Unit and integration tests
├── scripts/
│   └── init.sql               # Database initialization
└── docker-compose.yml         # Development environment
```

## Roadmap

See [ROADMAP.md](./ROADMAP.md) for detailed implementation phases.

### Summary
- Phase 0: Foundation ✅
- Phase 1-3: Core (Schema, Data, Query)
- Phase 4-6: APIs (REST, GraphQL, OData)
- Phase 7-8: Real-time (WebSocket, Webhook)
- Phase 9-10: Bulk & SDKs
- Phase 11-12: Security & Multi-tenant

## License

MIT License