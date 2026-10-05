using Microsoft.Extensions.DependencyInjection;
using MorphDB.Core.Encryption;
using MorphDB.Npgsql;

namespace MorphDB.Tests.Unit;

/// <summary>
/// A configuration that would encrypt columns is refused at startup.
/// <para>
/// Column encryption does not round-trip: a non-text column (the system <c>_version</c> among them)
/// cannot store the ciphertext, so every write answered an internal error; record queries returned the
/// ciphertext; and rotation and validation counted no encrypted column. Refusing the configuration
/// with the reason is the honest answer until the feature works — a service that starts and then fails
/// every write hides the cause behind a 500.
/// </para>
/// </summary>
public class EncryptionConfigurationTests
{
    private const string ConnectionString = "Host=localhost;Database=unused;Username=unused;Password=unused";
    private static readonly string MasterKey = Convert.ToBase64String(new byte[32]);

    [Fact]
    public void A_master_key_with_encrypt_all_by_default_is_refused_at_startup()
    {
        var register = () => new ServiceCollection().AddMorphDbNpgsql(ConnectionString, options =>
            options.EncryptionOptions = new DataEncryptionOptions { MasterKey = MasterKey, EncryptAllByDefault = true });

        register.Should().Throw<InvalidOperationException>()
            .WithMessage("*Column encryption is not available*EncryptAllByDefault*");
    }

    [Fact]
    public void A_master_key_that_encrypts_nothing_is_accepted()
    {
        // With EncryptAllByDefault off nothing is encrypted — no request can mark a column — so the
        // configuration is harmless and a deployment that set it keeps starting.
        var register = () => new ServiceCollection().AddMorphDbNpgsql(ConnectionString, options =>
            options.EncryptionOptions = new DataEncryptionOptions { MasterKey = MasterKey, EncryptAllByDefault = false });

        register.Should().NotThrow();
    }

    [Fact]
    public void A_disabled_master_key_is_accepted()
    {
        var register = () => new ServiceCollection().AddMorphDbNpgsql(ConnectionString, options =>
            options.EncryptionOptions = new DataEncryptionOptions { MasterKey = MasterKey, Enabled = false });

        register.Should().NotThrow();
    }

    [Fact]
    public void No_master_key_is_accepted()
    {
        var register = () => new ServiceCollection().AddMorphDbNpgsql(ConnectionString);

        register.Should().NotThrow();
    }
}
