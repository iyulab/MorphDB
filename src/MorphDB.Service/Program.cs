using System.Globalization;
using MorphDB.Npgsql;
using MorphDB.Service.GraphQL;
using MorphDB.Service.OData;
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
