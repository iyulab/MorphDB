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
    ProjectId = Guid.Parse("your-project-id")
});

// Projects. Everything else works inside one, so this is where a first run starts —
// `client.Projects` is the one surface that does not need a project id to have been set.
var project = await client.Projects.CreateAsync(new CreateProjectRequest { Name = "Catalogue" });
client.SetProjectId(project.Id);

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

- **Projects**: Create, read, update and delete projects, and read their storage and health reports
- **Schema Management**: Create, alter, and drop tables dynamically
- **Relations**: Declare links between tables, enforced on write or declared only
- **Data Operations**: CRUD operations with filtering, pagination, and ordering
- **Real-time Subscriptions**: WebSocket-based change notifications
- **Bulk Operations**: Import/export data in CSV, JSON, and XLSX formats
- **Webhooks**: Manage webhook subscriptions for event notifications
- **Type-safe API**: Strongly-typed request/response models

## Configuration

```csharp
var options = new MorphDBClientOptions
{
    ProjectId = projectId,
    Timeout = TimeSpan.FromSeconds(30),
    RetryCount = 3,
    RetryDelay = TimeSpan.FromSeconds(1)
};
```

## License

Apache License 2.0
