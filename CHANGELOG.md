# Changelog

## Unreleased

### Changed — error and write contracts

- **A caller's mistake now answers 4xx with a code and a hint — never a 500, and never an empty
  body.** A global exception handler is the single authority for what an escaped exception becomes
  on the wire; live-probed paths that previously answered `500 INTERNAL_ERROR` (or a bodyless 500)
  now answer: unknown column type on CREATE TABLE → 400 listing the supported types; unknown filter
  column → **400 `COLUMN_NOT_FOUND`** naming it; a project id no project bears → 404; explicit null
  into a `nullable:false` column → 400 naming the column (physical `23502`/`23505` violations
  translate to the same `VALIDATION_ERROR` the app-layer validators produce). Anything genuinely
  unexpected is a logged 500 carrying the fixed `INTERNAL_ERROR` envelope.
- **An unknown filter operator is now a 400 listing the supported operators.** It previously fell
  back to `eq` silently, so a typo became a different query with no signal.
- **Writes naming a column the table does not declare are rejected (400 `UNKNOWN_COLUMN`) instead
  of silently dropped.** This applies to every write door — data insert/update, batch, seed,
  upsert, bulk import rows, and GraphQL mutations — because the write paths that previously built
  their own SQL now all go through the write pipeline (which also means virtual constraints and
  system-column transformers apply uniformly; batch/seed/upsert rows now get UUIDv7 ids,
  timestamps and versions from the pipeline rather than database defaults). Callers that want the
  old dropping behaviour opt in explicitly with `?ignoreUnknown=true`.
- **`MISSING_PROJECT` no longer advertises an API key** the server never asks for; it says to send
  `X-Project-Id`.
- **Error text no longer carries internal identifiers.** CHECK-expression rejections quote the
  expression as the caller wrote it (previously the physically-renamed form), and not-found
  messages no longer embed project GUIDs.

### Fixed

- **`CURRENT_TIMESTAMP` as a column default failed the CREATE TABLE.** SQL's clock keywords take no
  parentheses, so the function-default check never saw them: they were quoted as string literals —
  `DEFAULT 'CURRENT_TIMESTAMP'` — which no temporal column can cast. The keywords
  (`CURRENT_TIMESTAMP`, `CURRENT_DATE`, `CURRENT_TIME`, `LOCALTIMESTAMP`, `LOCALTIME`) are now
  recognised on date/time/datetime columns; on a text column the same word remains an ordinary
  string literal.

- **`MorphDB.Npgsql`'s snake_case mapping only applied if you booted DI.** The Dapper flag the
  assembly's SQL is written against was set inside `AddMorphDbNpgsql`, so code constructing a
  repository directly read multi-word columns as defaults — `project_id` came back `Guid.Empty`
  with no error, and Dapper's per-query deserializer cache kept the wrong mapping alive even after
  DI later set the flag. The convention is now a module initializer: it holds before the first
  query this assembly issues, whoever issues it. Provisioning also refuses `Guid.Empty` outright
  instead of silently creating `p_00000000` schemas, and a created project that cannot be read
  back is an error rather than a null.

- **Desk described request shapes the server never accepted.** Its lookup, rollup and formula
  column-config types (`relationId`/`sourceColumnName`, `expression`/`outputType`) matched nothing
  in the API — creating any of those columns from desk could not have worked. The types now mirror
  the server's records. Its aggregation-result type was likewise wrong: the panel rendered an
  `executedAt` field the server never sends ("Invalid Date") and hid the `totalGroups` and
  scan-metadata fields it does. The scenario tests' typecheck errors had been pointing at exactly
  this — desk's `vitest` stayed green because mocks accept anything — and a desk typecheck job now
  runs in `ci.yml` on every push, where before it ran only on release tags.

- **The batch-family endpoints reported every failure — including our own — as a 400 whose message
  was the raw exception text.** Batch, bulk, transaction, aggregation and audit actions ended in
  `catch (Exception) { return BadRequest(ex.Message); }`: a caller could not tell a service defect
  from their own bad request (and retrying "their" error retried our bug), and internal exception
  text — driver messages, physical identifiers — was copied onto the wire. These actions now answer
  the way the data endpoints always did: what the service layer legitimately throws keeps its
  documented status (validation → 400, missing table/record → **404, where it was previously a
  400**, bad argument → 400), and anything unexpected is a logged **500 with a fixed message**.
  Per-item errors inside batch/seed responses follow the same rule. The audit endpoints' catch-all
  `AUDIT_QUERY_FAILED` / `AUDIT_STATS_FAILED` 400s are gone with that branch.

- **The schema API's documented 409s were unreachable.** The controller caught exception types
  nothing throws (`DuplicateException`, `ConcurrencyException`), so creating a table or column under
  a taken name came back **400 "SchemaError"** — indistinguishable from a malformed request — and a
  stale-version update escaped as an **unhandled 500**. The catches now name the types the schema
  layer actually throws; a duplicate name is 409 `DuplicateTable`/`DuplicateColumn` and a version
  conflict is 409 `ConcurrencyConflict`, as the endpoints always advertised.

### Removed

- **`POST /api/projects/{id}/archive`.** It promised "read-only mode" and no code anywhere enforced
  it: the endpoint set a status nothing ever read, and its implementation carried a TODO for a
  backup process the constitution rules out (operational orchestration is a non-goal). A dead
  promise is worse than an absent feature. The `Archived`/`Archiving` status values remain readable
  for databases that already carry them.
- **`ISchemaMapping` / `ISchemaMappingCache`** — interfaces nothing implemented or consumed.

### Removed (earlier in this cycle)

- **Exception types no release ever threw**, dead since their introduction:
  `ProjectIsolationException` (error code `PROJECT_ISOLATION_VIOLATION` — nor its pre-0.7.0 name
  `TENANT_ISOLATION_VIOLATION`; the 0.7.0 migration table maps the rename for completeness, but the
  code never appeared in a response), `DataValidationException`, `CircularReferenceException`,
  `DuplicateException`, and `ConcurrencyException`. None of their error codes ever reached the wire.

## 0.7.1

A container-image fix. **The NuGet packages are unchanged in substance** — they carry the new number
only because one version governs the whole repository. If you consume the client library and not the
image, there is nothing here for you.

If you run the image, upgrade: every container built from 0.6.0 or 0.7.0 reports itself unhealthy
forever, and anything waiting on that report waits forever.

### Fixed

- **The image's HEALTHCHECK called `wget`, which is not in the image.** Every container built from
  0.6.0 and 0.7.0 reported `unhealthy` forever while the service was serving normally, because
  `mcr.microsoft.com/dotnet/aspnet` ships neither wget nor curl. `docker compose up --wait` failed,
  and anything waiting on `depends_on: { condition: service_healthy }` waited forever. The service
  now probes itself — `dotnet MorphDB.Service.dll --health-check` — which depends on nothing beyond
  what the image already contains, so it cannot break again when the base image changes.
- **A fatal start-up failure exited 0**, so a container that never started reported a clean shutdown
  and `restart: on-failure` did not fire. It now exits 1.
- The `docker-compose.yml` api service redefined the healthcheck as `curl -f` — the same defect a
  second time. The override is gone; the image's own definition is the single one.

### Changed

- `--start-period` for the container healthcheck is 15s (was 5s). Start-up runs the global schema
  migrations, and 5s was optimistic.
- The Docker release workflow now boots the built image against Postgres and refuses to publish
  unless the container reports `healthy`. Publishing is irreversible; the gate belongs in that path.

## 0.7.0

A cleanup release. MorphDB is a virtual-schema layer, and several surfaces had drifted outside that:
a SaaS control plane it does not run, quotas it never enforced, and a name — *tenant* — promising an
isolation boundary it explicitly does not stand.

**Everything breaking is in this one release, on purpose.** These changes were held back and bundled
so a consumer crosses once rather than four times.

### Migration

**Databases migrate themselves.** The pre-bootstrap DDL renames the scope column and its indexes on
the next start, across the control plane and every per-project system schema. It is idempotent, uses
`RENAME COLUMN` so no data is rewritten, and leaves your data schemas alone — a column you named
`tenant_id` is yours.

**Client code does not.** There is no compatibility shim: the old header and the old option are gone
in this release, not deprecated. The changes are mechanical.

| Before | After |
|---|---|
| `X-Tenant-Id: <id>` | `X-Project-Id: <id>` |
| `new MorphDBClientOptions { TenantId = id }` | `new MorphDBClientOptions { ProjectId = id }` |
| error code `MISSING_TENANT` | `MISSING_PROJECT` |
| error code `TENANT_ISOLATION_VIOLATION` | `PROJECT_ISOLATION_VIOLATION` |

### Breaking

- **`tenant` is now `project`, everywhere.** One concept had carried two names: the domain model,
  the repositories and the REST routes said *project*, while the header, the client option and the
  columns underneath said *tenant*. You had to create a project and then send it as a tenant. The two
  words do not mean the same thing — a project is a schema namespace; a tenant is an isolation
  subject — and only one of them describes what this layer does.

- **The SaaS control plane is gone.** Organizations, memberships, invitations, SSO configuration,
  backup orchestration, quota administration and the platform admin surface have been removed, along
  with their endpoints, their tables and the `org_id` column on projects. They assumed what this
  database is used for, which a virtual-schema layer must not.

- **Per-project quota and rate-limit settings are gone** — `maxTables`, `maxStorageBytes` and
  `rateLimits` are no longer accepted or returned by `/api/projects`. Nothing had ever enforced them:
  they were stored, echoed back, and constrained nothing, so setting `maxTables: 10` still let the
  eleventh table through. Service-level rate limiting is unaffected.

- **`Suspended` is gone**, with `POST /api/projects/{id}/suspend` and `/reactivate`. Suspending set a
  status column and nothing else — requests never read the project row, so a suspended project kept
  serving reads and writes while the API reported success. `Provisioning`, `Archiving` and `Archived`
  remain: those describe the state of the schemas, which is this layer's business.

### Fixed

- **The API reference claimed authentication it does not require.** It opened with "All requests
  require API Key authentication"; the schema and data endpoints have no such requirement and serve a
  request carrying only a project header. A reader who believed it would pass a client-supplied
  project id straight through, and whoever picks that value picks which schemas they read.

  **Read the security note in the API reference before deploying.** A project is a schema namespace,
  not a trust boundary. Run MorphDB where only your application can reach it, and decide there who
  may see what.

- **A request that named no project could be answered as a server fault.** Every endpoint now gives
  the same answer — `400` with `MISSING_PROJECT` — decided before the action runs.

- **Realtime connections without a project are refused** instead of silently joining an empty scope.
  Such a connection used to subscribe successfully and then receive nothing, forever, with no error.

### Changed

- Project settings are stored with an explicit camelCase policy, matching what the REST surface has
  always spoken. Existing rows are read regardless of case, so nothing needs converting.

## Earlier releases

No changelog was kept before 0.7.0. See the git tags and release history.
