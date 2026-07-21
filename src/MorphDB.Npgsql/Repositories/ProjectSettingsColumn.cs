using System.Text.Json;
using MorphDB.Core.Models;

namespace MorphDB.Npgsql.Repositories;

/// <summary>
/// How the settings jsonb column is written and read.
/// <para>
/// Without an options object the column was written in PascalCase while the REST surface spoke
/// camelCase, so the stored shape and the documented shape disagreed — invisible while the same
/// code did both halves, and a trap for anyone querying the column directly. Naming the policy
/// makes the two agree; reading case-insensitively keeps the rows written under the old shape
/// readable, which is why no migration is needed here.
/// </para>
/// <para>
/// This is the single place that policy lives. The compatibility tests exercise these methods
/// rather than a copy of the options, so changing the policy here turns them red — a copy would
/// have let the two drift while every test stayed green.
/// </para>
/// </summary>
internal static class ProjectSettingsColumn
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    internal static string Serialize(ProjectSettings? settings) =>
        JsonSerializer.Serialize(settings, Options);

    internal static ProjectSettings? Deserialize(string? column) =>
        string.IsNullOrEmpty(column)
            ? null
            : JsonSerializer.Deserialize<ProjectSettings>(column, Options);
}
