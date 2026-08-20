# Changelog

## 0.10.0

### Breaking

- **Three kinds of GraphQL type change names in the served schema, and nothing about the change
  reaches a compiler.** The subscription root is now served as `Subscription`; it had taken its name
  from the class carrying the resolvers and read as `DynamicSubscription`. The three mutation
  envelopes are now `RecordMutationResult`, `RecordListMutationResult` and `BooleanMutationResult`;
  each had been named after its closed CLR generic — `MutationResultOfRecordNode`,
  `MutationResultOfIReadOnlyListOfRecordNode`, and the same construction for the boolean one —
  which put .NET shapes, including an interface name, into a published schema. A schema is a
  contract even though it does not travel through NuGet, so introspection output, generated
  clients and any document naming these types have to be regenerated; a client that only names
  fields is unaffected. The names are now stated by the schema rather than inherited from whatever
  class implements it, so renaming a class can no longer move them, and a test holds the served
  schema to them.

### Changed

- **The GraphQL server runs on HotChocolate 16, and a row written over GraphQL is now read by this
  service rather than by the server.** No package references HotChocolate, so a package consumer's
  restore graph does not move, and the schema still says `Any` for a row — which is why this change
  is invisible to introspection and to a schema diff. What moved is underneath: a mutation used to
  receive the row already materialised by the GraphQL server, and now receives the JSON the caller
  sent and narrows it here, in one place. The rules are stated once and apply in both directions:
  a number takes the smallest integer type that holds it and otherwise a decimal; a string that
  reads as a UUID or a timestamp becomes one; an object or array is kept as JSON text rather than
  re-materialised, since that is what the column stores. The reason to do it here is that the HTTP
  door reads the same JSON by the same rules — two doors that coerce differently are two contracts,
  and they now agree. **A client that wrote rows over GraphQL and depended on the previous
  coercion should check values that are numeric, UUID-shaped or timestamp-shaped**; a client that
  wrote over HTTP is unaffected, and one that used both gets the same row from either. The mapping
  is now held by a test, which it was not before.
  The 16.x line also carries forward the fix for the advisory that had been closed in the
  transitive `HotChocolate.Language`.
- **Four scalars carry the descriptions and specification links their current definitions give
  them.** `Any` now states a specification URL where it previously stated none, `UUID` points at
  the scalar specification rather than at RFC 4122 and describes itself against RFC 9562, and
  `DateTime`, `Long` and `UUID` read differently in generated documentation. Nothing about their
  shape or their use changes — a document that was valid stays valid — but a tool that reads
  `@specifiedBy` to pick a client-side representation sees new values, and generated docs change
  wording. The whole served schema is now recorded in the repository and compared on every build,
  so a difference of this kind is a release note rather than a discovery.
- **A relation states whether it is enforced, and a project can state it once for all of them.**
  Enforcement is no longer implied: a relation that says nothing resolves against the project's
  `defaultEnforceOnWrite`, which is true unless the project says otherwise, and a relation that
  states its own value overrides it. A non-enforced relation gets no physical constraint either —
  turning one of the two off while leaving the other on leaves the database still refusing writes,
  which is an option that only pretends to be off.

### Added

- **Connection secrets, with roles, and a boundary that is enforced before it is advertised.**
  A project can issue secrets that carry a role, and the surface refuses what the role does not
  cover rather than documenting a restriction it does not apply.
- **A client can declare relations.** `SchemaClient.CreateRelationAsync` creates a relation by
  logical names rather than internal identifiers, and takes the optional `EnforceOnWrite` described
  above.
- **A client can manage projects, not only work inside one.** `MorphDBClient.Projects` answers the
  eight project endpoints the server already served — create, list, get, get-by-slug, update,
  delete, stats and health — one to one, with no convenience method composing several of them.
  Until now the SDK could work inside a project but could not create, read or configure one, so
  that half of the API required dropping to HTTP.
- **A caller can choose the id a project is created under.** Creating the same project twice
  answers `409 DUPLICATE_PROJECT_ID`, which is enough for a start-up step to be safe to re-run.
- **A project decides how much of its own audit history it keeps** through
  `auditLogRetentionDays`.
- **A reverse proxy can be named as trusted**, through `TrustedProxies` configuration
  (`KnownProxies`/`KnownNetworks`). Unconfigured is a deliberate no-op — see Fixed.

### Fixed

- **`EnforceOnWrite` was a switch wired to nothing** — the value was accepted and then had no
  effect on whether writes were checked.
- **An error response always carries the code callers are told to branch on.** The envelope
  documented a `code` for every failure, and a large number of throw sites were leaving it null,
  so a client following the documented shape saw `null` where it had been promised a string. The
  field is now required, which makes the compiler the gate, and a test reads real responses rather
  than the type that produces them.
- **A retention window that cannot be applied is refused rather than stored**, so a project's
  settings cannot record a promise the service will not keep.
- **A race for a chosen project id answers as a conflict rather than an internal error.**
- **A data type that is declared but not implemented is refused as such, not as unknown.** Two
  members of the type vocabulary have no storage behind them; naming one produced "Unknown data
  type", which sent the caller looking for a spelling mistake in a value that is spelled correctly
  and parses. The refusal now says the type is unimplemented and what to do instead.
- **The list of types a caller is offered is the set a column can actually be created with.** It
  was written by hand and named fifteen of the thirty members the vocabulary has — worse than
  naming none, because a reader takes it for the whole answer — and it is now derived, which also
  keeps the two unimplemented members out of it.
- **A registered webhook was never triggered.** Every management endpoint for webhooks worked —
  register, list, delete, replay from the dead-letter queue — but no code path ever called the one
  method that queues a delivery, so a webhook could be created and inspected while nothing was ever
  sent to it. The realtime listener that already turns a row change into a broadcast now offers the
  same event to subscribed webhooks, matched by table and event. A webhook's `filter` is still not
  evaluated at delivery time — a subscription with a filter fires for every row on its table and
  event, not only the ones the filter would admit — which is tracked separately.
- **A held-open realtime connection could call `Subscribe`/`Unsubscribe` without limit.** The rate
  limiter only ever saw the HTTP handshake that opened the connection; every message after that
  travelled the open WebSocket and never re-entered the pipeline the limit was checked against. The
  same limiter, keyed the same way (`project:{id}`) HTTP requests already are, now runs on hub
  method calls too, so a caller cannot outrun its quota by switching transport. A denied call fails
  the caller's `invoke()` with a `HubException`.
- **`X-Forwarded-For` was trusted unconditionally for the IP used in rate-limit keys and audit
  entries**, with no reverse-proxy allowlist anywhere in the service — a caller could set and
  rotate an arbitrary value to defeat rate limiting or forge the IP its own actions were recorded
  under. The header is now consulted only when at least one proxy or network is named under
  `TrustedProxies`; an unconfigured deployment (the default) never reads the header and always uses
  the raw TCP peer address.

### Docs

- The API document says where the error envelope stops: it covers routed requests, and a request
  that matches no route gets the framework's own empty 404. Held to that by a test.
- The constraint boundary said the application layer enforces what the database enforces.
- The package document says which of the three packages to reach for, and the settings contract is
  held to a test rather than to prose.
- The OData surface says which clients can reach it and which cannot: a project is addressed by
  header, and a connector dialog that offers only a URL and an authentication kind cannot send one,
  so that path needs the advanced editor. The document now says so, and says what to write there.
- The constitution records that relations declare their own enforcement, and that matching an
  already-shipped surface is not the same as proposing a new primitive.
- The architecture document says who owns backup and disaster recovery: MorphDB covers schema and
  data operations, not the PostgreSQL instance's persistence lifecycle. Backup, point-in-time
  recovery and DR tooling sit below the storage layer, in the deployment itself — none of the
  reference deployment manifests in this repository configure backups, which is the boundary
  rather than a gap.

## 0.9.1

### Docs

- **`docs/API.md` no longer promises six endpoints the server does not serve.** Column update and
  delete are addressed by column id (`/api/schema/columns/{columnId}`), not by table and column
  name; index creation lives under its table; bulk import and export name the format in the path
  and run as asynchronous jobs returning `202 Accepted`, which the document described as synchronous
  single calls. A documented dry-run validation endpoint was removed — no such endpoint exists, and
  the response example named an error code the server never answers. A route in the API document is
  now held to the routes the controllers actually declare, so this class of drift fails the build
  instead of a consumer's integration.
- **The architecture document no longer describes GraphQL types being generated per table.** The
  schema's shape is fixed and comes from CLR types; tables and rows are served as data by resolvers
  that read metadata per request, which is why creating a table changes no GraphQL type and why
  building the schema needs no database. The diagram also drew an endpoint-generator component that
  does not exist — there is nothing to generate.

### Removed

- Two unused data-type-to-GraphQL-type mapping helpers on the schema builder. They had no callers
  and were the only apparent evidence for the per-table type generation the docs described.

### Dependencies

- `coverlet.collector` to 10.0.1, verified by collecting coverage rather than by tests passing —
  a passing suite does not exercise the collector at all.

- ASP.NET Core packages to 10.0.10 (JWT bearer, SignalR client, MVC testing, Redis cache),
  the OpenTelemetry family to 1.17.0 in lockstep (Prometheus tracks it on its `-beta.1` line,
  the only line it has ever had), plus `Npgsql` 10.0.3, `Dapper` 2.1.79, `Polly` 8.7.0,
  `System.IdentityModel.Tokens.Jwt` 8.22.0, `BCrypt.Net-Next` 4.2.0,
  `Microsoft.AspNetCore.OData` 9.5.0, and the test packages. Patch and minor only; no
  vulnerability advisories were open against the previous set.

### Internal

- The release workflow creates the GitHub release itself instead of relying on someone doing it by
  hand, and refuses to publish a version this changelog does not describe.

## 0.9.0

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

### Docs

- `docs/SYSTEM_COLUMNS.md` is now in English, and its constraint matrix no longer contradicts the
  constitution's amended boundary (it still described FK/NOT NULL/UNIQUE as virtual; they are
  physical with pipeline pre-checks, and only CHECK is virtual). The stale "to implement" list was
  replaced with the actual implementation map.

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
