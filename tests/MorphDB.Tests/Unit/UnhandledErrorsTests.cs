using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using MorphDB.Core.Exceptions;
using MorphDB.Service.Controllers;
using MorphDB.Service.ErrorHandling;
using MorphDB.Service.Models.Api;

namespace MorphDB.Tests.Unit;

/// <summary>
/// The terminal catch of the batch-family actions used to be
/// <c>catch (Exception ex) { return BadRequest(ex.Message); }</c> — every defect became a 400 a
/// caller could not tell from their own bad request, with internal exception text on the wire.
/// These tests pin the mapping that replaced it, now living in the global
/// <see cref="GlobalExceptionHandler"/>: what the service layer legitimately throws keeps its
/// documented status, and everything else is a 500 whose body says nothing about the exception.
/// </summary>
public class GlobalExceptionHandlerTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static async Task<(int Status, ErrorResponse? Body, bool Handled)> RunAsync(Exception ex)
    {
        var handler = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance);
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();

        var handled = await handler.TryHandleAsync(ctx, ex, CancellationToken.None);

        ctx.Response.Body.Position = 0;
        ErrorResponse? body = null;
        if (handled && ctx.Response.Body.Length > 0)
        {
            body = await JsonSerializer.DeserializeAsync<ErrorResponse>(ctx.Response.Body, Json);
        }

        return (ctx.Response.StatusCode, body, handled);
    }

    [Fact]
    public async Task ValidationException_IsA400_WithItsOwnCodeAndMessage()
    {
        var (status, body, _) = await RunAsync(new ValidationException("age", "must be positive"));

        status.Should().Be(400);
        body!.Code.Should().Be("VALIDATION_ERROR");
        body.Message.Should().Contain("age");
    }

    [Fact]
    public async Task MissingProjectException_IsA400_AndDoesNotAdvertiseAnApiKey()
    {
        var (status, body, _) = await RunAsync(new MissingProjectException());

        status.Should().Be(400);
        body!.Code.Should().Be("MISSING_PROJECT");
        body.Message.Should().Contain("X-Project-Id");
        body.Message.Should().NotContain("API key",
            "the server never asks for an API key — advertising one sends callers hunting for a credential that does not exist");
    }

    [Fact]
    public async Task NotFoundException_IsA404()
    {
        var (status, body, _) = await RunAsync(new NotFoundException("Table", "orders"));

        status.Should().Be(404);
        body!.Code.Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task TableNotFoundException_IsA404()
    {
        var (status, body, _) = await RunAsync(new TableNotFoundException("orders"));

        status.Should().Be(404);
        body!.Code.Should().Be("TABLE_NOT_FOUND");
    }

    [Fact]
    public async Task ColumnNotFoundException_IsA400_BecauseAColumnIsNotARouteResource()
    {
        var (status, body, _) = await RunAsync(new ColumnNotFoundException("orders", "nosuch"));

        status.Should().Be(400);
        body!.Code.Should().Be("COLUMN_NOT_FOUND");
    }

    [Fact]
    public async Task KeyNotFoundException_IsA404()
    {
        var (status, body, _) = await RunAsync(new KeyNotFoundException("Table 'orders' not found"));

        status.Should().Be(404);
        body!.Code.Should().Be("TABLE_NOT_FOUND");
    }

    [Fact]
    public async Task DuplicateNameException_IsA409()
    {
        var (status, body, _) = await RunAsync(new DuplicateNameException("Table", "orders"));

        status.Should().Be(409);
        body!.Code.Should().Be("DUPLICATE_NAME");
    }

    [Fact]
    public async Task SchemaVersionConflict_IsA409()
    {
        var (status, body, _) = await RunAsync(new SchemaVersionConflictException(2, 3));

        status.Should().Be(409);
        body!.Code.Should().Be("SCHEMA_VERSION_CONFLICT");
    }

    [Fact]
    public async Task ArgumentException_IsA400()
    {
        var (status, body, _) = await RunAsync(new ArgumentException("bad filter"));

        status.Should().Be(400);
        body!.Code.Should().Be("INVALID_ARGUMENT");
    }

    /// <summary>
    /// The point of the whole class: a defect must not answer 400, and its text must not reach the
    /// caller — driver messages carry physical names the hidden-layer principle exists to hide.
    /// It must also never be an <em>empty</em> 500: the body always carries the INTERNAL_ERROR
    /// envelope, so a caller has a code to branch on.
    /// </summary>
    [Fact]
    public async Task AnythingElse_IsA500_WithAnEnvelope_AndItsMessageStaysOffTheWire()
    {
        var (status, body, handled) = await RunAsync(
            new InvalidOperationException("relation \"p_a1b2._t_orders\" does not exist"));

        handled.Should().BeTrue("no exception may fall through to the framework's empty-body 500");
        status.Should().Be(500);
        body!.Code.Should().Be("INTERNAL_ERROR");
        body.Message.Should().NotContain("_t_orders",
            "internal exception text must not be echoed to the caller");
    }

    /// <summary>
    /// A canceled request is nobody's error — mapping it to any status invents an answer for a
    /// caller who already hung up. The handler declines it so the host observes the cancellation.
    /// </summary>
    [Fact]
    public async Task OperationCanceled_IsDeclined()
    {
        var (_, body, handled) = await RunAsync(new OperationCanceledException());

        handled.Should().BeFalse();
        body.Should().BeNull();
    }

    [Fact]
    public void ItemMessage_KeepsExpectedFailureText_AndReplacesUnexpectedText()
    {
        UnhandledErrors.ItemMessage(NullLogger.Instance, new ValidationException("x", "y"), "op")
            .Should().Contain("x");

        UnhandledErrors.ItemMessage(NullLogger.Instance,
                new InvalidOperationException("pk column missing on _t_orders"), "op")
            .Should().Be("An unexpected error occurred");
    }
}
