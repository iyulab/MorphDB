using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.RegularExpressions;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration.Api;

/// <summary>
/// The execution half of docs parity, for the HTTP surface. The other gates compare descriptions —
/// the names in <c>docs/API.md</c> against the names in the code — and a reader who copies an
/// example out of the reference is not comparing names, they are making a call.
/// <para>
/// The two are not the same check, and the gap between them has shipped: the realtime section's
/// first example was refused by the hub binder for two months while the name-level gate stayed
/// green, because every method, event and field name in it was correct and the arity was not. A
/// description-level gate cannot see that, by construction. This is the same axis for HTTP —
/// every request the reference teaches is sent to a running server, and what is asserted is that
/// the server recognizes it.
/// </para>
/// <para>
/// A reference example is written for a reader, so it elides: a path says <c>{columnId}</c> and an
/// id in a body says <c>…</c>, both meant to be replaced before sending. Those are filled in here
/// with type-valid values (<see cref="Elisions"/>), which is what makes the assertion the right one
/// — the claim is not that the example is runnable verbatim, it is that <em>once the reader fills
/// in what they were told to fill in</em>, the shape they were taught is one this API recognizes.
/// An elision left in a path segment typed as a GUID does not even reach a route, so without this
/// step the gate would report the document's own shorthand as an unrouted address.
/// </para>
/// <para>
/// Deliberately narrow, and worth stating what it does <em>not</em> claim: an example is not
/// asserted to succeed. It names tables and rows this database does not have, so a
/// <c>404 TABLE_NOT_FOUND</c> or a validation refusal is a correct answer and passes. What fails is
/// the request never reaching a handler as written — an address this API does not route, or a body
/// the binder rejects as unrecognized. Those two are exactly the failures a reader cannot work
/// around, and neither depends on the state of the database, which is why this gate needs no
/// fixture data.
/// </para>
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class DocsExampleExecutionTests
{
    private readonly HttpClient _client;

    public DocsExampleExecutionTests(ApiIntegrationFixture fixture)
    {
        _client = fixture.Api.Client;
    }

    public static TheoryData<string, string, string?> DocumentedRequests()
    {
        var data = new TheoryData<string, string, string?>();
        foreach (var (method, path, body) in DocumentedExamples.Parse(DocsFiles.ReadApiReference()))
        {
            data.Add(method, Elisions.InPath(path), body is null ? null : Elisions.InBody(body));
        }

        return data;
    }

    [Fact]
    public void The_reference_still_carries_executable_examples()
    {
        // A parser that silently matches nothing turns this whole class into a gate that passes by
        // finding no work. The count is a floor, not a pin: examples may be added freely.
        DocumentedExamples.Parse(DocsFiles.ReadApiReference()).Count.Should().BeGreaterThanOrEqualTo(10,
            "the HTTP reference documents more than a handful of requests, and a parse that stops "
            + "finding them is a broken gate rather than a shrinking document");
    }

    [Theory]
    [MemberData(nameof(DocumentedRequests))]
    public async Task A_documented_request_is_routed_and_binds_as_written(string method, string path, string? body)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);
        var payload = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // An unrouted URL is answered by the framework, not this API: an empty 404 with no envelope
        // (UnroutedRequestContractTests pins that shape). A routed miss always carries a code.
        (response.StatusCode == HttpStatusCode.NotFound && payload.Length == 0).Should().BeFalse(
            $"`{method} {path}` is taught by docs/API.md and a reader who sends it reaches no "
            + "endpoint at all — the address is not part of this API");

        if (response.StatusCode == HttpStatusCode.BadRequest && body is not null)
        {
            var error = await response.Content.ReadFromJsonAsync<ErrorPayload>(TestContext.Current.CancellationToken);
            (error?.Code == "INVALID_ARGUMENT" && error.Message?.Contains("Supported members") == true)
                .Should().BeFalse(
                    $"the body docs/API.md shows for `{method} {path}` is refused by the binder as "
                    + $"unrecognized, so copying the example cannot work: {error?.Message}");
        }
    }

    private sealed record ErrorPayload(string? Code, string? Message);
}

/// <summary>
/// Reads the request examples out of the HTTP reference. Kept beside the gate that runs them
/// because its only contract is what that gate asserts on.
/// </summary>
internal static class DocumentedExamples
{
    private static readonly Regex HttpBlock = new(@"```http\n(?<block>.*?)```", RegexOptions.Singleline);
    private static readonly Regex RequestLine = new(@"^(?<method>GET|POST|PUT|PATCH|DELETE) (?<path>/\S*)");

    public static IReadOnlyList<(string Method, string Path, string? Body)> Parse(string markdown)
    {
        var examples = new List<(string, string, string?)>();

        foreach (System.Text.RegularExpressions.Match block in HttpBlock.Matches(markdown))
        {
            var lines = block.Groups["block"].Value.Split('\n');
            (string Method, string Path)? pending = null;
            var body = new StringBuilder();

            foreach (var raw in lines)
            {
                var line = raw.TrimEnd('\r');
                var request = RequestLine.Match(line);

                if (request.Success)
                {
                    Flush(examples, ref pending, body);
                    pending = (request.Groups["method"].Value, StripComment(request.Groups["path"].Value));
                    continue;
                }

                // Headers and the reference's own `#` annotations are not part of a body.
                if (pending is not null && (body.Length > 0 || line.StartsWith('{') || line.StartsWith('[')))
                {
                    body.AppendLine(line);
                }
            }

            Flush(examples, ref pending, body);
        }

        return examples;
    }

    private static void Flush(
        List<(string, string, string?)> examples,
        ref (string Method, string Path)? pending,
        StringBuilder body)
    {
        if (pending is null)
        {
            return;
        }

        var text = body.ToString().Trim();
        examples.Add((pending.Value.Method, pending.Value.Path, text.Length == 0 ? null : text));
        body.Clear();
        pending = null;
    }

    private static string StripComment(string path)
    {
        var comment = path.IndexOf('#');
        return comment < 0 ? path : path[..comment].TrimEnd();
    }
}

/// <summary>
/// Fills in the placeholders a reference example expects its reader to replace. Every substitution
/// is type-valid rather than merely non-empty, because the failures this feeds a gate on — an
/// unrouted address, a body the binder does not recognize — are only meaningful once the request is
/// as well-formed as the reader would have made it.
/// </summary>
internal static class Elisions
{
    // Stable rather than random: a failure message that names the request should be the same
    // message on the next run, or the first thing anyone does with it is re-run to see if it moves.
    private const string Id = "1f3d3f8a-0000-4000-8000-00000000abcd";

    private static readonly Regex PathPlaceholder = new(@"\{(?<name>\w+)\}");

    public static string InPath(string path) => PathPlaceholder.Replace(path, match =>
        match.Groups["name"].Value.EndsWith("id", StringComparison.OrdinalIgnoreCase) ? Id : "docs_example");

    public static string InBody(string body) => body
        .Replace("\"…\"", $"\"{Id}\"", StringComparison.Ordinal)
        .Replace("<project id>", Id, StringComparison.Ordinal);
}
