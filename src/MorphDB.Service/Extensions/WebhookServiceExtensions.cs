using MorphDB.Core.Abstractions;
using MorphDB.Service.Services;

namespace MorphDB.Service.Extensions;

/// <summary>
/// Extension methods for registering webhook services.
/// </summary>
public static class WebhookServiceExtensions
{
    /// <summary>
    /// Adds webhook delivery services to the service collection.
    /// </summary>
    public static IServiceCollection AddWebhookDelivery(
        this IServiceCollection services,
        Action<WebhookProcessorOptions>? configure = null)
    {
        var options = new WebhookProcessorOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);
        services.AddSingleton<WebhookSignatureService>();

        // Configure HttpClient for webhook delivery
        services.AddHttpClient<IWebhookDeliveryService, WebhookDeliveryService>(client =>
        {
            client.Timeout = options.HttpTimeout;
            client.DefaultRequestHeaders.Add("User-Agent", "MorphDB-Webhook/1.0");
            client.DefaultRequestHeaders.Add("Accept", "application/json");
        });

        // Register background processor
        services.AddHostedService<WebhookProcessorService>();

        return services;
    }
}
