# MorphDB

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![.NET](https://img.shields.io/badge/.NET-10.0-blue.svg)](https://dotnet.microsoft.com/)

**Runtime-flexible relational database service for PostgreSQL**

MorphDB provides runtime schema flexibility while preserving PostgreSQL's power. Build Notion-style databases, dynamic forms, or any application requiring runtime data structures.

## Key Features

- **Dynamic Schema** - Create/modify tables and columns at runtime
- **Multi-Protocol** - REST, GraphQL, OData, WebSocket, Webhook
- **Real-time** - Live data sync via SignalR
- **Type Safety** - Native PostgreSQL types with logical name mapping

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

```bash
# Prerequisites: .NET 10.0, Docker

# Start PostgreSQL
docker-compose up -d

# Run
dotnet run --project src/MorphDB.Service

# Access
# REST API: http://localhost:5400/api/
# GraphQL:  http://localhost:5400/graphql
# Swagger:  http://localhost:5400/swagger
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
│   ├── MorphDB.Npgsql/     # PostgreSQL implementation
│   ├── MorphDB.Service/    # ASP.NET Core API service
│   └── MorphDB.Client/     # .NET client SDK
├── sdk/
│   ├── typescript/         # TypeScript SDK
│   └── python/             # Python SDK
├── desk/                   # Electron desktop app
└── tests/
```

## Use Cases

- Notion/Airtable-style databases
- Low-code/No-code platforms
- Dynamic form builders
- CRM/ERP with custom fields
- Multi-tenant SaaS backends

## License

MIT License - see [LICENSE](./LICENSE)

---

See [docs/](docs/) for detailed documentation.
