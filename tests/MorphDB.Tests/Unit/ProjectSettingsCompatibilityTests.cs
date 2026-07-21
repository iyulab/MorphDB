using System.Text.Json;
using FluentAssertions;
using MorphDB.Core.Models;

namespace MorphDB.Tests.Unit;

/// <summary>
/// Two removals left rows behind. The quota and rate-limit fields went because nothing enforced them,
/// and rows written before that still carry those keys. Then the settings column gained an explicit
/// serialization policy, because it had been written in PascalCase while the REST surface spoke
/// camelCase — so rows exist in both shapes.
///
/// These tests hold what both of those rested on: unmapped keys are skipped rather than rejected, and
/// either shape still reads.
///
/// Known limit — say it rather than hide it: <c>ProjectRepository.MapToProject</c> and its options are
/// private, so these tests configure <c>JsonSerializer</c> the same way instead of calling it. They pin
/// the serializer's behaviour, not the repository's use of it: change the repository's options and
/// these stay green while old rows break. Closing that needs the mapping reachable from a test.
/// </summary>
public class ProjectSettingsCompatibilityTests
{
    /// <summary>Mirrors the policy ProjectRepository applies to the settings column.</summary>
    private static readonly JsonSerializerOptions SettingsJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// The stored shape, not the wire shape. <c>ProjectRepository</c> serializes the settings column with
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
        var settings = JsonSerializer.Deserialize<ProjectSettings>(SettingsWrittenBeforeQuotaRemoval, SettingsJson);

        settings.Should().NotBeNull("a project provisioned before the removal must remain readable");
    }

    /// <summary>
    /// The surviving fields must come back intact — skipping the removed keys must not cost the ones
    /// next to them.
    /// </summary>
    [Fact]
    public void Settings_WrittenBeforeQuotaRemoval_ShouldPreserveTheRemainingFields()
    {
        var settings = JsonSerializer.Deserialize<ProjectSettings>(SettingsWrittenBeforeQuotaRemoval, SettingsJson)!;

        settings.DefaultLocale.Should().Be("ko-KR");
        settings.Timezone.Should().Be("Asia/Seoul");
        settings.EnableAuditLog.Should().BeTrue();
        settings.Metadata.Should().ContainKey("env").WhoseValue.Should().Be("prod");
    }

    /// <summary>
    /// Reading an old row and writing it back must not carry the removed keys forward. Serialization is
    /// how the settings column is rewritten on update, so a re-save is where the stale keys drop out.
    /// </summary>
    [Fact]
    public void Settings_ReSerialized_ShouldNotCarryTheRemovedKeysForward()
    {
        var settings = JsonSerializer.Deserialize<ProjectSettings>(SettingsWrittenBeforeQuotaRemoval, SettingsJson)!;

        var json = JsonSerializer.Serialize(settings, SettingsJson);

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

        var fromCamel = JsonSerializer.Deserialize<ProjectSettings>(camel, SettingsJson)!;
        var fromPascal = JsonSerializer.Deserialize<ProjectSettings>(pascal, SettingsJson)!;

        fromCamel.Timezone.Should().Be("Asia/Seoul");
        fromPascal.Timezone.Should().Be("Asia/Seoul", "rows written before the policy must still read");
        fromPascal.DefaultLocale.Should().Be(fromCamel.DefaultLocale);
    }
}
