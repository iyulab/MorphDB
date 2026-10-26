# MorphDB.Mcp

A read-only [MCP](https://modelcontextprotocol.io) door for a MorphDB service.

The service hosts it: it asks the service's own authentication and project scoping first, then answers four tools
from the same read paths the REST API uses — `describe_tables`, `describe_table`, `query_records` and `get_record`. There are no
tools that write, and the port the tools stand on (`IMorphReadPort`) has no member that could.

It is off unless `MorphDB:Mcp:Enabled` is `true`, and the service refuses to start with it on while it enforces no
authentication (`Security:MasterSecret` unset): an unauthenticated REST API is a choice, an unauthenticated door that
agents call by themselves is not one to make by omission.

```csharp
builder.Services.AddMorphDbMcp(builder.Configuration, authenticationEnforced: …);
builder.Services.AddScoped<IMorphReadPort, YourReadPort>();
app.MapMorphDbMcp();   // maps nothing when the door is off
```

A request names its project with the `X-Project-Id` header, as on the REST API — never with a tool argument.
