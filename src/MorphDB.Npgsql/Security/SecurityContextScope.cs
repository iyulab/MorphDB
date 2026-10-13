using MorphDB.Core.Security;

namespace MorphDB.Npgsql.Security;

/// <summary>
/// A stretch of work done as a caller other than the ambient one: a context is made ambient when
/// the scope is opened, and what was ambient before is put back when it is disposed.
/// </summary>
/// <remarks>
/// The ambient context flows with the asynchronous call, so one set inside an awaited method does
/// not reach that method's caller. The scope does not lean on that. It puts the earlier context
/// back itself, in the method that opened it — which is also what keeps the rest of that method,
/// and the next piece of work it does, from running as the caller of the last one.
/// </remarks>
internal readonly struct SecurityContextScope : IDisposable
{
    private readonly ISecurityContextAccessor _accessor;
    private readonly SecurityContext? _previous;

    private SecurityContextScope(ISecurityContextAccessor accessor, SecurityContext? previous)
    {
        _accessor = accessor;
        _previous = previous;
    }

    /// <summary>
    /// Makes <paramref name="context"/> the ambient context until the scope is disposed —
    /// <c>null</c> for no caller at all, whoever is ambient now. Open and dispose it in the same
    /// method.
    /// </summary>
    public static SecurityContextScope Begin(ISecurityContextAccessor accessor, SecurityContext? context)
    {
        var previous = accessor.ContextOrNull;
        accessor.SetContext(context);
        return new SecurityContextScope(accessor, previous);
    }

    public void Dispose() => _accessor.SetContext(_previous);
}
