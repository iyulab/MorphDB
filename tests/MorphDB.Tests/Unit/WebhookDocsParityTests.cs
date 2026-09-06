using System.Text.Json;
using System.Text.RegularExpressions;
using MorphDB.Core.Models;
using MorphDB.Service.Models.Api;
using MorphDB.Service.Services;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Unit;

/// <summary>
/// The doc-server parity gate for webhooks — the fifth advertised protocol, and until now the only
/// one with no gate at all.
/// <para>
/// It is the surface that needs one most for the same reason the real-time hub did: a receiver has
/// no schema document to read, in any environment. It is worse here, because the receiver is not
/// even a client of this service — it is a third-party endpoint that finds out what a delivery
/// looks like when one arrives, and nothing about a wrong guess fails loudly. Every webhook defect
/// this repository has recorded was found that way, after the fact, by standing up a receiver by
/// hand.
/// </para>
/// <para>
/// So both halves are derived, never transcribed: the request fields come from the model the
/// endpoint binds, the event vocabulary from the enum the delivery path switches on, and the
/// payload fields from serializing a real payload through the very options the delivery service
/// uses. A field added, renamed, or re-cased on any of the three fails this gate until
/// <c>docs/API.md</c> says so.
/// </para>
/// </summary>
public partial class WebhookDocsParityTests
{
    [Fact]
    public void The_documented_registration_body_names_the_fields_the_endpoint_binds()
    {
        var documented = KeysOf(RegistrationExample());

        var bound = typeof(CreateWebhookApiRequest).GetProperties()
            .Select(p => CamelCase(p.Name))
            .ToHashSet(StringComparer.Ordinal);

        documented.Should().BeEquivalentTo(bound,
            "the example is the only registration reference a caller has — a field it names that "
            + "the endpoint does not bind is silently dropped, and one the endpoint binds that the "
            + "example omits is a capability nobody can discover");
    }

    /// <summary>
    /// The documentation states outright that the signing secret is server-generated and "not a
    /// request field". That sentence is a promise about the binding model, so it is held against
    /// the binding model — the fact above would already fail if a `secret` appeared on one side
    /// only, but this says why, and keeps the claim from quietly becoming false.
    /// </summary>
    [Fact]
    public void The_signing_secret_is_not_a_field_the_registration_binds()
    {
        // Whitespace-collapsed: this half reads a sentence rather than a table, and a sentence
        // wraps wherever the paragraph happens to end a line.
        Prose(WebhookSection()).Should().Contain("not a request field",
            "the documentation has to keep stating where the secret comes from");

        typeof(CreateWebhookApiRequest).GetProperties()
            .Select(p => p.Name)
            .Should().NotContain("Secret");
    }

    [Fact]
    public void The_documented_events_are_the_ones_the_server_delivers_on()
    {
        var section = WebhookSection();

        foreach (var served in Enum.GetNames<WebhookEvent>().Select(n => n.ToLowerInvariant()))
        {
            Regex.IsMatch(section, $"[`\"]{served}[`\"]").Should().BeTrue(
                $"a subscriber can only ask for an event by naming it, and '{served}' is one the "
                + "server delivers on — an event the documentation never names as a value is one "
                + "no reader of this section knows they can subscribe to");
        }

        foreach (var documented in EventToken().Matches(RegistrationExample())
                     .Select(m => m.Groups["event"].Value))
        {
            Enum.GetNames<WebhookEvent>().Select(n => n.ToLowerInvariant())
                .Should().Contain(documented,
                    "an event value the example teaches must be one the server accepts");
        }
    }

    [Fact]
    public void The_documented_payload_names_the_fields_a_delivery_actually_carries()
    {
        var documented = KeysOf(PayloadExample());

        // Serialized rather than reflected: the wire names come from a naming policy, and the point
        // of the gate is that the policy is checked too, not just the property list.
        var sent = KeysOf(JsonSerializer.Serialize(
            new WebhookPayload
            {
                Event = "insert",
                Table = "orders",
                RecordId = Guid.Empty,
                Timestamp = DateTimeOffset.UnixEpoch,
                Data = new Dictionary<string, object?>(),
            },
            WebhookPayloadSerialization.Options));

        documented.Should().BeEquivalentTo(sent,
            "the receiver is a third-party endpoint with no schema to read — a field it is told to "
            + "expect and never gets, or gets and was never told about, is only ever discovered by "
            + "inspecting a live delivery");
    }

    private static HashSet<string> KeysOf(string json)
    {
        using var document = JsonDocument.Parse(json);

        return document.RootElement.EnumerateObject()
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static string CamelCase(string name) => char.ToLowerInvariant(name[0]) + name[1..];

    /// <summary>Markdown prose with its line wrapping collapsed, so a phrase can be matched.</summary>
    private static string Prose(string markdown) => Whitespace().Replace(markdown, " ");

    /// <summary>The `## Webhook` section, up to the next section of the same level.</summary>
    private static string WebhookSection()
    {
        var section = SectionRegex().Match(ConstraintBoundaryDoc.ReadRepoFile("docs/API.md"));

        section.Success.Should().BeTrue("API.md must carry a '## Webhook' section");

        return section.Groups["body"].Value;
    }

    /// <summary>The request body of the registration example, taken out of its `http` block.</summary>
    private static string RegistrationExample()
    {
        var example = RegistrationBody().Match(WebhookSection());

        example.Success.Should().BeTrue(
            "the Webhook section must show a POST /api/webhooks body — it is the registration "
            + "reference the gate holds the binding model against");

        return example.Groups["json"].Value;
    }

    private static string PayloadExample()
    {
        var example = PayloadBody().Match(WebhookSection());

        example.Success.Should().BeTrue("the Webhook section must show the delivered payload");

        return example.Groups["json"].Value;
    }

    [GeneratedRegex(@"^##\s+Webhook\s*$(?<body>.*?)(?=^##\s|\z)",
        RegexOptions.Singleline | RegexOptions.Multiline)]
    private static partial Regex SectionRegex();

    [GeneratedRegex(@"POST\s+/api/webhooks.*?(?<json>\{.*?\n\})\s*```", RegexOptions.Singleline)]
    private static partial Regex RegistrationBody();

    /// <summary>
    /// The first fenced JSON object after the words that introduce the delivery. Anchored on the
    /// phrase rather than on the punctuation that follows it, so re-wording the paragraph around
    /// the example cannot quietly disarm the gate.
    /// </summary>
    [GeneratedRegex(@"Webhook payload.*?```json\s*(?<json>\{.*?\n\})\s*```", RegexOptions.Singleline)]
    private static partial Regex PayloadBody();

    [GeneratedRegex(@"""(?<event>insert|update|delete)""")]
    private static partial Regex EventToken();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
