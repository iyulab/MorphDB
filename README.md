# MorphDB

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![.NET](https://img.shields.io/badge/.NET-10.0-blue.svg)](https://dotnet.microsoft.com/)

**Runtime-flexible relational database service for PostgreSQL**

MorphDB is a database service layer that provides runtime schema flexibility while preserving PostgreSQL's power. It's designed as the foundation for applications requiring dynamic data structures, similar to Notion Database or Airtable.

## Overview

MorphDB is an **open-source, MIT-licensed** library that can be used as a NuGet package or deployed as a self-hosted service. It provides:

- **Schema Management (DDL)** - Runtime table/column creation, modification, deletion
- **Data API (DML)** - Full CRUD operations with type safety
- **Multiple Protocols** - REST API, GraphQL, OData
- **Real-time** - WebSocket-based change subscriptions
- **Webhooks** - Event notifications for external system integration
- **Bulk Operations** - Import/Export for large datasets

## Why MorphDB?

Common approaches to dynamic schemas have significant drawbacks:

| Approach | Problems |
|----------|----------|
| NoSQL | Limited relations, transactions, complex queries |
| EAV Pattern | Query complexity, performance degradation |
| JSON Columns | Indexing limitations, no type safety |
| Dynamic DDL | Complex migrations on column rename |

MorphDB places an abstraction layer over real PostgreSQL schemas, maintaining all RDB benefits while supporting runtime schema changes.

## Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                        Client Layer                              │
│                                                                  │
│   REST API      GraphQL      OData      WebSocket     Webhook    │
│   /api/v1/*     /graphql     /odata/*   /ws          (outbound)  │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                      MorphDB.Service                             │
│                                                                  │
│  ┌────────────┐ ┌────────────┐ ┌────────────┐ ┌──────────────┐  │
│  │ Schema API │ │  Data API  │ │  Bulk API  │ │ Realtime Hub │  │
│  │   (DDL)    │ │   (DML)    │ │            │ │              │  │
│  └────────────┘ └────────────┘ └────────────┘ └──────────────┘  │
│                              │                                   │
│  ┌───────────────────────────────────────────────────────────┐  │
│  │              Dynamic Endpoint Generator                    │  │
│  │  • Table creation → auto-enabled endpoint (zero downtime)  │  │
│  │  • Dynamic GraphQL schema generation                       │  │
│  │  • Auto-refresh OData $metadata                            │  │
│  └───────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                        System Layer                              │
│                                                                  │
│   _morph_tables      Table mapping and metadata                  │
│   _morph_columns     Column mapping, types, constraints          │
│   _morph_relations   Table relationships                         │
│   _morph_indexes     Index mapping                               │
│   _morph_views       Saved views/filters                         │
│   _morph_enums       Enum options                                │
│   _morph_webhooks    Webhook subscription settings               │
│                                                                  │
│   "customers" → tbl_a7f3b2c1                                     │
│   "email" → col_e9d8c7b6                                         │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                        Data Layer                                │
│                                                                  │
│   tbl_a7f3b2c1 (col_e9d8c7b6, col_f1a2b3c4, ...)                │
│                                                                  │
│   * All identifiers are hash-based                               │
│   * Interpretable only through System Layer mapping              │
└─────────────────────────────────────────────────────────────────┘
```

## Core Features

- **Dynamic Schema** - Runtime table/column creation, modification, deletion
- **Logical Naming** - Separation of physical hash names and logical display names
- **Strong Typing** - Native PostgreSQL type utilization
- **Auto API** - Zero-downtime automatic REST/GraphQL/OData endpoint generation on schema changes
- **Real-time** - WebSocket-based data change subscriptions
- **Webhook** - Event notifications for external system integration
- **Bulk Operations** - Large-scale data Import/Export

## API Protocols

MorphDB supports multiple protocols to meet various client requirements:

| Protocol | Use Case | Features |
|----------|----------|----------|
| **REST API** | General CRUD, Schema management | Simple, broad compatibility |
| **GraphQL** | Flexible queries, Frontend | Select only needed fields, nested relations |
| **OData** | Enterprise integration | Excel, Power BI, SAP compatible |
| **WebSocket** | Real-time sync | Collaborative editing, live updates |
| **Webhook** | External system notifications | Automation, Zapier/n8n integration |
| **Bulk API** | Large data processing | Import/Export, migrations |

### REST API

```yaml
# Schema API (DDL)
POST   /api/schema/tables                    # Create table
GET    /api/schema/tables                    # List tables
GET    /api/schema/tables/{name}             # Get table details
PATCH  /api/schema/tables/{name}             # Update table
DELETE /api/schema/tables/{name}             # Delete table

POST   /api/schema/tables/{name}/columns     # Add column
PATCH  /api/schema/tables/{name}/columns/{col}  # Update column
DELETE /api/schema/tables/{name}/columns/{col}  # Delete column

POST   /api/schema/relations                 # Create relation
POST   /api/schema/indexes                   # Create index
POST   /api/schema/batch                     # Batch DDL

# Data API (DML) - Auto-generated per table
GET    /api/data/{table}                     # List (filter, sort, pagination)
GET    /api/data/{table}/{id}                # Get single record
POST   /api/data/{table}                     # Create
PATCH  /api/data/{table}/{id}                # Update
DELETE /api/data/{table}/{id}                # Delete
POST   /api/data/{table}/query               # Complex query
POST   /api/data/{table}/batch               # Batch DML
```

### GraphQL

Tables automatically generate GraphQL types and queries/mutations:

```graphql
# Auto-generated schema example
type Customer {
  id: ID!
  name: String!
  email: String
  createdAt: DateTime!
  grade: CustomerGrade
  orders: [Order!]!          # Relation auto-resolved
}

type Query {
  customer(id: ID!): Customer
  customers(
    filter: CustomerFilter
    orderBy: CustomerOrderBy
    first: Int
    after: String
  ): CustomerConnection!
}

type Mutation {
  createCustomer(input: CustomerInput!): Customer!
  updateCustomer(id: ID!, input: CustomerInput!): Customer!
  deleteCustomer(id: ID!): Boolean!
}

type Subscription {
  customerChanged(filter: CustomerFilter): CustomerChangeEvent!
}
```

### OData

Standard OData v4 protocol support for enterprise tool integration:

```http
# Metadata
GET /odata/$metadata

# Queries
GET /odata/Customers?$filter=grade eq 'VIP'&$orderby=createdAt desc&$top=10
GET /odata/Customers?$expand=orders&$select=name,email

# CUD
POST /odata/Customers
PATCH /odata/Customers('id')
DELETE /odata/Customers('id')
```

Supported: `$filter`, `$orderby`, `$top`, `$skip`, `$select`, `$expand`, `$count`

### WebSocket (Real-time)

```javascript
const ws = new WebSocket('wss://api.example.com/ws');

// Subscribe to table
ws.send(JSON.stringify({
  type: 'subscribe',
  table: 'customers',
  filter: { grade: 'VIP' }
}));

// Receive change events
ws.onmessage = (event) => {
  const { type, table, operation, data, previous } = JSON.parse(event.data);
  // operation: 'insert' | 'update' | 'delete'
};
```

### Webhook

```http
# Register webhook
POST /api/webhooks
{
  "name": "Order notification",
  "table": "orders",
  "events": ["insert", "update"],
  "url": "https://external.system/callback",
  "headers": {
    "Authorization": "Bearer xxx"
  },
  "filter": { "status": "completed" },
  "secret": "webhook-signing-secret"
}
```

### Bulk API

```http
# CSV Import
POST /api/bulk/{table}/import
Content-Type: text/csv

name,email,grade
John Doe,john@example.com,VIP
Jane Doe,jane@example.com,Standard

# Export
GET /api/bulk/{table}/export?format=csv
GET /api/bulk/{table}/export?format=json
GET /api/bulk/{table}/export?format=xlsx
```

## Data Types

### Primitive Types

| MorphDB Type | PostgreSQL Type | Description |
|--------------|-----------------|-------------|
| `text` | varchar(n), text | String |
| `integer` | int2, int4, int8 | Integer |
| `decimal` | numeric(p,s) | Fixed-point decimal |
| `float` | float4, float8 | Floating-point |
| `boolean` | boolean | True/False |
| `date` | date | Date |
| `time` | time | Time |
| `timestamp` | timestamp | DateTime |
| `timestamptz` | timestamptz | DateTime with timezone |
| `uuid` | uuid | UUID |
| `json` | jsonb | JSON data |

### Extended Types

| MorphDB Type | Implementation | Description |
|--------------|----------------|-------------|
| `enum` | PostgreSQL enum / lookup | Single selection |
| `enum[]` | array | Multi-selection |
| `file` | uuid + meta table | File reference |
| `encrypted` | bytea + encrypt/decrypt | Encrypted text |
| `auto_number` | sequence | Auto-increment |
| `computed` | GENERATED ALWAYS AS | Computed field |

## Packages

```
MorphDB/
├── src/
│   ├── MorphDB.Core/           # Core abstractions, types, interfaces
│   ├── MorphDB.Npgsql/         # PostgreSQL implementation
│   ├── MorphDB.Service/        # ASP.NET Core service
│   │   ├── Controllers/        # REST API controllers
│   │   ├── GraphQL/            # HotChocolate-based GraphQL
│   │   ├── OData/              # OData endpoints
│   │   ├── Realtime/           # WebSocket/SignalR hub
│   │   └── Bulk/               # Import/Export processing
│   └── MorphDB.Client/         # .NET client SDK
├── clients/
│   ├── typescript/             # TypeScript/JavaScript SDK
│   └── python/                 # Python SDK
└── tests/
```

## Quick Start

### Prerequisites

- .NET 10.0 SDK
- Docker & Docker Compose
- PostgreSQL 15+ (or use Docker)

### Development Setup

```bash
# Clone repository
git clone https://github.com/iyulab/morphdb.git
cd morphdb

# Start development environment
docker-compose up -d

# Build and test
dotnet build
dotnet test
```

### As a Library

```bash
dotnet add package MorphDB.Core
dotnet add package MorphDB.Npgsql
```

```csharp
// Register services
services.AddMorphDbNpgsql(connectionString);

// Use in your application
var schemaManager = serviceProvider.GetRequiredService<ISchemaManager>();
var dataService = serviceProvider.GetRequiredService<IMorphDataService>();

// Create a table
await schemaManager.CreateTableAsync(tenantId, new CreateTableRequest
{
    LogicalName = "customers",
    Columns = new[]
    {
        new ColumnDefinition { LogicalName = "name", DataType = MorphDataType.Text },
        new ColumnDefinition { LogicalName = "email", DataType = MorphDataType.Text, IsUnique = true }
    }
});

// Insert data
var customer = await dataService.InsertAsync(tenantId, "customers", new Dictionary<string, object?>
{
    ["name"] = "John Doe",
    ["email"] = "john@example.com"
});
```

### As a Service

```bash
# Run the service
cd src/MorphDB.Service
dotnet run

# Access endpoints
# REST: http://localhost:5000/api/
# GraphQL: http://localhost:5000/graphql
# Swagger: http://localhost:5000/swagger
```

## Use Cases

- Notion/Airtable-style databases
- Low-code/No-code platforms
- Dynamic form builders
- CRM/ERP requiring custom fields
- SaaS backends
- Real-time collaborative applications

## Development Status

See [ROADMAP.md](./ROADMAP.md) for detailed implementation phases.

### Current Progress

- **Phase 0-7**: ✅ Completed (Foundation, Schema, Data, Query, REST API, GraphQL, OData, Real-time)
- **Phase 8-12**: Planned (Webhook, Bulk, SDKs, Security, Deployment)

### Version Milestones

| Version | Phases | Goal |
|---------|--------|------|
| 0.1.0 | 0-3 | Core functionality |
| 0.2.0 | 4-6 | API layer complete |
| 0.3.0 | 7-8 | Real-time features |
| 0.4.0 | 9-10 | Bulk & SDKs |
| 0.5.0 | 11-12 | Production Ready (Beta) |

## Contributing

Contributions are welcome! Please read our contributing guidelines and submit pull requests for any improvements.

```bash
# Create feature branch
git checkout -b feature/your-feature
# ... develop ...
git push origin feature/your-feature
# Create PR
```

## License

MIT License - see [LICENSE](./LICENSE) for details.

---

**Note**: For enterprise features (multi-tenancy, admin dashboard, SSO integration, backup/recovery, audit logs), see [MorphDB Enterprise](https://github.com/iyulab/morphdb-platform) (commercial license).
