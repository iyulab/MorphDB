using System.Globalization;
using Microsoft.Extensions.Primitives;
using MorphDB.Core.Models;
using MorphDB.Core.Pipeline;

namespace MorphDB.Service.Filters;

/// <summary>
/// Marks a write action as one that evaluates <c>If-Match</c> against the row's entity tag, so
/// <see cref="RejectConditionalHeadersAttribute"/> lets that one header through to it. Every other conditional
/// header (<c>If-None-Match</c>, <c>If-Unmodified-Since</c>) is still refused everywhere — no door evaluates them.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class AcceptsIfMatchAttribute : Attribute;

/// <summary>What an <c>If-Match</c> header on a write asks of the row.</summary>
internal enum IfMatchKind
{
    /// <summary>No header: the write is unconditional, as it always was.</summary>
    Absent,

    /// <summary><c>*</c>: the row must exist — which the route has already established.</summary>
    Any,

    /// <summary>One strong entity tag naming a row version: the write goes only if the row is still at it.</summary>
    Version,

    /// <summary>
    /// A tag no row can match — a weak tag, or one that is not a version this service issued. RFC 9110 §13.1.1
    /// compares <c>If-Match</c> strongly, so the condition is false and the write is not applied.
    /// </summary>
    NeverMatches,

    /// <summary>A header this service cannot honour (several tags, or not an entity-tag list at all).</summary>
    Unsupported,
}

/// <summary>A parsed <c>If-Match</c> header.</summary>
internal readonly record struct IfMatch(IfMatchKind Kind, int Version = 0, string? Problem = null)
{
    internal static readonly IfMatch None = new(IfMatchKind.Absent);
}

/// <summary>
/// The entity tag of a row and the reading of <c>If-Match</c> against it (RFC 9110 §8.8.3 and §13.1.1; OData 4.01
/// §11.4.1.1). A row's tag is its <c>_version</c> as a strong entity tag, <c>"3"</c>: it changes with every write
/// to the row, and <c>If-Match</c> must be compared strongly, so a weak tag (<c>W/"3"</c>) never matches.
/// <para>
/// The tag names the row's state, not one rendering of it: a read with <c>$select</c> shows fewer columns under the
/// same tag. That is what a client wants the precondition to mean — "the row has not changed since I read it" — and
/// it is the same meaning <c>_version</c> has in the write pipeline.
/// </para>
/// </summary>
internal static class EntityTags
{
    /// <summary>The entity tag for a row version.</summary>
    internal static string For(int version) => string.Create(CultureInfo.InvariantCulture, $"\"{version}\"");

    /// <summary>The entity tag of <paramref name="row"/>, or null when the row carries no version (a table that keeps none).</summary>
    internal static string? ForRow(IDictionary<string, object?>? row)
    {
        if (row is null || !row.TryGetValue(SystemColumns.Version, out var value) || value is null)
        {
            return null;
        }

        return value switch
        {
            int i => For(i),
            long l when l is >= int.MinValue and <= int.MaxValue => For((int)l),
            _ => int.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)
                ? For(v)
                : null,
        };
    }

    /// <summary>Reads an <c>If-Match</c> header.</summary>
    internal static IfMatch ParseIfMatch(StringValues header)
    {
        if (header.Count == 0)
        {
            return IfMatch.None;
        }

        // A list may arrive as several header lines or as one comma-separated line; either way it is a list.
        var members = header
            .SelectMany(line => (line ?? string.Empty).Split(','))
            .Select(m => m.Trim())
            .Where(m => m.Length > 0)
            .ToList();

        if (members.Count == 0)
        {
            return new IfMatch(IfMatchKind.Unsupported, Problem: "The 'If-Match' header is empty: name an entity tag, or '*'.");
        }

        if (members is ["*"])
        {
            return new IfMatch(IfMatchKind.Any);
        }

        if (members.Count > 1)
        {
            return new IfMatch(
                IfMatchKind.Unsupported,
                Problem: "The 'If-Match' header names several entity tags; a row has one version at a time, so name the one you read.");
        }

        var tag = members[0];
        if (tag.StartsWith("W/", StringComparison.Ordinal))
        {
            return new IfMatch(IfMatchKind.NeverMatches);
        }

        if (tag.Length < 2 || tag[0] != '"' || tag[^1] != '"')
        {
            return new IfMatch(
                IfMatchKind.Unsupported,
                Problem: "The 'If-Match' header is not an entity tag: an entity tag is a quoted string such as \"3\" (RFC 9110 §8.8.3), or '*'.");
        }

        return int.TryParse(tag[1..^1], NumberStyles.None, CultureInfo.InvariantCulture, out var version)
            ? new IfMatch(IfMatchKind.Version, version)
            : new IfMatch(IfMatchKind.NeverMatches);
    }
}

/// <summary>Why a request's <c>If-Match</c> is answered rather than evaluated: the status, the REST error code, and what to tell the caller.</summary>
internal sealed record PreconditionProblem(int Status, string Code, string Message);

/// <summary>
/// Reads the <c>If-Match</c> of a single-row write against the table it writes to, for every door that evaluates it.
/// A header the service cannot honour is a <c>400</c>; a tag that can never match (weak, or not a version) is a
/// <c>412</c>; a version tag on a table that keeps no version is a <c>400</c> — a precondition that cannot be evaluated
/// is refused, never ignored. Each door puts the answer on its own wire (REST envelope, OData error).
/// </summary>
internal static class Preconditions
{
    internal const string FailedCode = "PRECONDITION_FAILED";

    /// <summary>The status a failed precondition is answered with.</summary>
    internal const int FailedStatus = StatusCodes.Status412PreconditionFailed;

    /// <summary>The message of a precondition the row has moved past, from the write's own refusal.</summary>
    internal static PreconditionProblem Failed(string message) => new(FailedStatus, FailedCode, message);

    /// <summary>Reads the header; returns a problem to answer with instead of writing, or null with <paramref name="precondition"/> set.</summary>
    internal static PreconditionProblem? Read(HttpRequest request, TableMetadata table, out IfMatch precondition)
    {
        precondition = EntityTags.ParseIfMatch(request.Headers.IfMatch);
        return precondition.Kind switch
        {
            IfMatchKind.Unsupported => new PreconditionProblem(
                StatusCodes.Status400BadRequest, "INVALID_ARGUMENT", precondition.Problem ?? "Unsupported If-Match header."),
            IfMatchKind.NeverMatches => Failed(
                "The 'If-Match' entity tag does not match the row: a row's entity tag is a strong tag such as \"3\", and a weak or foreign tag never matches."),
            IfMatchKind.Version when !table.VersioningEnabled => new PreconditionProblem(
                StatusCodes.Status400BadRequest,
                "INVALID_ARGUMENT",
                $"Table '{table.LogicalName}' keeps no row version, so there is no entity tag for 'If-Match' to name."),
            _ => null,
        };
    }

    /// <summary>The write options a precondition asks for on top of <paramref name="ignoreUnknown"/>; null when it asks for none.</summary>
    internal static WriteOptions? OptionsFor(IfMatch precondition, bool ignoreUnknown)
    {
        if (precondition.Kind != IfMatchKind.Version && !ignoreUnknown)
        {
            return null;
        }

        var options = WriteOptions.Default;
        if (ignoreUnknown)
        {
            options = options with { AllowUnknownFields = true };
        }

        return precondition.Kind == IfMatchKind.Version ? options with { ExpectedVersion = precondition.Version } : options;
    }
}
