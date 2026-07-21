using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using MorphDB.Core.Exceptions;
using MorphDB.Service.Controllers;
using MorphDB.Service.Models.Api;

namespace MorphDB.Tests.Unit;

/// <summary>
/// The terminal catch of the batch-family actions used to be
/// <c>catch (Exception ex) { return BadRequest(ex.Message); }</c> — every defect became a 400 a
/// caller could not tell from their own bad request, with internal exception text on the wire.
/// These tests pin the mapping that replaced it: what the service layer legitimately throws keeps
/// its documented status, and everything else is a 500 whose body says nothing about the exception.
/// </summary>
public class UnhandledErrorsTests
{
    private sealed class ProbeController : ControllerBase;

    private static readonly ProbeController Controller = new();

    private static ErrorResponse BodyOf(IActionResult result) =>
        (ErrorResponse)((ObjectResult)result).Value!;

    [Fact]
    public void ValidationException_IsA400_WithItsOwnCodeAndMessage()
    {
        var result = UnhandledErrors.Map(Controller, NullLogger.Instance,
            new ValidationException("age", "must be positive"), "op");

        ((ObjectResult)result).StatusCode.Should().Be(400);
        BodyOf(result).Code.Should().Be("VALIDATION_ERROR");
        BodyOf(result).Message.Should().Contain("age");
    }

    [Fact]
    public void NotFoundException_IsA404()
    {
        var result = UnhandledErrors.Map(Controller, NullLogger.Instance,
            new NotFoundException("Table", "orders"), "op");

        ((ObjectResult)result).StatusCode.Should().Be(404);
        BodyOf(result).Code.Should().Be("NOT_FOUND");
    }

    [Fact]
    public void KeyNotFoundException_IsA404()
    {
        var result = UnhandledErrors.Map(Controller, NullLogger.Instance,
            new KeyNotFoundException("Table 'orders' not found"), "op");

        ((ObjectResult)result).StatusCode.Should().Be(404);
        BodyOf(result).Code.Should().Be("TABLE_NOT_FOUND");
    }

    [Fact]
    public void ArgumentException_IsA400()
    {
        var result = UnhandledErrors.Map(Controller, NullLogger.Instance,
            new ArgumentException("bad filter"), "op");

        ((ObjectResult)result).StatusCode.Should().Be(400);
        BodyOf(result).Code.Should().Be("INVALID_ARGUMENT");
    }

    /// <summary>
    /// The point of the whole class: a defect must not answer 400, and its text must not reach the
    /// caller — driver messages carry physical names the hidden-layer principle exists to hide.
    /// </summary>
    [Fact]
    public void AnythingElse_IsA500_AndItsMessageStaysOffTheWire()
    {
        var result = UnhandledErrors.Map(Controller, NullLogger.Instance,
            new InvalidOperationException("relation \"p_a1b2._t_orders\" does not exist"), "op");

        ((ObjectResult)result).StatusCode.Should().Be(500);
        BodyOf(result).Code.Should().Be("INTERNAL_ERROR");
        BodyOf(result).Message.Should().NotContain("_t_orders",
            "internal exception text must not be echoed to the caller");
    }

    /// <summary>
    /// A canceled request is nobody's error — mapping it to any status invents an answer for a
    /// caller who already hung up. It must propagate for the host to observe.
    /// </summary>
    [Fact]
    public void OperationCanceled_Rethrows()
    {
        var act = () => UnhandledErrors.Map(Controller, NullLogger.Instance,
            new OperationCanceledException(), "op");

        act.Should().Throw<OperationCanceledException>();
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
