using System.Security.Claims;

namespace MorphDB.Service.Services;

/// <summary>
/// Provides access to the current tenant context.
/// </summary>
public interface ITenantContextAccessor
{
    /// <summary>
    /// Gets the current tenant ID from the authenticated user or HTTP header.
    /// </summary>
    Guid TenantId { get; }

    /// <summary>
    /// Gets the current tenant ID or null if not available.
    /// </summary>
    Guid? TenantIdOrNull { get; }
}

/// <summary>
/// HTTP context-based tenant context accessor.
/// Resolves tenant ID from: 1) Authenticated user claims (API Key), 2) X-Tenant-Id header.
/// </summary>
public sealed class HttpTenantContextAccessor : ITenantContextAccessor
{
    private const string TenantIdHeader = "X-Tenant-Id";
    private const string TenantIdClaimType = "tenant_id";
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpTenantContextAccessor(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid TenantId
    {
        get
        {
            var tenantId = TenantIdOrNull;
            if (!tenantId.HasValue)
            {
                throw new InvalidOperationException("Tenant ID is required. Provide a valid API key or X-Tenant-Id header.");
            }
            return tenantId.Value;
        }
    }

    public Guid? TenantIdOrNull
    {
        get
        {
            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext is null)
                return null;

            // 1. First, try to get tenant ID from authenticated user claims (set by API key authentication)
            var user = httpContext.User;
            if (user.Identity?.IsAuthenticated == true)
            {
                var tenantClaim = user.FindFirst(TenantIdClaimType);
                if (tenantClaim != null && Guid.TryParse(tenantClaim.Value, out var claimTenantId) && claimTenantId != Guid.Empty)
                {
                    return claimTenantId;
                }
            }

            // 2. Fallback to X-Tenant-Id header (for backwards compatibility)
            if (httpContext.Request.Headers.TryGetValue(TenantIdHeader, out var tenantIdHeader) &&
                Guid.TryParse(tenantIdHeader.FirstOrDefault(), out var headerTenantId) &&
                headerTenantId != Guid.Empty)
            {
                return headerTenantId;
            }

            return null;
        }
    }
}
