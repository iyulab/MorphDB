# MorphDB

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
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
docker pull ghcr.io/iyulab/morphdb:latest
```

Run with PostgreSQL using docker-compose:

```yaml
# docker-compose.yml
services:
  morphdb:
    image: ghcr.io/iyulab/morphdb:latest
    ports:
      - "8080:8080"
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

Available tags: `latest`, `0.2`, `0.2.0`
Platforms: `linux/amd64`, `linux/arm64`

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
await schemaManager.CreateTableAsync(tenantId, new CreateTableRequest
{
    LogicalName = "customers",
    Columns = new[]
    {
        new ColumnDefinition { LogicalName = "name", DataType = MorphDataType.Text },
        new ColumnDefinition { LogicalName = "email", DataType = MorphDataType.Text }
    }
});

// Insert data using logical names
await dataService.InsertAsync(tenantId, "customers", new Dictionary<string, object?>
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
├── sdk/
│   ├── typescript/         # TypeScript SDK
│   └── python/             # Python SDK
├── desk/                   # Electron desktop app
└── tests/
```

## Use Cases

- **Spreadsheet-style databases** - Runtime schema + relational power
- **Low-code/No-code platforms** - API-first data layer
- **Dynamic form builders** - Schema-on-the-fly
- **CRM/ERP with custom fields** - Tenant-isolated customization
- **Multi-tenant SaaS backends** - Secure data isolation

## License

MIT License - see [LICENSE](./LICENSE)

---

See [docs/](docs/) for detailed documentation.
