using System.Globalization;
using Microsoft.OpenApi.Models;
using MorphDB.Npgsql;
using MorphDB.Npgsql.Security;
using MorphDB.Service.Extensions;
using MorphDB.Service.GraphQL;
using MorphDB.Service.OData;
using MorphDB.Service.Realtime;
using MorphDB.Service.Security;
using MorphDB.Service.Services;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture)
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting MorphDB Service");

    var builder = WebApplication.CreateBuilder(args);

    // Configure Serilog
    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture));

    // Add MorphDB services
    var connectionString = builder.Configuration.GetConnectionString("MorphDB")
        ?? throw new InvalidOperationException("Connection string 'MorphDB' not found.");

    builder.Services.AddMorphDbNpgsql(connectionString, options =>
    {
        options.RedisConnectionString = builder.Configuration.GetConnectionString("Redis");
    });

    // Configure JWT options
    builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));

    // Add authentication
    builder.Services.AddAuthentication(MorphDBAuthenticationExtensions.SchemeName)
        .AddMorphDB();

    builder.Services.AddAuthorization();

    // Add API services
    builder.Services.AddControllers();
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(options =>
    {
        options.SwaggerDoc("v1", new()
        {
            Title = "MorphDB API",
            Version = "v1",
            Description = "Dynamic schema database service API"
        });

        // Add API Key security definition
        options.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
        {
            Description = "API Key authentication. Use 'X-API-Key' header with your API key.",
            Name = "X-API-Key",
            In = ParameterLocation.Header,
            Type = SecuritySchemeType.ApiKey,
            Scheme = "ApiKey"
        });

        // Add JWT Bearer security definition
        options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
        {
            Description = "JWT Bearer authentication. Enter your token in the text input below.",
            Name = "Authorization",
            In = ParameterLocation.Header,
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT"
        });

        // Add Tenant ID header
        options.AddSecurityDefinition("TenantId", new OpenApiSecurityScheme
        {
            Description = "Tenant ID header for multi-tenant operations.",
            Name = "X-Tenant-Id",
            In = ParameterLocation.Header,
            Type = SecuritySchemeType.ApiKey
        });

        options.AddSecurityRequirement(new OpenApiSecurityRequirement
        {
            {
                new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "ApiKey" }
                },
                Array.Empty<string>()
            },
            {
                new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
                },
                Array.Empty<string>()
            },
            {
                new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "TenantId" }
                },
                Array.Empty<string>()
            }
        });
    });

    // Add HTTP context accessor for tenant context
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<ITenantContextAccessor, HttpTenantContextAccessor>();
    builder.Services.AddScoped<ISubscriptionEventSender, HotChocolateSubscriptionEventSender>();

    // Add OData services for dynamic EDM model generation
    builder.Services.AddSingleton<IEdmModelProvider>(sp =>
        new CachingEdmModelProvider(
            sp.GetRequiredService<IServiceScopeFactory>(),
            TimeSpan.FromMinutes(5)));
    builder.Services.AddScoped<ODataQueryHandler>();

    // Add GraphQL (HotChocolate) with dynamic MorphDB types
    builder.Services
        .AddGraphQLServer()
        .AddMorphDbTypes()
        .ModifyRequestOptions(opt => opt.IncludeExceptionDetails = builder.Environment.IsDevelopment());

    // Add real-time services (SignalR + PostgreSQL LISTEN/NOTIFY)
    builder.Services.AddMorphDbRealtime();

    // Add webhook delivery services
    builder.Services.AddWebhookDelivery(options =>
    {
        options.PollingInterval = TimeSpan.FromSeconds(5);
        options.MaxRetries = 5;
        options.HttpTimeout = TimeSpan.FromSeconds(30);
    });

    // Health checks
    builder.Services.AddHealthChecks();

    var app = builder.Build();

    // Configure the HTTP request pipeline
    app.UseSerilogRequestLogging();

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    app.UseHttpsRedirection();
    app.UseWebSockets(); // Required for GraphQL subscriptions
    app.UseAuthentication();
    app.UseAuthorization();

    app.MapControllers();
    app.MapGraphQL().WithOptions(new HotChocolate.AspNetCore.GraphQLServerOptions
    {
        Tool = { Enable = app.Environment.IsDevelopment() }
    });
    app.MapHealthChecks("/health");
    app.MapMorphHub(); // SignalR hub at /hubs/morph

    // Ready endpoint
    app.MapGet("/ready", () => Results.Ok(new { status = "ready", timestamp = DateTime.UtcNow }));

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

// Needed for WebApplicationFactory in integration tests
public partial class Program { }
