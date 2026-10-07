namespace MorphDB.Service.Infrastructure;

/// <summary>
/// Settings of features this service no longer has. A deployment still configuring one is refused
/// at start with the reason, rather than left believing the feature is on.
/// </summary>
public static class RemovedSettings
{
    /// <summary>
    /// Throws when <paramref name="configuration"/> still sets a removed feature's setting.
    /// </summary>
    public static void EnsureAbsent(IConfiguration configuration)
    {
        // Column encryption never round-tripped, and storage encryption belongs to PostgreSQL and
        // the disk beneath it.
        if (!string.IsNullOrEmpty(configuration["Encryption:MasterKey"]))
        {
            throw new InvalidOperationException(
                "Column encryption has been removed from MorphDB: encrypt storage at the PostgreSQL or disk " +
                "level, and remove the Encryption:MasterKey setting.");
        }
    }
}
