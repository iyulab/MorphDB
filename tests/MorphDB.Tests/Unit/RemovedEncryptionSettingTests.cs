using Microsoft.Extensions.Configuration;
using MorphDB.Service.Infrastructure;

namespace MorphDB.Tests.Unit;

/// <summary>
/// Column encryption was removed — it never round-tripped, and storage encryption belongs to
/// PostgreSQL and the disk beneath it. A deployment that still configures its key is refused when the
/// service starts, with the reason, rather than left believing its columns are encrypted.
/// </summary>
public sealed class RemovedEncryptionSettingTests
{
    private static IConfiguration Settings(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();

    [Fact]
    public void A_configured_encryption_key_refuses_the_start()
    {
        var check = () => RemovedSettings.EnsureAbsent(Settings(("Encryption:MasterKey", Convert.ToBase64String(new byte[32]))));

        check.Should().Throw<InvalidOperationException>()
            .WithMessage("*Column encryption has been removed*Encryption:MasterKey*");
    }

    [Fact]
    public void Without_an_encryption_key_the_start_proceeds()
    {
        var check = () => RemovedSettings.EnsureAbsent(Settings(("Encryption:MasterKey", "")));

        check.Should().NotThrow("an empty key — the shipped default of earlier versions — configures nothing");
    }
}
