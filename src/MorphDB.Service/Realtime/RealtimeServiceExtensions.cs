namespace MorphDB.Service.Realtime;

/// <summary>
/// Extension methods for registering real-time services.
/// </summary>
public static class RealtimeServiceExtensions
{
    /// <summary>
    /// Adds MorphDB real-time services including SignalR hub and PostgreSQL change listener.
    /// </summary>
    public static IServiceCollection AddMorphDbRealtime(this IServiceCollection services)
    {
        // Add SignalR
        services.AddSignalR(options =>
        {
            options.EnableDetailedErrors = true;
            options.MaximumReceiveMessageSize = 1024 * 1024; // 1MB
        });

        // Add subscription manager (singleton for shared state across connections)
        services.AddSingleton<SubscriptionManager>();

        // Add change notification setup
        services.AddSingleton<ChangeNotificationSetup>();

        // Add hosted services
        services.AddHostedService<ChangeNotificationInitializer>();
        services.AddHostedService<PostgresChangeListener>();

        return services;
    }

    /// <summary>
    /// Maps the MorphHub SignalR endpoint.
    /// </summary>
    public static IEndpointRouteBuilder MapMorphHub(this IEndpointRouteBuilder endpoints, string pattern = "/hubs/morph")
    {
        endpoints.MapHub<MorphHub>(pattern);
        return endpoints;
    }
}
