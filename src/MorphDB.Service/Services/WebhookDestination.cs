namespace MorphDB.Service.Services;

/// <summary>
/// Where a webhook delivery may be sent (<c>Webhooks__AllowedHosts__&lt;n&gt;</c>).
/// </summary>
public sealed class WebhookDestinationOptions
{
    /// <summary>The configuration section these options bind to.</summary>
    public const string SectionName = "Webhooks";

    /// <summary>
    /// The hosts a webhook may deliver to: a host name (<c>hooks.example.com</c>) or every host under a domain
    /// (<c>*.example.com</c>, which does not include <c>example.com</c> itself). Empty — the default — allows any host.
    /// </summary>
    public List<string> AllowedHosts { get; set; } = [];
}

/// <summary>
/// The rules for a webhook's destination, applied when a webhook is registered or changed and again when a delivery is
/// sent: an address that was allowed at registration is not owed a delivery after the allow-list has been tightened.
/// <para>
/// A webhook makes this service send a signed request, carrying row data, to an address its registrant chose from the
/// service's own place on the network. The rules are therefore about the address, not about who the registrant is
/// (that is <c>RequireMasterSecret</c>'s): an absolute <c>http</c> or <c>https</c> URL with no credentials embedded in
/// it, and — when the deployment has named its hosts — one of those hosts.
/// </para>
/// </summary>
public static class WebhookDestination
{
    /// <summary>Whether <paramref name="url"/> may be a destination; when it may not, <paramref name="reason"/> says why.</summary>
    public static bool TryValidate(string? url, WebhookDestinationOptions options, out string reason)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host))
        {
            reason = "The url must be an absolute http or https URL.";
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            reason = $"The url scheme '{uri.Scheme}' is not allowed; use http or https.";
            return false;
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            reason = "The url must not carry credentials; send them in the webhook's headers.";
            return false;
        }

        if (options.AllowedHosts.Count > 0 && !options.AllowedHosts.Any(allowed => Matches(allowed, uri.Host)))
        {
            reason = $"The host '{uri.Host}' is not one of the hosts this service delivers webhooks to (Webhooks__AllowedHosts).";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private static bool Matches(string allowed, string host)
    {
        if (string.IsNullOrWhiteSpace(allowed))
        {
            return false;
        }

        allowed = allowed.Trim();
        return allowed.StartsWith("*.", StringComparison.Ordinal)
            ? host.EndsWith(allowed[1..], StringComparison.OrdinalIgnoreCase) && host.Length > allowed.Length - 1
            : string.Equals(allowed, host, StringComparison.OrdinalIgnoreCase);
    }
}
