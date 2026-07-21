# Changelog

## Unreleased

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
