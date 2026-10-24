using System.Buffers;
using System.Text;
using System.Text.Json;
using MorphDB.Core.Security;

namespace MorphDB.Npgsql.Security;

/// <summary>
/// A security context as it is stored beside work that is requested now and carried out later: the
/// part of it <see cref="Infrastructure.MorphConnectionFactory"/> writes to a connection, and nothing
/// else. What is read back is therefore the caller PostgreSQL saw when the work was requested.
/// </summary>
/// <remarks>
/// Written and read by hand rather than through the serializer, for the reason the factory writes
/// the claims by hand: the shape is small and fixed, and a host published with trimming has no
/// reflection metadata to serialize from. It also keeps what is stored to the fields named here — a
/// context carries a <c>ClaimsPrincipal</c> as well, which is not something to put in a row.
/// </remarks>
internal static class SecurityContextSnapshot
{
    private const string ProjectId = "projectId";
    private const string UserId = "userId";
    private const string Role = "role";
    private const string Email = "email";
    private const string ActedBy = "actedBy";
    private const string IsAuthenticated = "isAuthenticated";
    private const string BypassRls = "bypassRls";
    private const string Claims = "claims";

    /// <summary>The context as JSON, or <c>null</c> when there is no context to store.</summary>
    public static string? Write(SecurityContext? context)
    {
        if (context is null)
        {
            return null;
        }

        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString(ProjectId, context.ProjectId);
            writer.WriteString(UserId, context.UserId);
            writer.WriteString(Role, context.Role);
            writer.WriteString(Email, context.Email);
            writer.WriteString(ActedBy, context.ActedBy);
            writer.WriteBoolean(IsAuthenticated, context.IsAuthenticated);
            writer.WriteBoolean(BypassRls, context.BypassRls);

            writer.WriteStartObject(Claims);
            foreach (var (name, value) in context.Claims)
            {
                writer.WriteString(name, value);
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>
    /// The context <paramref name="json"/> holds, or <c>null</c> when nothing was stored. A member
    /// that is absent reads as the caller not having it.
    /// </summary>
    /// <exception cref="JsonException">
    /// What is stored is not a context. It is not read as an empty one: the work it belongs to
    /// fails rather than run as somebody it was not requested by.
    /// </exception>
    public static SecurityContext? Read(string? json)
    {
        if (json is null)
        {
            return null;
        }

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("A stored security context must be a JSON object.");
        }

        try
        {
            var context = new SecurityContext
            {
                ProjectId = root.TryGetProperty(ProjectId, out var projectId) && projectId.ValueKind != JsonValueKind.Null
                    ? projectId.GetGuid()
                    : Guid.Empty,
                UserId = StringOf(root, UserId),
                Role = StringOf(root, Role),
                Email = StringOf(root, Email),
                ActedBy = StringOf(root, ActedBy),
                IsAuthenticated = BooleanOf(root, IsAuthenticated),
                BypassRls = BooleanOf(root, BypassRls),
            };

            if (root.TryGetProperty(Claims, out var claims) && claims.ValueKind != JsonValueKind.Null)
            {
                foreach (var claim in claims.EnumerateObject())
                {
                    context.Claims[claim.Name] = claim.Value.GetString() ?? string.Empty;
                }
            }

            return context;
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException)
        {
            // A member of the wrong kind: a number where a name belongs, text where a flag does.
            throw new JsonException("A stored security context has a member of the wrong type.", ex);
        }
    }

    private static string? StringOf(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) ? value.GetString() : null;

    // GetBoolean refuses anything that is not true or false, so "bypassRls": "yes" is an error and
    // not a caller that bypasses nothing — or everything.
    private static bool BooleanOf(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.GetBoolean();
}
