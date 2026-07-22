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
| **Virtual Constraints** | FK, UNIQUE, CHECK at app layer; only PK/Index physical |
| **Blocked Access = Security** | No direct SQL = No SQL injection, full audit trail |
| **Encapsulated Complexity** | Simple API, complex internals hidden |

**Trade-offs**: We sacrifice direct SQL access and some advanced DB features in exchange for security, flexibility, and simplicity.

→ See [Philosophy](docs/PHILOSOPHY.md) for detailed design rationale.

## Quick Start

### Docker (Recommended)

```bash
docker pull ghcr.io/iyulab/morphdb:0.9.0
```

One number covers everything: a release publishes the git tag `vX.Y.Z`, the image `X.Y.Z`, and the
NuGet packages `X.Y.Z` together, so a client and a server that share a version are a compatible pair.
Pin that number rather than `latest` — this is a 0.x line and a minor release may break you. The
newest one is the newest [tag](https://github.com/iyulab/MorphDB/tags).

Run with PostgreSQL using docker-compose:

```yaml
# docker-compose.yml
services:
  morphdb:
    image: ghcr.io/iyulab/morphdb:0.9.0
    ports:
      # Bound to loopback on purpose: this quick-start compose has no authentication in front of
      # it. To serve other machines, put a reverse proxy (or your app) in front and bind that.
      - "127.0.0.1:8080:8080"
    environment:
      ConnectionStrings__MorphDB: Host=postgres;Port=5432;Database=morphdb;Username=morph;Password=morph
    depends_on:
      postgres:
        condition: service_healthy

  postgres:
    image: postgres:16-alpine
    environment:
      POSTGRES_USER: morph
      POSTGRES_PASSWORD: morph
      POSTGRES_DB: morphdb
    volumes:
      - postgres_data:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U morph -d morphdb"]
      interval: 10s
      timeout: 5s
      retries: 5

volumes:
  postgres_data:
```

```bash
docker compose up -d

# Access
# REST API: http://localhost:8080/api/
# GraphQL:  http://localhost:8080/graphql
# Health:   http://localhost:8080/health
```

Platforms: `linux/amd64`, `linux/arm64`.
Tags: every release publishes `X.Y.Z` and `X.Y`, plus `latest`.

### First request: create a project

Every schema and data endpoint is project-scoped, so a bare request is answered with a 400 carrying
`MISSING_PROJECT`. Create a project first, then send its id as `X-Project-Id` on everything else.

> **A project is a schema namespace, not a trust boundary.** It exists so MorphDB can operate physical
> schemas on its own judgement — it is an internal operating unit, not a multi-tenancy feature. The
> service does not authenticate any endpoint, so `X-Project-Id` says *which* schemas a
> request means, not *whether the caller may have them*. If you are building something multi-user, that
> boundary is yours to stand, in front of MorphDB. Do not pass a client-supplied project id through.

```bash
# 1. Create a project. The response carries the id you will scope requests with.
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

### From Source

```bash
# Prerequisites: .NET 10.0, Docker

# Start database
docker compose up -d postgres

# Run
dotnet run --project src/MorphDB.Service

# Access: http://localhost:5400
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
| [Roadmap](docs/ROADMAP.md) | Development phases and progress |

## Project Structure

```
MorphDB/
├── src/
│   ├── MorphDB.Core/       # Abstractions and interfaces
│   ├── MorphDB.Npgsql/     # Database provider implementation
│   ├── MorphDB.Service/    # ASP.NET Core API service
│   └── MorphDB.Client/     # .NET client SDK
├── sdk/                    # reference clients, not published -- see each README
│   ├── typescript/         # TypeScript
│   └── python/             # Python
├── desk/                   # Electron desktop app
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
