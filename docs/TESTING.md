# MorphDB Testing Guide

Comprehensive testing documentation for all MorphDB components.

## Server Tests (.NET)

Integration tests use **Testcontainers** for PostgreSQL - no external DB required.

### Commands

```bash
dotnet test                                            # Run all tests
dotnet test --filter "FullyQualifiedName~Unit"         # Unit tests only
dotnet test --filter "FullyQualifiedName~Integration"  # Integration tests only
dotnet test --filter "ClassName=SchemaManagerTests"    # Single test class
```

### Test Organization

| Directory | Description |
|-----------|-------------|
| `tests/MorphDB.Tests/Unit/` | Unit tests for DdlBuilder, DmlBuilder, etc. |
| `tests/MorphDB.Tests/Integration/` | Tests requiring PostgreSQL container |
| `tests/MorphDB.Tests/Integration/Api/` | Full API tests via WebApplicationFactory |

### Test Fixtures

- **`PostgresFixture`**: Shared container for integration tests (use `[Collection("PostgreSQL")]`)
- **`ApiTestFixture`**: WebApplicationFactory for API tests

---

## Desk Tests (TypeScript/Vitest)

### Commands

```bash
cd desk
npm run test                            # Run all tests (watch mode)
npm run test -- --run                   # Run all tests once
npm run test -- --run src/renderer/lib/__tests__/api.test.ts  # Single file
```

### Test Organization

| File/Directory | Description |
|----------------|-------------|
| `desk/src/renderer/lib/__tests__/api.test.ts` | MorphDBClient unit tests |
| `desk/src/renderer/lib/__tests__/api-scenarios.test.ts` | Cross-component API scenarios |
| `desk/src/renderer/lib/__tests__/usage-scenarios.test.ts` | Real-world usage pattern simulations |
| `desk/e2e/` | Playwright E2E tests |
| `desk/e2e/integration/` | Server integration tests (requires running server) |

### Test Patterns

#### Mock Fetch Setup

```typescript
import { vi, describe, it, expect, beforeEach, afterEach } from 'vitest'
import { MorphDBClient } from '../api'

const mockFetch = vi.fn()
global.fetch = mockFetch

function createMockResponse<T>(data: T, ok = true, status = 200): Response {
  return {
    ok,
    status,
    json: async () => data,
    headers: new Headers(),
    // ... other Response properties
  } as Response
}
```

#### Response Converters

API responses are transformed via `toColumnApiResponse`, `toTableApiResponse`:

| Raw API Field | Converted Field |
|---------------|-----------------|
| `unique` | `isUnique` |
| `indexed` | `isIndexed` |
| `default` | `defaultValue` |

#### Derived Columns

Lookup/rollup/formula details are **not** included in `ColumnApiResponse`. Only check the `isDerived` flag:

```typescript
// ✅ Correct
expect(column.isDerived).toBe(true)

// ❌ Wrong - these fields don't exist in ColumnApiResponse
expect(column.lookup?.targetTable).toBe('categories')
```

#### Bulk Operations

Use `bulkInsert`, `bulkUpdate`, `bulkDelete` methods (not `batch`):

```typescript
// ✅ Correct
const result = await client.bulkInsert('orders', records)
const updated = await client.bulkUpdate('products', { stock: 0 }, "status eq 'discontinued'")

// ❌ Wrong
const result = await client.batch('orders', operations)
```

---

## SDK Tests

### Python SDK

```bash
cd sdk/python
pytest                      # All tests
pytest -m unit              # Unit tests only
pytest -m integration       # Integration tests only
```

### TypeScript SDK

```bash
cd sdk/typescript
npm test                    # All tests
npm run test:integration    # Integration tests only
```

---

## Integration Test Requirements

For full integration testing across components:

```bash
# 1. Start test environment
docker compose -f docker-compose.test.yml up -d

# 2. Run server integration tests
dotnet test --filter "FullyQualifiedName~Integration"

# 3. Run SDK integration tests
cd sdk/python && pytest -m integration
cd sdk/typescript && npm run test:integration

# 4. Run Desk E2E tests
cd desk && npx playwright test e2e/integration
```

---

## Test Coverage

| Component | Unit | Integration | E2E |
|-----------|------|-------------|-----|
| Server | ✅ | ✅ | - |
| Python SDK | ✅ | ✅ | - |
| TypeScript SDK | ✅ | ✅ | - |
| Desk | ✅ | ✅ | ✅ |

---

*Last Updated: 2025-01-05*
