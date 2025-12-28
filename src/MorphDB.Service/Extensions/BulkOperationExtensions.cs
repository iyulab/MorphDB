using MorphDB.Service.Services;

namespace MorphDB.Service.Extensions;

/// <summary>
/// Extension methods for configuring bulk operation services.
/// </summary>
public static class BulkOperationExtensions
{
    /// <summary>
    /// Adds bulk job processor services to the service collection.
    /// </summary>
    public static IServiceCollection AddBulkJobProcessor(
        this IServiceCollection services,
        Action<BulkJobProcessorOptions>? configure = null)
    {
        var options = new BulkJobProcessorOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);
        services.AddHostedService<BulkJobProcessorService>();

        return services;
    }
}
