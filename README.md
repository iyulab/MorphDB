# MorphDB

[![License: Apache 2.0](https://img.shields.io/badge/License-Apache_2.0-blue.svg)](https://opensource.org/licenses/Apache-2.0)
[![.NET](https://img.shields.io/badge/.NET-10.0-blue.svg)](https://dotnet.microsoft.com/)
[![Docker](https://img.shields.io/badge/ghcr.io-morphdb-blue?logo=docker)](https://ghcr.io/iyulab/morphdb)

**Runtime-flexible relational database service**

MorphDB provides runtime schema flexibility while preserving full RDBMS power—ACID transactions, complex queries, and referential integrity. Its design philosophy enables building dynamic data applications—spreadsheet-style databases, form builders, or any system requiring runtime data structures.

## Key Features

- **Dynamic Schema** - Create/modify tables and columns at runtime
- **Multi-Protocol** - REST, GraphQL, OData, WebSocket, Webhook
- **Real-time** - Live data sync via SignalR
- **Type Safety** - Native SQL types with logical name mapping

## Philosophy: Virtual DOM for Databases

MorphDB applies the **Virtual DOM pattern** to databases. Just as React abstracts DOM manipulation, MorphDB abstracts database access:

```
React:   [Developer] → [Virtual DOM] → [Real DOM]
MorphDB: [Developer] → [Logical Schema] → [Physical DB]
```

**Core Principles**:

| Principle | Effect |
|-----------|--------|
| **Logical-Physical Separation** | Rename tables/columns without data migration |
| **Physical Integrity, Logical Names** | Integrity constraints are enforced by the database; only CHECK stays app-layer, because its expressions reference logical names |
| **Blocked Access = Security** | No direct SQL = No SQL injection, full audit trail |
| **Encapsulated Complexity** | Simple API, complex internals hidden |

**Trade-offs**: We sacrifice direct SQL access and some advanced DB features in exchange for security, flexibility, and simplicity.

→ See [Philosophy](docs/PHILOSOPHY.md) for detailed design rationale.

## Quick Start

### Docker (Recommended)

```bash
docker pull ghcr.io/iyulab/morphdb:0.18.0
```

One number covers everything: a release publishes the git tag `vX.Y.Z`, the image `X.Y.Z`, and the
NuGet packages `X.Y.Z` together, so a client and a server that share a version are a compatible pair.
Pin that number rather than `latest` — this is a 0.x line and a minor release may break you. The
newest one is the newest [tag](https://github.com/iyulab/MorphDB/tags).

**The documents in this repository describe `main`, which is ahead of that image.** Anything they
describe that the published version does not serve yet is marked `Since x.y.z`. To read a
reference that matches what you are running, open it at its tag instead of on the default branch —
the tag carries the documents as they were when that version shipped.

### Which package

Three are published, and they answer different questions. **`MorphDB.Client` is the supported entry
point** — if you are talking to a running server, it is the only one you need.

| Package | For | |
|---|---|---|
| **`MorphDB.Client`** | Talking to a MorphDB server over HTTP | **Start here** |
| `MorphDB.Core` | Embedding MorphDB in your own process — the engine's contracts, models and exceptions | Advanced |
| `MorphDB.Npgsql` | The implementation of those contracts, for the same embedded use | Advanced |

The last two exist because the engine is a library before it is a service: they let a .NET host run
schema and data operations in-process against its own PostgreSQL, without a server in front. That is a
narrower path than the client and it is not what the API documentation describes — `docs/API.md` is
the wire contract, which an embedded host does not use.

All three move together on one version, so an embedded host upgrades as one unit.

"Embedded" here means no server process in front, not a different storage engine. PostgreSQL is the
only engine MorphDB runs on, by design: projects are PostgreSQL schemas, change notification is
`LISTEN`/`NOTIFY`, and schema changes are serialized with advisory locks. `MorphDB.Core` is split
from `MorphDB.Npgsql` to keep the engine's contracts apart from their implementation, not as an
interface for plugging in another database.

Run with PostgreSQL using docker-compose:

```yaml
# docker-compose.yml
services:
  morphdb:
    image: ghcr.io/iyulab/morphdb:0.18.0
    ports:
      # Bound to loopback on purpose: this quick-start injects no master secret, so nothing here
      # authenticates. To serve other machines, either set Security__MasterSecret (see
      # docs/API.md#connection-secrets) or put a reverse proxy in front and bind that.
      - "127.0.0.1:8080:8080"
    environment:
      # The service role created below -- not the image's superuser, which MorphDB refuses.
      ConnectionStrings__MorphDB: Host=postgres;Port=5432;Database=morphdb;Username=morph;Password=morph
    depends_on:
      postgres:
        condition: service_healthy

  postgres:
    image: postgres:16-alpine
    environment:
      # The bootstrap superuser (postgres) is for administration only.
      POSTGRES_PASSWORD: postgres
      POSTGRES_DB: morphdb
    configs:
      # Runs once, on an empty volume: creates the role MorphDB logs in as.
      - source: morphdb-service-role
        target: /docker-entrypoint-initdb.d/morphdb-service-role.sql
    volumes:
      - postgres_data:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -h 127.0.0.1 -U postgres -d morphdb"]
      interval: 10s
      timeout: 5s
      retries: 5

configs:
  morphdb-service-role:
    content: |
      CREATE ROLE morph LOGIN PASSWORD 'morph' NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE;
      ALTER DATABASE morphdb OWNER TO morph;

volumes:
  postgres_data:
```

> This is the file to copy — one file, nothing beside it (inline `configs` need Docker Compose
> 2.23.1 or later). The `docker-compose.yml` in the repository is the **development bundle** — it
> builds the service from source and keeps it behind `--profile app` — so `docker compose up -d`
> from a checkout starts the stores and not the server.

```bash
docker compose up -d

# Access
# REST API: http://localhost:8080/api/
# GraphQL:  http://localhost:8080/graphql
# Health:   http://localhost:8080/health
```

Platforms: `linux/amd64`, `linux/arm64`.
Tags: every release publishes `X.Y.Z` and `X.Y`, plus `latest`.

### Database role

MorphDB must connect as a role that is **neither a superuser nor `BYPASSRLS`**, and it refuses to
start otherwise (`INSECURE_DATABASE_ROLE`). PostgreSQL applies no row-level security to either kind
of role — not even with `FORCE ROW LEVEL SECURITY` — so every security policy declared through
MorphDB would be stored and silently ignored; a superuser connection would also let the service drop
the database. The role needs no other privilege than owning its database: MorphDB creates its schemas and
tables there, and it never needs `CREATE EXTENSION`.

The official `postgres` image always makes `POSTGRES_USER` a superuser, so that account is for
administration only and the service logs in as a second role. The quick-start above creates it; on
any other PostgreSQL, run this as a superuser, with your own password:

```sql
CREATE ROLE morph LOGIN PASSWORD '<password>' NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE;
ALTER DATABASE morphdb OWNER TO morph;
```

On a managed PostgreSQL (Azure, Cloud SQL, RDS), create the same role for MorphDB rather than
pointing it at the administrator account the provider gives you: what that account may do is the
provider's choice, and the start-up check reads `rolsuper` and `rolbypassrls` of whatever role it is
given.

**Upgrading a database created before 0.19.0.** The compose files before 0.19.0 made the image's
bootstrap superuser — `POSTGRES_USER: morph` — the service's login, so that database has no service
role yet, and PostgreSQL does not let the bootstrap superuser be demoted. Hand the database over to
a new role with [`scripts/migrate-to-service-role.sql`](scripts/migrate-to-service-role.sql), which
changes only ownership — no data is touched:

```bash
docker compose exec -T postgres psql -U morph -d morphdb   -v service_role=morphdb_service -v service_password='<password>'   < migrate-to-service-role.sql
```

then set `ConnectionStrings__MorphDB` to `Username=morphdb_service;Password=<password>` and start
the new image. Keep the existing `POSTGRES_USER` value in the compose file: it no longer creates
anything on a volume that is already initialised, and it is still the account you administer with.

### First request: create a project

Every schema and data endpoint is project-scoped, so a bare request is answered with a 400 carrying
`MISSING_PROJECT`. Create a project first, then send its id as `X-Project-Id` on everything else.

> **A project is a schema namespace, not a trust boundary.** It exists so MorphDB can operate physical
> schemas on its own judgement — it is an internal operating unit, not a multi-tenancy feature.
> `X-Project-Id` says *which* schemas a request means, never *whether the caller may have them* —
> so do not pass a client-supplied project id through.
>
> **Out of the box the service does not authenticate any endpoint.** Inject a master secret
> (`Security__MasterSecret`) and it requires `Authorization: Bearer <secret>` on everything but the
> health and metrics probes; you then issue further secrets, each carrying a role.
> See [Connection secrets](docs/API.md#connection-secrets). If you are building something
> multi-user, that boundary is still yours to stand, in front of MorphDB.

```bash
# 1. Create a project. The response carries the id you will scope requests with. The name is
#    slugged and must be unique: creating "my-app" a second time answers 409 DUPLICATE_SLUG.
PROJECT=$(curl -sS -X POST http://localhost:8080/api/projects \
  -H 'Content-Type: application/json' \
  -d '{"name":"my-app"}' | jq -r .id)

# 2. Create a table inside it.
curl -sS -X POST http://localhost:8080/api/schema/tables \
  -H "X-Project-Id: $PROJECT" \
  -H 'Content-Type: application/json' \
  -d '{"name":"customers","columns":[{"name":"email","type":"text","nullable":false}]}'

# 3. Write a row.
curl -sS -X POST http://localhost:8080/api/data/customers \
  -H "X-Project-Id: $PROJECT" \
  -H 'Content-Type: application/json' \
  -d '{"email":"ada@example.com"}'
```

The .NET client takes the same id once, at construction:

```csharp
var client = new MorphDBClient("http://localhost:8080", new MorphDBClientOptions { ProjectId = projectId });
```

> **A project's settings are replaced, not merged.** `PATCH /api/projects/{id}` with a `settings`
> object writes that object whole — anything you leave out goes back to its default rather than
> keeping its stored value. Read the project first and send the settings you want it to end up with.
> The client has no project surface, so these calls are made over HTTP either way; the trap is the
> same one described in [the API reference](docs/API.md#audit-retention).

### From Source

```bash
# Prerequisites: .NET 10.0, Docker

# Start the stores from the development bundle (the repository's docker-compose.yml)
docker compose up -d postgres

# Run
dotnet run --project src/MorphDB.Service

# Access: http://localhost:5400  -- the Kestrel development port (launchSettings.json).
# The container image listens on 8080; that is the port the Docker quick-start above uses.
```

## How It Works

MorphDB maps logical names to hash-based physical names, enabling schema changes without breaking queries:

```
"customers" → tbl_a7f3b2c1
"email"     → col_e9d8c7b6
```

```csharp
// Create table at runtime
await schemaManager.CreateTableAsync(projectId, new CreateTableRequest
{
    LogicalName = "customers",
    Columns = new[]
    {
        new ColumnDefinition { LogicalName = "name", DataType = MorphDataType.Text },
        new ColumnDefinition { LogicalName = "email", DataType = MorphDataType.Text }
    }
});

// Insert data using logical names
await dataService.InsertAsync(projectId, "customers", new Dictionary<string, object?>
{
    ["name"] = "John Doe",
    ["email"] = "john@example.com"
});
```

## Documentation

| Document | Description |
|----------|-------------|
| [Philosophy](docs/PHILOSOPHY.md) | Project vision, principles, and scope |
| [Architecture](docs/ARCHITECTURE.md) | System design and layer structure |
| [API Reference](docs/API.md) | REST, GraphQL, OData, WebSocket endpoints |
| [Changelog](CHANGELOG.md) | Release history |

## Project Structure

```
MorphDB/
├── src/
│   ├── MorphDB.Core/       # Abstractions and interfaces
│   ├── MorphDB.Npgsql/     # Database provider implementation
│   ├── MorphDB.Service/    # ASP.NET Core API service
│   └── MorphDB.Client/     # .NET client SDK
├── sdk/                    # archived Python/TypeScript clients -- frozen, not published, not maintained
│   ├── typescript/         # TypeScript
│   └── python/             # Python
├── desk/                   # Electron desktop app -- parked, see desk/README.md
└── tests/
```

## Use Cases

- **Spreadsheet-style databases** - Runtime schema + relational power
- **Low-code/No-code platforms** - API-first data layer
- **Dynamic form builders** - Schema-on-the-fly
- **CRM/ERP with custom fields** - Schema-isolated customization
- **Backends serving many projects** - One deployment, separate schemas (authorization stays in your app)

## License

Apache License 2.0 - see [LICENSE](./LICENSE)

---

See [docs/](docs/) for detailed documentation.
