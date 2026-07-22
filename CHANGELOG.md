# Changelog

## Unreleased

### Changed

- **CHECK has one grammar, and it is the enforced one.** A CHECK expression is accepted at
  declaration only when the app-layer evaluator can enforce it: comparisons (`> >= < <= = == !=
  <>` against a literal or another field), `MATCHES '<regex>'`, combined with `AND`/`OR` and
  parentheses. Previously the declaration accepted any inline-safe SQL predicate, stored it, and
  the evaluator silently skipped what it could not parse — enforcement fell to a physical CHECK
  constraint that the ALTER path could never update (a redeclared rule left the old one physically
  enforced). No physical CHECK is emitted anymore (the constitution's "only CHECK is virtual"
  boundary now holds in code); expressions like `name ~ '...'` or `length(name) > 3` are refused
  with `400 INVALID_ARGUMENT` listing the supported forms — use `MATCHES` for regex. Tables created
  by earlier versions keep their existing physical CHECK constraints; they enforce the same
  declared rule and may be dropped manually.

- **`project_id` no longer appears on any consumption surface.** The project is an internal
  operating unit and every request is already scoped by `X-Project-Id`, so the GUID carried zero
  information — yet it leaked into every data row (REST, complex query, OData, write responses),
  the schema column list, and the "Available columns" error text. All three surfaces now exclude
  it; the physical column and scope isolation are unchanged server-side. Consumers that read the
  leaked value must stop — it was never part of the row contract. (The four private copies of the
  physical-row→logical-dictionary mapping converged into one `RowMapper` on the way.)

- **Request envelopes are strict.** A JSON member a request DTO does not declare is refused with
  `400 INVALID_ARGUMENT` naming the member and listing the supported ones — previously it was
  silently dropped by default deserialization, so a typo'd `filters` against
  `POST /api/data/{table}/query` answered 200 with every row, the filter ignored (a confidently
  wrong answer), and a `colums` typo created a table with zero columns. Applies to every API model,
  nested nodes included; row-data dictionary bodies are unaffected (their unknown-field policy is
  the write pipeline's `UNKNOWN_COLUMN`). Model-binding failures now answer the standard
  `{error, message, code}` envelope instead of the framework's ProblemDetails — one error shape on
  every path.

- **Write-failure error codes are now the documented ones.** A write refused because it named
  undeclared columns answers `400 UNKNOWN_COLUMN` (exactly what docs/API.md always promised); every
  other validation failure — and any mix of causes — answers `400 VALIDATION_ERROR`, the same code
  physical constraint violations translate to. The previous `VALIDATION_FAILED` appeared in no
  documentation and is retired, on the REST and GraphQL write surfaces alike.
- **The Errors table in docs/API.md now lists every code the server can answer** (17 codes were
  produced but undocumented — job/view/audit lookups, batch preconditions, and more), and a parity
  test (`DocsErrorCodeParityTests`) holds the table and the server inventory equal from now on: a
  code added on either side without the other fails the suite.
- **`POST /api/data/{table}/query` is now documented** — filter-tree (`$type`
  condition/group), `select`, `orderBy`, paging, and the paged response envelope — and its examples
  run verbatim as contract tests (`ComplexQueryApiTests`). The docs' claim that `in`/`isnull` are
  available on this endpoint was false and is corrected: the 11-operator vocabulary is the whole
  set, on every surface.

### Fixed

- **A failed transaction now tells you why.** The failed operation's result — with its
  `validationErrors` (field + machine-readable code) — is included in the transaction response's
  `results`; previously it was dropped, reducing the whole failure to an opaque `"Validation
  failed"` string with a `failedOperationIndex`. The transaction door's write contract is now
  pinned by equivalence tests (`TransactionWriteContractTests`): the same bad row is refused for
  the same code as REST (`UNKNOWN_COLUMN`, `CHECK_VIOLATION`), a transactional row carries the same
  pipeline-applied system columns, and the pipeline write demonstrably runs inside the
  transaction's connection scope (removing the scope sends the rollback test red).
- **The C# SDK now delivers the server's error contract.** Every client (`Schema`, `Data`, `Batch`,
  `Bulk`, `Transactions`, `Views`, `Webhooks`) parses the server's `{error, message, code}` envelope
  into the exception it throws: `Message` is the server's message (what went wrong and what to do),
  `ErrorCode` is the server's code (the machine-readable contract `docs/API.md` says to branch on).
  Previously all seven surfaces threw fixed strings — `"Validation failed"` — and `ErrorCode` was
  always null, so the 0.8.0 error contract never reached SDK consumers. A response body that is not
  an envelope (a proxy's HTML error page) falls back to the legacy fixed messages and per-type
  default codes. The seven per-client copies of the status-to-exception mapping converged into one
  shared helper.
- **An OData `$filter` the handler cannot parse is refused (400 `VALIDATION_ERROR`), not silently
  ignored.** Previously an unparseable filter expression (e.g. `$filter=name eq`) answered 200 with
  every row, letting the caller believe the predicate matched. The OData error surface is now pinned
  by contract tests alongside REST, GraphQL and the C# SDK: unknown column in
  `$filter`/`$orderby`/`$select` answers 400 `COLUMN_NOT_FOUND`, an unknown entity set answers 404
  `NOT_FOUND`, all with the standard envelope.

## 0.8.0

### Removed — the authentication machinery

- **The service no longer carries authentication it never enforced.** A production image had no way
  to mint an API key — the only issuing endpoint was Development-mode-only, and the key-management
  endpoints demanded a role only an existing key could grant — so every working deployment already
  ran unauthenticated, and the machinery's only effect was to advertise a boundary that did not
  exist. Removed: the `X-API-Key` / JWT `Authorization` authentication handler, the
  `/api/security/keys` endpoints, the Development-only `POST /api/dev/bootstrap`, the
  `_morph_api_keys` control-plane table (existing databases drop it on start), the `Jwt`
  configuration section, and the client options and methods that carried credentials
  (`MorphDBClientOptions.ApiKey`/`JwtToken`, `SetApiKey`, `SetJwtToken`). Access control is the
  deployment's job: bind the service privately, or put an authenticating proxy in front. Desk's
  credential storage (the connection dialog's API-key field and the encrypted store behind it),
  its API-keys management tab, and the credential options of the TypeScript and Python reference
  SDKs went with it.
- **The role gates fell with it.** The security-policy, encryption-rotation and diagnostics
  endpoints — previously `[Authorize]`-gated behind a role no production caller could hold, and so
  unreachable — now answer like every other endpoint. Row-level security still evaluates: an HTTP
  request runs in its project's anonymous context, so `{{is_authenticated}}` is `false` and the
  user-bearing placeholders substitute `NULL` (fail-closed; a `{{user_id}}` policy matches no rows
  over HTTP).

### Changed — safe defaults

- **The composes bind to loopback.** The README quick-start and the repository's development
  compose publish every port on `127.0.0.1` — an unauthenticated service must not land on all
  interfaces by default. To serve other machines, front it with a reverse proxy (or your app) and
  bind that.
- **The service states its posture once per start**: a single startup log line says that nothing
  authenticates and access control belongs to the deployment. Ghost references to `X-API-Key` in
  the API reference, client README, desk user guide and philosophy docs left with the machinery.

### Changed — the contract is served, not shipped dark

- **`/swagger` (OpenAPI document and UI) is served in every environment.** It was registered
  unconditionally but exposed only in Development, so the deployed image answered 404 for its own
  machine-readable contract.

### Changed — error and write contracts

- **`PROJECT_NOT_FOUND` and `DUPLICATE_SLUG` are typed exceptions** (`ProjectNotFoundException`,
  `DuplicateSlugException`) instead of code-string matches on the base exception, and the global
  handler maps them (404 / 409). Wire responses on the project endpoints are unchanged; the floor
  improves — these escaping on any other path answered 500, now 404/409.
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

- **A deleted logical name could never be created again.** DELETE drops the physical object and
  keeps the metadata row as a tombstone, but uniqueness was a plain table-level constraint that
  counts tombstones as occupants — while the lookups guarding creation filter `is_active = true`
  and so could not see them. The second declaration of any name therefore died on a raw `23505`
  that escaped as a 500, permanently: drop-and-rebuild, the standard schema-evolution path, was a
  one-way door from the second declaration onward. Uniqueness is now a partial index over the live
  rows on every soft-deleted control-plane table — tables (logical **and** physical name), columns,
  indexes, views and security policies — so a tombstone releases the name it no longer uses while
  two live objects still cannot share one. Existing databases are migrated on start; a control
  plane older than the `is_active` flag gets it before the indexes are built, so the bootstrap
  cannot crash-loop. A unique violation on a control-plane insert now answers **409
  `DUPLICATE_NAME`** rather than a 500.

- **Deleting a table left its columns, indexes and relations marked live.** The delete drops the
  physical table — and with it every index on it — but only the table's own metadata row was
  retired, so the control plane went on describing parts of a table that no longer existed, and a
  relation kept pointing at a table that was gone. A delete now retires the table's columns,
  its indexes, and every relation touching either end, in one statement with the table itself.

- **Deleting a table that another table references answered a bare 500.** The drop carries no
  CASCADE on purpose — tearing down another table's foreign key is not something deleting this one
  should decide — but PostgreSQL's refusal escaped untranslated, quoting a physical table name the
  caller is not meant to know exists. It now answers `TABLE_HAS_DEPENDENTS` naming the table by the
  logical name the caller gave it.

- **A security policy's expression reached the WHERE clause unvalidated.** Policy expressions are
  spliced into ordinary queries, which makes them caller-authored strings that reach SQL verbatim —
  the same category as a CHECK predicate or an index predicate, both of which were already gated
  while this path shipped open. A statement separator, a comment opener, an unbalanced parenthesis
  or an unterminated quote is now refused (400 `INVALID_EXPRESSION`) when the policy is created or
  updated, and again on the substituted text at evaluation time, so a row stored before this
  release fails the read rather than being emitted. The validator itself moved out of `DdlBuilder`
  (it stopped being about DDL) and is now the one gate every such path calls.

- **Security policies never worked at all.** Both name lookups asked `_morph_tables` for a column
  called `name`, which it has never had (`logical_name`), so every create and every by-name read
  answered `42703` as a 500; and the row type was a positional record, which Dapper cannot
  materialise under the assembly's snake_case convention, so every read by id or table failed on
  materialisation. `POST /security/policies` and the policy reads behind it were shipped and
  unreachable. Both name lookups now use `logical_name` and exclude deleted tables, and the row
  type maps by property as the rest of the assembly does. The service has integration coverage for
  the first time.

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
