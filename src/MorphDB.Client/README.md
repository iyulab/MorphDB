# MorphDB.Client

Official .NET client SDK for MorphDB - a PostgreSQL-based dynamic schema database service.

## Installation

```bash
dotnet add package MorphDB.Client
```

## Quick Start

<!-- snippet: compile packages="MorphDB.Client" -->
```csharp
using MorphDB.Client;
using MorphDB.Client.Models;

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
        new CreateColumnRequest { Name = "email", Type = "text", Unique = true },
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

// Real-time subscriptions — changes arrive one at a time, in order, and the next one waits
// for the callback's task, so the callback can await its own work
await client.Realtime.SubscribeAsync("users", async change =>
{
    await File.AppendAllTextAsync("changes.log", $"{change.Operation} {change.TableName} {change.RecordId}{Environment.NewLine}");
});

// A callback with nothing to await takes the plain form
await client.Realtime.SubscribeAsync("users", change =>
    Console.WriteLine($"Change: {change.Operation} on {change.TableName}"));
```

## Features

- **Projects**: Create, read, update and delete projects, and read their storage and health reports
- **Schema Management**: Create, alter, and drop tables dynamically
- **Relations**: Declare links between tables, enforced on write or declared only
- **Indexes**: Create composite indexes over named columns; read them back with the table
- **Data Operations**: CRUD operations with filtering, pagination, and ordering
- **Hierarchy**: Walk a table whose rows point at a parent — ancestors, descendants, subtree, path, siblings — and check for cycles
- **Real-time Subscriptions**: WebSocket-based change notifications
- **Bulk Operations**: Import/export data in CSV, JSON, and XLSX formats
- **Webhooks**: Manage webhook subscriptions for event notifications
- **Security**: Row-level security policies on a table, and issuing, listing and revoking secrets (with the master secret)
- **Type-safe API**: Strongly-typed request/response models

## Configuration

```csharp
var options = new MorphDBClientOptions
{
    ProjectId = projectId,
    Secret = Environment.GetEnvironmentVariable("MORPHDB_SECRET"), // sent as Authorization: Bearer
    Timeout = TimeSpan.FromSeconds(30)
};
```

`Secret` is presented on every REST call and on the real-time connection. A service started with a
master secret refuses requests without one (`MorphDBAuthenticationException`); managing secrets
(`client.Security.IssueSecretAsync` and the rest) needs the master secret itself.

The client does not retry. To retry, build the `HttpClient` yourself — with a resilience handler, for
instance — and pass it to `new MorphDBClient(httpClient, options)`; `ProjectId` and `Secret` are
applied to it as well.

## License

Apache License 2.0
