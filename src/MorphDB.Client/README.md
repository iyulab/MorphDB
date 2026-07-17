# MorphDB.Client

Official .NET client SDK for MorphDB - a PostgreSQL-based dynamic schema database service.

## Installation

```bash
dotnet add package MorphDB.Client
```

## Quick Start

```csharp
using MorphDB.Client;

// Create client
var client = new MorphDBClient("http://localhost:5000", new MorphDBClientOptions
{
    TenantId = Guid.Parse("your-tenant-id"),
    ApiKey = "your-api-key"
});

// Schema Management
await client.Schema.CreateTableAsync(new CreateTableRequest
{
    Name = "users",
    Columns = new[]
    {
        new CreateColumnRequest { Name = "name", Type = "text" },
        new CreateColumnRequest { Name = "email", Type = "text", IsUnique = true },
        new CreateColumnRequest { Name = "age", Type = "integer" }
    }
});

// Data Operations
var user = await client.Data.InsertAsync("users", new Dictionary<string, object?>
{
    ["name"] = "John Doe",
    ["email"] = "john@example.com",
    ["age"] = 30
});

// Query with filters
var adults = await client.Data.QueryAsync("users", new QueryRequest
{
    Filters = new[] { new Filter("age", FilterOperator.GreaterThanOrEqual, 18) },
    OrderBy = new[] { new OrderBy("name", ascending: true) },
    PageSize = 10
});

// Real-time subscriptions
await client.Realtime.SubscribeAsync("users", async (change) =>
{
    Console.WriteLine($"Change: {change.Operation} on {change.TableName}");
});
```

## Features

- **Schema Management**: Create, alter, and drop tables dynamically
- **Data Operations**: CRUD operations with filtering, pagination, and ordering
- **Real-time Subscriptions**: WebSocket-based change notifications
- **Bulk Operations**: Import/export data in CSV, JSON, and XLSX formats
- **Webhooks**: Manage webhook subscriptions for event notifications
- **Type-safe API**: Strongly-typed request/response models

## Configuration

```csharp
var options = new MorphDBClientOptions
{
    TenantId = tenantId,
    ApiKey = "your-api-key",      // API key for authentication
    Timeout = TimeSpan.FromSeconds(30),
    RetryCount = 3,
    RetryDelay = TimeSpan.FromSeconds(1)
};
```

## License

Apache License 2.0
