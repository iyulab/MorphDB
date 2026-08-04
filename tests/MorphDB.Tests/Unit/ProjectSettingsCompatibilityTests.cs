using AwesomeAssertions;
using MorphDB.Npgsql.Repositories;

namespace MorphDB.Tests.Unit;

/// <summary>
/// Two removals left rows behind. The quota and rate-limit fields went because nothing enforced them,
/// and rows written before that still carry those keys. Then the settings column gained an explicit
/// serialization policy, because it had been written in PascalCase while the REST surface spoke
/// camelCase — so rows exist in both shapes.
///
/// These tests hold what both of those rested on: unmapped keys are skipped rather than rejected, and
/// either shape still reads. They exercise <see cref="ProjectSettingsColumn"/> — the same code the
/// repository runs against the column — so changing the policy there turns these red. An earlier
/// version configured its own <c>JsonSerializer</c> the same way, which pinned the serializer's
/// behaviour but not the repository's use of it.
/// </summary>
public class ProjectSettingsCompatibilityTests
{
    /// <summary>
    /// The stored shape, not the wire shape. Before the policy existed the column was written with
    /// <c>JsonSerializer.Serialize</c> and no options, so the jsonb keys are PascalCase even though the
    /// REST surface speaks camelCase. Deserialization is case-sensitive by default, so a fixture written
    /// in the wire shape would prove nothing about the rows that actually exist.
    /// </summary>
    private const string SettingsWrittenBeforeQuotaRemoval = """
        {
          "DefaultLocale": "ko-KR",
          "Timezone": "Asia/Seoul",
          "EnableAuditLog": true,
          "MaxTables": 100,
          "MaxStorageBytes": 10737418240,
          "RateLimits": {
            "RequestsPerMinute": 1000,
            "RequestsPerHour": 50000,
            "MaxConcurrentConnections": 20
          },
          "Metadata": { "env": "prod" }
        }
        """;

    /// <summary>
    /// Deserialization must not throw on the removed keys. If a future change sets
    /// <c>UnmappedMemberHandling.Disallow</c>, or reads settings through options that do, every project
    /// provisioned before the removal becomes unreadable — this test goes red first.
    /// </summary>
    [Fact]
    public void Settings_WrittenBeforeQuotaRemoval_ShouldStillDeserialize()
    {
        var settings = ProjectSettingsColumn.Deserialize(SettingsWrittenBeforeQuotaRemoval);

        settings.Should().NotBeNull("a project provisioned before the removal must remain readable");
    }

    /// <summary>
    /// The surviving fields must come back intact — skipping the removed keys must not cost the ones
    /// next to them.
    /// </summary>
    [Fact]
    public void Settings_WrittenBeforeQuotaRemoval_ShouldPreserveTheRemainingFields()
    {
        var settings = ProjectSettingsColumn.Deserialize(SettingsWrittenBeforeQuotaRemoval)!;

        settings.DefaultLocale.Should().Be("ko-KR");
        settings.Timezone.Should().Be("Asia/Seoul");
        settings.EnableAuditLog.Should().BeTrue();
        settings.Metadata.Should().ContainKey("env").WhoseValue.Should().Be("prod");
    }

    /// <summary>
    /// A field added after a row was written must read as absent, not as a value. Audit retention
    /// is the case that makes this load-bearing rather than tidy: every project provisioned before
    /// the setting existed has no key for it, and a default that read as a window would start
    /// deleting their history the moment the setting shipped.
    /// </summary>
    [Fact]
    public void Settings_WrittenBeforeRetentionExisted_ShouldReadAsNoWindow()
    {
        var settings = ProjectSettingsColumn.Deserialize(SettingsWrittenBeforeQuotaRemoval)!;

        settings.AuditLogRetentionDays.Should().BeNull(
            "a project that never asked for a retention window keeps everything");
    }

    /// <summary>
    /// Reading an old row and writing it back must not carry the removed keys forward. Serialization is
    /// how the settings column is rewritten on update, so a re-save is where the stale keys drop out.
    /// </summary>
    [Fact]
    public void Settings_ReSerialized_ShouldNotCarryTheRemovedKeysForward()
    {
        var settings = ProjectSettingsColumn.Deserialize(SettingsWrittenBeforeQuotaRemoval)!;

        var json = ProjectSettingsColumn.Serialize(settings);

        json.Should().NotContain("axTables", "the quota fields are gone and must not be written back");
        json.Should().NotContain("axStorageBytes");
        json.Should().NotContain("ateLimits");
    }

    /// <summary>
    /// The column is written in camelCase now. Both shapes have to read, or the change that introduced
    /// the policy would have stranded every row written before it.
    /// </summary>
    [Fact]
    public void Settings_InEitherShape_ShouldReadTheSame()
    {
        const string camel = """{"defaultLocale":"ko-KR","timezone":"Asia/Seoul","enableAuditLog":true}""";
        const string pascal = """{"DefaultLocale":"ko-KR","Timezone":"Asia/Seoul","EnableAuditLog":true}""";

        var fromCamel = ProjectSettingsColumn.Deserialize(camel)!;
        var fromPascal = ProjectSettingsColumn.Deserialize(pascal)!;

        fromCamel.Timezone.Should().Be("Asia/Seoul");
        fromPascal.Timezone.Should().Be("Asia/Seoul", "rows written before the policy must still read");
        fromPascal.DefaultLocale.Should().Be(fromCamel.DefaultLocale);
    }

    /// <summary>
    /// The column is written camelCase — the shape the REST surface documents. If serialization ever
    /// reverts to bare <c>JsonSerializer.Serialize</c>, new rows silently go back to PascalCase and the
    /// stored shape diverges from the documented one again. This is the write-side half of the policy;
    /// the tests above only constrain reads.
    /// </summary>
    [Fact]
    public void Settings_Written_ShouldBeCamelCase()
    {
        var settings = ProjectSettingsColumn.Deserialize("""{"defaultLocale":"ko-KR"}""");

        var json = ProjectSettingsColumn.Serialize(settings);

        json.Should().Contain("\"defaultLocale\"").And.NotContain("\"DefaultLocale\"");
    }
}
