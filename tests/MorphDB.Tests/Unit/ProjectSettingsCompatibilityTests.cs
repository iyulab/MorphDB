using System.Text.Json;
using FluentAssertions;
using MorphDB.Core.Models;

namespace MorphDB.Tests.Unit;

/// <summary>
/// The quota and rate-limit fields were removed from <see cref="ProjectSettings"/> because nothing
/// enforced them; the constitution names billing and quota as a non-goal.
///
/// Rows written before that removal still carry those keys in the settings jsonb column, and
/// <c>ProjectRepository</c> deserializes them with no options object. These tests pin the two things
/// that claim was resting on: unmapped keys are skipped rather than rejected, and the fields that
/// survived still round-trip.
///
/// Known limit — say it rather than hide it: <c>ProjectRepository.MapToProject</c> is private, so these
/// tests call <c>JsonSerializer</c> the same way it does instead of calling it. That means they pin the
/// serializer's behaviour, not the repository's use of it. If someone hands the repository an options
/// object with <c>UnmappedMemberHandling.Disallow</c>, these tests stay green while old rows break. The
/// covering test would need the mapping reachable.
/// </summary>
public class ProjectSettingsCompatibilityTests
{
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
        var settings = JsonSerializer.Deserialize<ProjectSettings>(SettingsWrittenBeforeQuotaRemoval);

        settings.Should().NotBeNull("a project provisioned before the removal must remain readable");
    }

    /// <summary>
    /// The surviving fields must come back intact — skipping the removed keys must not cost the ones
    /// next to them.
    /// </summary>
    [Fact]
    public void Settings_WrittenBeforeQuotaRemoval_ShouldPreserveTheRemainingFields()
    {
        var settings = JsonSerializer.Deserialize<ProjectSettings>(SettingsWrittenBeforeQuotaRemoval)!;

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
        var settings = JsonSerializer.Deserialize<ProjectSettings>(SettingsWrittenBeforeQuotaRemoval)!;

        var json = JsonSerializer.Serialize(settings);

        json.Should().NotContain("axTables", "the quota fields are gone and must not be written back");
        json.Should().NotContain("axStorageBytes");
        json.Should().NotContain("ateLimits");
    }
}
