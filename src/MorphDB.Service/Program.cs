using System.Globalization;
using HealthChecks.UI.Client;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.OpenApi;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Encryption;
using MorphDB.Npgsql;
using MorphDB.Npgsql.Security;
using MorphDB.Service.Extensions;
using MorphDB.Service.GraphQL;
using MorphDB.Service.Infrastructure;
using MorphDB.Service.Middleware;
using MorphDB.Service.OData;
using MorphDB.Service.RateLimiting;
using MorphDB.Service.Realtime;
using MorphDB.Service.Security;
using MorphDB.Service.Services;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
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

    // Configure encryption from settings
    var encryptionSection = builder.Configuration.GetSection("Encryption");
    var encryptionOptions = new DataEncryptionOptions();
    encryptionSection.Bind(encryptionOptions);

    builder.Services.AddMorphDbNpgsql(connectionString, options =>
    {
        options.RedisConnectionString = builder.Configuration.GetConnectionString("Redis");

        // Enable encryption if master key is configured
        if (!string.IsNullOrEmpty(encryptionOptions.MasterKey))
        {
            options.EncryptionOptions = encryptionOptions;
            Log.Information("Data encryption enabled (Algorithm: {Algorithm}, KeyVersion: {KeyVersion})",
                encryptionOptions.Algorithm, encryptionOptions.KeyVersion);
        }
        else
        {
            Log.Information("Data encryption disabled (no master key configured)");
        }
    });

    // Configure JWT options
    builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));

    // Add authentication
    builder.Services.AddAuthentication(MorphDBAuthenticationExtensions.SchemeName)
        .AddMorphDB();

    builder.Services.AddAuthorization();

    // Add CORS for development (allows Electron dev server and other local clients)
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("Development", policy =>
        {
            policy.SetIsOriginAllowed(origin =>
                    new Uri(origin).Host == "localhost" ||
                    new Uri(origin).Host == "127.0.0.1")
                .AllowAnyMethod()
                .AllowAnyHeader()
                .AllowCredentials();
        });
    });

    // Add API services
    builder.Services.AddControllers(options =>
    {
        options.Filters.Add<MorphDB.Service.Filters.ProjectExceptionFilter>();
    });
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

        // Add Project ID header
        options.AddSecurityDefinition("ProjectId", new OpenApiSecurityScheme
        {
            Description = "The project a request is scoped to. A schema namespace, not a trust boundary.",
            Name = "X-Project-Id",
            In = ParameterLocation.Header,
            Type = SecuritySchemeType.ApiKey
        });

        options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference("ApiKey", document)] = [],
            [new OpenApiSecuritySchemeReference("Bearer", document)] = [],
            [new OpenApiSecuritySchemeReference("ProjectId", document)] = []
        });
    });

    // Add HTTP context accessor for project context
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<IProjectContextAccessor, HttpProjectContextAccessor>();
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

    // Add bulk job processor for background import/export processing
    builder.Services.AddBulkJobProcessor(options =>
    {
        options.PollingInterval = TimeSpan.FromSeconds(5);
        options.BatchSize = 5;
    });

    // Add rate limiting
    builder.Services.Configure<RateLimitConfig>(builder.Configuration.GetSection("RateLimiting"));
    builder.Services.AddSingleton<IRateLimiter, MemoryRateLimiter>();

    // Add graceful shutdown with request draining (Phase 24: Production Hardening)
    builder.Services.Configure<GracefulShutdownOptions>(builder.Configuration.GetSection("GracefulShutdown"));
    builder.Services.AddSingleton<GracefulShutdownService>();
    builder.Services.AddHostedService(sp => sp.GetRequiredService<GracefulShutdownService>());

    // Health checks with dependencies.
    // Redis is optional — the schema cache only registers when a connection string is configured
    // (see AddMorphDbNpgsql), so probing a default localhost endpoint would report the service
    // unhealthy for a dependency it is not using, and every probe would block on the connect timeout.
    // It is also a cache: when it is configured but down, reads fall back to the database, so it is
    // not part of readiness.
    var redisConnectionString = builder.Configuration.GetConnectionString("Redis");
    var healthChecks = builder.Services.AddHealthChecks()
        .AddNpgSql(connectionString, name: "postgresql", tags: ["db", "ready"]);

    if (!string.IsNullOrWhiteSpace(redisConnectionString))
    {
        healthChecks.AddRedis(redisConnectionString, name: "redis", tags: ["cache"]);
    }

    // OpenTelemetry configuration
    var serviceName = "MorphDB.Service";
    var serviceVersion = typeof(Program).Assembly.GetName().Version?.ToString() ?? "1.0.0";

    builder.Services.AddOpenTelemetry()
        .ConfigureResource(resource => resource
            .AddService(serviceName: serviceName, serviceVersion: serviceVersion)
            .AddAttributes(new Dictionary<string, object>
            {
                ["deployment.environment"] = builder.Environment.EnvironmentName.ToLowerInvariant()
            }))
        .WithTracing(tracing => tracing
            .AddAspNetCoreInstrumentation(options =>
            {
                options.RecordException = true;
                options.Filter = httpContext =>
                {
                    // Don't trace health check endpoints
                    var path = httpContext.Request.Path.Value ?? "";
                    return !path.StartsWith("/health", StringComparison.OrdinalIgnoreCase) &&
                           !path.StartsWith("/metrics", StringComparison.OrdinalIgnoreCase);
                };
            })
            .AddHttpClientInstrumentation()
            .AddOtlpExporter())
        .WithMetrics(metrics => metrics
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddRuntimeInstrumentation()
            .AddPrometheusExporter());

    var app = builder.Build();

    // Ensure global morphdb schema exists (replaces init.sql for embedded mode)
    await app.Services.EnsureMorphDbSchemaAsync();

    // Configure the HTTP request pipeline
    app.UseSerilogRequestLogging();
    app.UseRequestTracking(); // Graceful shutdown request tracking (Phase 24)

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
        app.UseCors("Development"); // Enable CORS for development
    }

    app.UseHttpsRedirection();
    app.UseWebSockets(); // Required for GraphQL subscriptions
    app.UseAuthentication();
    app.UseAuthorization();
    app.UseRateLimiting(); // Rate limiting after auth
    app.UseAuditLogging(); // Audit logging captures all requests including rate-limited ones

    app.MapControllers();
    app.MapGraphQL().WithOptions(new HotChocolate.AspNetCore.GraphQLServerOptions
    {
        Tool = { Enable = app.Environment.IsDevelopment() }
    });

    // Development-only bootstrap endpoint for creating initial API key
    if (app.Environment.IsDevelopment())
    {
        app.MapPost("/api/dev/bootstrap", async (MorphDB.Core.Security.IApiKeyService apiKeyService) =>
        {
            var projectId = Guid.NewGuid();
            var (key, rawKey) = await apiKeyService.CreateKeyAsync(
                projectId,
                MorphDB.Core.Security.ApiKeyType.Service,
                "Development Key",
                "Auto-generated development API key");

            return Results.Ok(new
            {
                message = "Development API key created successfully",
                projectId = projectId.ToString(),
                apiKey = rawKey,
                keyId = key.Id,
                warning = "This endpoint is only available in Development mode"
            });
        }).AllowAnonymous();

        Log.Information("Development bootstrap endpoint enabled: POST /api/dev/bootstrap");
    }

    // Health check endpoints
    app.MapHealthChecks("/health", new HealthCheckOptions
    {
        ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
    });
    app.MapHealthChecks("/health/live", new HealthCheckOptions
    {
        Predicate = _ => false, // No dependency checks for liveness
        ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
    });
    app.MapHealthChecks("/health/ready", new HealthCheckOptions
    {
        Predicate = check => check.Tags.Contains("ready"),
        ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
    });

    // Prometheus metrics endpoint
    app.MapPrometheusScrapingEndpoint("/metrics");

    app.MapMorphHub(); // SignalR hub at /hubs/morph

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
