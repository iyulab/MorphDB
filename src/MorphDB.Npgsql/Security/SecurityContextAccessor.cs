using MorphDB.Core.Security;

namespace MorphDB.Npgsql.Security;

/// <summary>
/// AsyncLocal-based security context accessor.
/// </summary>
public sealed class SecurityContextAccessor : ISecurityContextAccessor
{
    private static readonly AsyncLocal<SecurityContextHolder> _contextHolder = new();

    public SecurityContext Context
    {
        get
        {
            var context = ContextOrNull;
            if (context == null)
            {
                throw new InvalidOperationException("Security context is not available");
            }
            return context;
        }
    }

    public SecurityContext? ContextOrNull => _contextHolder.Value?.Context;

    public void SetContext(SecurityContext context)
    {
        _contextHolder.Value = new SecurityContextHolder { Context = context };
    }

    private sealed class SecurityContextHolder
    {
        public SecurityContext? Context { get; set; }
    }
}
