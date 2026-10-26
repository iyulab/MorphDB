namespace MorphDB.Service.Security;

/// <summary>
/// Said once, as the service starts: a service that enforces no credential is a deployment choice, and one nobody made on
/// purpose should be visible in the first lines of its log.
/// </summary>
internal static partial class OpenServiceLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "No master secret is injected (Security__MasterSecret): this service answers every caller that can reach it, and refuses nothing for want of a credential.")]
    public static partial void Open(ILogger logger);
}
