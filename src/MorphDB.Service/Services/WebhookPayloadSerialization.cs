using System.Text.Json;
using MorphDB.Core.Models;

namespace MorphDB.Service.Services;

/// <summary>
/// The one owner of the wire shape of a <see cref="WebhookPayload"/>.
/// </summary>
/// <remarks>
/// <see cref="WebhookDeliveryService"/> serializes with these options, and the doc-server parity
/// gate reads the same instance to derive the field names <c>docs/API.md</c> has to name — so
/// neither half keeps its own copy of the naming policy. A receiver has no schema document for this
/// payload, so a naming change that the documentation does not follow is invisible until a live
/// delivery arrives; holding both halves against one instance is what makes that change fail a
/// build instead.
/// </remarks>
internal static class WebhookPayloadSerialization
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false
    };
}
