# MorphDB Testing Guide

Comprehensive testing documentation for all MorphDB components.

## Server Tests (.NET)

Integration tests use **Testcontainers** for PostgreSQL - no external DB required.

### Commands

```bash
dotnet test                                                        # Run all tests
dotnet test --filter-namespace MorphDB.Tests.Unit                  # Unit tests only (includes sub-namespaces)
dotnet test --filter-namespace MorphDB.Tests.Integration            # Integration tests only
dotnet test --filter-class MorphDB.Tests.Integration.SchemaManagerTests   # Single test class
dotnet test --coverlet --coverlet-output-format cobertura           # With coverage (coverlet.MTP)
```

The suite runs on xunit.v3 through the Microsoft.Testing.Platform mode of `dotnet test`
(`global.json` selects the runner), so the VSTest-era `--filter "FullyQualifiedName~..."` and
`--collect` switches do not apply; the filter switches above are xunit.v3's own. A filter that
matches nothing fails the run (exit code 8) rather than passing vacuously. In this mode `dotnet
test` forwards every switch it does not own to the test host, and a switch the host does not know
— `--nologo`, for one — makes it run nothing: exit code 5, `Zero tests ran`, `error: 1`, and no
message naming the switch. Pass only the switches listed here.

### Test Organization

| Directory | Description |
|-----------|-------------|
| `tests/MorphDB.Tests/Unit/` | Unit tests for DdlBuilder, DmlBuilder, etc. |
| `tests/MorphDB.Tests/Integration/` | Tests requiring PostgreSQL container |
| `tests/MorphDB.Tests/Integration/Api/` | Full API tests via WebApplicationFactory |

### Test Fixtures

- **`PostgresFixture`**: Shared container for integration tests (use `[Collection("PostgreSQL")]`)
- **`ApiTestFixture`**: WebApplicationFactory for API tests
- **`TestPostgres`**: Builds every PostgreSQL container the suite starts. Each one is initialised
  by `scripts/init.sql` — the script the compose files mount — and the code under test connects as
  the service role it creates (`NOSUPERUSER NOBYPASSRLS`, owner of the database), never as the
  image's superuser, which MorphDB refuses at start-up. A test that genuinely needs a superuser
  uses `PostgresFixture.SuperuserConnectionString` and says why.

### Contract tests: when a write or exposure rule crosses more than one door

A **contract test** (`tests/MorphDB.Tests/Integration/Api/*ContractTests.cs`) pins something that
must hold *the same way* across REST, GraphQL, OData, realtime and export — not one door's
behavior in isolation. Two shapes so far:

- **Write convergence** (`GraphQlWriteContractTests`, `TransactionWriteContractTests`, ...): the
  same bad row is refused through every door for the same reason, and a row accepted through one
  door carries the same pipeline-applied system columns as one accepted through another.
- **Non-exposure** (`ProjectIdExposureTests`, `PhysicalNameExposureContractTests`): something the
  server knows internally (`project_id`, a physical column name) must never reach a caller, on any
  surface a row's shape reaches one through.

Add a contract test — not just a per-door unit test — when a PR does either of these:

1. **Adds a new write door**, or a new way an existing door accepts data. Per-door unit coverage
   cannot catch two doors drifting apart, which is exactly how the pre-existing defects this
   pattern was built to catch lived (a bad row silently dropped on one door, enforced on another).
2. **Adds a new surface a row's data reaches a caller through** (a REST field, a GraphQL type, a
   new export format, a realtime event, ...). Use `PhysicalNameGuard` (`tests/MorphDB.Tests/
   Fixtures/PhysicalNameGuard.cs`) to scan that surface's live response for a leaked physical name
   or `project_id`, the same way `PhysicalNameExposureContractTests` does for REST/GraphQL/export/
   view — this is what caught the real-time broadcast never having had a translation step at all.

A contract test runs the real thing (a live request against `ApiIntegrationFixture`, or a real
NOTIFY through `PostgresChangeListener`) — a test that only inspects a generated string or a
hand-built object cannot tell "looks translated" from "actually resolves," which is how the view
builder's join-translation defect and the SQL trigger's untranslated broadcast both shipped
unnoticed.

---

## SDK Tests

The Python and TypeScript clients under `sdk/` are archived (see `docs/COMPATIBILITY.md`). Their
test suites are frozen with them: no workflow runs them, and they are not expected to pass against
the current server.

---

## Integration Test Requirements

For full integration testing across components:

```bash
# 1. Start test environment
docker compose -f docker-compose.test.yml up -d

# 2. Run server integration tests
dotnet test --filter-namespace MorphDB.Tests.Integration
```

---

## Test Coverage

| Component | Unit | Integration | E2E |
|-----------|------|-------------|-----|
| Server | ✅ | ✅ | - |
| Python SDK (archived) | frozen, not run | frozen, not run | - |
| TypeScript SDK (archived) | frozen, not run | frozen, not run | - |

The archived SDKs keep their test suites under `sdk/`, but nothing runs them and nothing will: a
change to the server contract is not reflected on the SDK side. `MorphDB.Client` is the only client
verified against a live server.

---

*Last Updated: 2026-09-03*
