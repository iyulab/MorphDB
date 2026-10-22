using System.Net;
using System.Text;
using MorphDB.Client;

namespace MorphDB.Tests.Unit;

/// <summary>
/// Guards the SDK half of the 0.8.0 error contract (issue client-discards-server-error-envelope).
/// docs/API.md says "branch on <c>code</c>, not on message text" — so the server's
/// <c>{error, message, code}</c> envelope must survive into the exception the SDK consumer
/// actually sees: <c>Message</c> = the server's message, <c>ErrorCode</c> = the server's code.
/// Every client funnels through the shared <c>ErrorEnvelope</c> helper, so the representative
/// errors are exercised across several client surfaces to pin the convergence.
/// </summary>
public class ClientErrorEnvelopeTests
{
    private static MorphDBClient ClientReturning(HttpStatusCode status, string body, string contentType = "application/json")
        => new("http://localhost:9", new MorphDBClientOptions
        {
            HttpMessageHandler = new StubHandler(status, body, contentType),
        });

    [Fact]
    public async Task The_servers_message_and_code_reach_the_consumer()
    {
        // MISSING_PROJECT — the exact envelope the live 0.8.0 server answers with (issue repro).
        await using var client = ClientReturning(HttpStatusCode.BadRequest,
            """{"error":"BadRequest","message":"This request must say which project it applies to. Send an X-Project-Id header.","code":"MISSING_PROJECT","details":null}""");

        var act = () => client.Schema.CreateTableAsync(new() { Name = "probe", Columns = [] });

        var ex = (await act.Should().ThrowAsync<MorphDBValidationException>()).Which;
        ex.Message.Should().Contain("X-Project-Id", "the server said what to do; the SDK must not replace it with 'Validation failed'");
        ex.ErrorCode.Should().Be("MISSING_PROJECT");
        ex.ResponseBody.Should().Contain("MISSING_PROJECT", "the raw body stays available");
    }

    [Fact]
    public async Task A_validation_failure_carries_the_servers_code_through_the_data_surface()
    {
        await using var client = ClientReturning(HttpStatusCode.BadRequest,
            """{"error":"BadRequest","message":"Unknown column 'nmae' in filter.","code":"VALIDATION_ERROR","details":null}""");

        var act = () => client.Data.QueryAsync("items", new());

        var ex = (await act.Should().ThrowAsync<MorphDBValidationException>()).Which;
        ex.Message.Should().Contain("nmae");
        ex.ErrorCode.Should().Be("VALIDATION_ERROR");
    }

    /// <summary>
    /// The reasons by field the server sends under <c>details</c> reach <see cref="MorphDBValidationException.Errors"/>,
    /// which was always empty — a batch's refused row (<c>[2].email</c>) could be read only from the message.
    /// </summary>
    [Fact]
    public async Task A_validation_failure_carries_the_servers_reasons_by_field()
    {
        await using var client = ClientReturning(HttpStatusCode.BadRequest,
            """{"error":"ValidationError","message":"Validation error for '[2].email': a value is required","code":"VALIDATION_ERROR","details":{"[2].email":["a value is required"]}}""");

        var act = () => client.Batch.InsertManyAsync("items", [new Dictionary<string, object?>()]);

        var ex = (await act.Should().ThrowAsync<MorphDBValidationException>()).Which;
        ex.Errors.Should().ContainKey("[2].email").WhoseValue.Should().Equal("a value is required");
    }

    [Fact]
    public async Task A_conflict_carries_the_servers_code_through_the_schema_surface()
    {
        await using var client = ClientReturning(HttpStatusCode.Conflict,
            """{"error":"Conflict","message":"A table named 'items' already exists in this project.","code":"DUPLICATE_NAME","details":null}""");

        var act = () => client.Schema.CreateTableAsync(new() { Name = "items", Columns = [] });

        var ex = (await act.Should().ThrowAsync<MorphDBConflictException>()).Which;
        ex.Message.Should().Contain("already exists");
        ex.ErrorCode.Should().Be("DUPLICATE_NAME");
    }

    [Fact]
    public async Task The_transaction_surface_uses_the_same_funnel()
    {
        await using var client = ClientReturning(HttpStatusCode.BadRequest,
            """{"error":"BadRequest","message":"This request must say which project it applies to. Send an X-Project-Id header.","code":"MISSING_PROJECT","details":null}""");

        var act = () => client.Transactions.ExecuteAsync(new() { Operations = [] });

        var ex = (await act.Should().ThrowAsync<MorphDBValidationException>()).Which;
        ex.ErrorCode.Should().Be("MISSING_PROJECT");
    }

    private const string ProjectGone =
        """{"error":"NotFound","message":"Project not found.","code":"PROJECT_NOT_FOUND","details":null}""";

    private const string TableGone =
        """{"error":"NotFound","message":"Table 'items' not found.","code":"TABLE_NOT_FOUND","details":null}""";

    /// <summary>
    /// A nullable getter answers <c>null</c> for "no such table" — but a deleted project answers
    /// <c>404</c> too, and reading that as <c>null</c> told a caller the table was missing when the
    /// project was. A caller that then creates the table fails with an error that names neither.
    /// </summary>
    [Fact]
    public async Task A_missing_project_is_not_read_as_a_missing_table()
    {
        await using var client = ClientReturning(HttpStatusCode.NotFound, ProjectGone);

        var act = () => client.Schema.GetTableAsync("items", TestContext.Current.CancellationToken);

        var ex = (await act.Should().ThrowAsync<MorphDBNotFoundException>()).Which;
        ex.ErrorCode.Should().Be("PROJECT_NOT_FOUND");
    }

    [Fact]
    public async Task A_missing_table_still_reads_as_null()
    {
        await using var client = ClientReturning(HttpStatusCode.NotFound, TableGone);

        (await client.Schema.GetTableAsync("items", TestContext.Current.CancellationToken)).Should().BeNull();
    }

    [Fact]
    public async Task Every_nullable_getter_in_a_project_throws_for_a_missing_project()
    {
        await using var client = ClientReturning(HttpStatusCode.NotFound, ProjectGone);

        Func<Task>[] getters =
        [
            () => client.Data.GetByIdAsync("items", Guid.NewGuid(), TestContext.Current.CancellationToken),
            () => client.Views.GetAsync("v", TestContext.Current.CancellationToken),
            () => client.Webhooks.GetByIdAsync(Guid.NewGuid(), TestContext.Current.CancellationToken),
            () => client.Webhooks.GetDeadLetterAsync(Guid.NewGuid(), TestContext.Current.CancellationToken),
            () => client.Bulk.GetImportJobStatusAsync(Guid.NewGuid(), TestContext.Current.CancellationToken),
            () => client.Bulk.GetExportJobStatusAsync(Guid.NewGuid(), TestContext.Current.CancellationToken),
        ];

        foreach (var getter in getters)
        {
            (await getter.Should().ThrowAsync<MorphDBNotFoundException>()).Which.ErrorCode.Should().Be("PROJECT_NOT_FOUND");
        }
    }

    /// <summary>The project getter is the one place PROJECT_NOT_FOUND is the resource itself.</summary>
    [Fact]
    public async Task The_project_getter_reads_a_missing_project_as_null()
    {
        await using var client = ClientReturning(HttpStatusCode.NotFound, ProjectGone);

        (await client.Projects.GetAsync(Guid.NewGuid(), TestContext.Current.CancellationToken)).Should().BeNull();
    }

    [Fact]
    public async Task A_body_that_is_not_an_envelope_falls_back_to_the_legacy_surface()
    {
        // e.g. a proxy or gateway answering with HTML — the typed exception surface must not break.
        await using var client = ClientReturning(HttpStatusCode.BadRequest, "<html>Bad Request</html>", "text/html");

        var act = () => client.Data.QueryAsync("items", new());

        var ex = (await act.Should().ThrowAsync<MorphDBValidationException>()).Which;
        ex.Message.Should().Be("Validation failed");
        ex.ErrorCode.Should().Be("VALIDATION_ERROR", "the legacy default code still applies when the server said nothing machine-readable");
    }

    [Fact]
    public async Task A_not_found_envelope_reaches_the_consumer()
    {
        await using var client = ClientReturning(HttpStatusCode.NotFound,
            """{"error":"NotFound","message":"Table 'ghost' does not exist.","code":"TABLE_NOT_FOUND","details":null}""");

        var act = () => client.Schema.DropTableAsync("ghost");

        var ex = (await act.Should().ThrowAsync<MorphDBNotFoundException>()).Which;
        ex.Message.Should().Contain("ghost");
        ex.ErrorCode.Should().Be("TABLE_NOT_FOUND");
    }

    private sealed class StubHandler(HttpStatusCode status, string body, string contentType) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(status)
            {
                RequestMessage = request,
                Content = new StringContent(body, Encoding.UTF8, contentType),
            });
    }
}
