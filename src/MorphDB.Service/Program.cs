using System.Globalization;
using HealthChecks.UI.Client;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.OpenApi;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Encryption;
using MorphDB.Core.Security;
using MorphDB.Npgsql;
using MorphDB.Npgsql.Security;
using MorphDB.Service;
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

// The container HEALTHCHECK invokes this. It returns before any host is built, so probing costs a
// CLR start and nothing else.
if (args is [HealthProbe.Argument])
{
    return await HealthProbe.RunAsync().ConfigureAwait(false);
}

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
    builder.Services.AddControllers()
        // Model-binding failures answer the standard envelope, not ProblemDetails (one error shape).
        .ConfigureApiBehaviorOptions(options =>
        {
            options.InvalidModelStateResponseFactory = MorphDB.Service.ErrorHandling.StrictRequestBinding.InvalidModelStateResponse;
        })
        .AddJsonOptions(options =>
        {
            // Fail-loud request envelopes: an unmapped JSON member on an API model is a 400, never a
            // silent drop (see StrictRequestBinding).
            options.JsonSerializerOptions.TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver
            {
                Modifiers = { MorphDB.Service.ErrorHandling.StrictRequestBinding.DisallowUnmappedMembers },
            };
        });

    // One authority for what an escaped exception becomes on the wire. Without it, anything a
    // controller's catch chain does not name is a framework-default 500 with an empty body.
    builder.Services.AddExceptionHandler<MorphDB.Service.ErrorHandling.GlobalExceptionHandler>();
    builder.Services.AddProblemDetails();
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(options =>
    {
        options.SwaggerDoc("v1", new()
        {
            Title = "MorphDB API",
            Version = "v1",
            Description = "Dynamic schema database service API"
        });

        // Two cross-cutting headers, and they answer different questions. The project scope says
        // which schemas a request means; the secret says whether the caller may have them. The
        // second is only required when a deployment has injected a master secret -- with none
        // injected the service authenticates nothing, which is the default shape.
        options.AddSecurityDefinition("ProjectId", new OpenApiSecurityScheme
        {
            Description = "The project a request is scoped to. A schema namespace, not a trust boundary.",
            Name = "X-Project-Id",
            In = ParameterLocation.Header,
            Type = SecuritySchemeType.ApiKey
        });

        options.AddSecurityDefinition("Secret", new OpenApiSecurityScheme
        {
            Description =
                "A connection secret, when one is required. Required on every endpoint except the " +
                "health and metrics probes if Security__MasterSecret is injected; ignored otherwise.",
            Scheme = "bearer",
            Type = SecuritySchemeType.Http
        });

        options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference("ProjectId", document)] = [],
            [new OpenApiSecuritySchemeReference("Secret", document)] = []
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

    // Connection secrets. The master secret arrives the way POSTGRES_PASSWORD does — from the
    // deployment, before anything can ask for it (Security__MasterSecret) — so the authority to
    // issue credentials never originates inside the API. Its presence is also the switch: with
    // nothing injected the service authenticates nothing, exactly as it did before secrets existed.
    var secretOptions = builder.Configuration.GetSection(SecretOptions.SectionName).Get<SecretOptions>()
        ?? new SecretOptions();
    builder.Services.AddSingleton(secretOptions);
    builder.Services.AddSingleton<ISecretService, SecretService>();

    // Add rate limiting
    builder.Services.Configure<RateLimitConfig>(builder.Configuration.GetSection("RateLimiting"));
    builder.Services.AddSingleton<IRateLimiter, MemoryRateLimiter>();

    // Apply each project's audit retention window. Keeping the audit table within its declared
    // size is this server's obligation — the caller has no path to that table.
    var auditRetentionOptions = new AuditRetentionOptions();
    builder.Configuration.GetSection("AuditRetention").Bind(auditRetentionOptions);
    builder.Services.AddSingleton(auditRetentionOptions);
    builder.Services.AddHostedService<AuditRetentionService>();

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
    app.UseExceptionHandler();
    app.UseSerilogRequestLogging();
    app.UseRequestTracking(); // Graceful shutdown request tracking (Phase 24)

    // The OpenAPI document is the service's machine-readable contract; a deployed image serves it
    // like any other read-only surface. Gating it behind Development made the deployed contract a
    // 404 while the code carried it all along.
    app.UseSwagger();
    app.UseSwaggerUI();

    if (app.Environment.IsDevelopment())
    {
        app.UseCors("Development"); // Enable CORS for development
    }

    app.UseHttpsRedirection();
    app.UseWebSockets(); // Required for GraphQL subscriptions
    app.UseSecurityContext();
    app.UseSecretAuthentication(); // Enforces secrets when one is injected; before rate limiting and
                                   // audit logging so a denial is still counted and still recorded
    app.UseRateLimiting(); // Rate limiting after auth
    app.UseAuditLogging(); // Audit logging captures all requests including rate-limited ones

    app.MapControllers();
    app.MapGraphQL().WithOptions(new HotChocolate.AspNetCore.GraphQLServerOptions
    {
        Tool = { Enable = app.Environment.IsDevelopment() }
    });

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

    // One line of posture on every start: nothing here authenticates, by design. Access control is
    // the deployment's job — bind privately, or front with an authenticating proxy.
    Log.Warning("MorphDB serves every endpoint to any caller that can reach it (no authentication). Bind it privately or front it with an authenticating proxy.");

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");

    // Exit non-zero, or the container reports a clean shutdown after a fatal start-up failure and
    // `restart: on-failure` never fires.
    return 1;
}
finally
{
    Log.CloseAndFlush();
}

return 0;

// Needed for WebApplicationFactory in integration tests
public partial class Program { }
