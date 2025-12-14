namespace MorphDB.Service.Services;

/// <summary>
/// Provides access to the current tenant context.
/// </summary>
public interface ITenantContextAccessor
{
    /// <summary>
    /// Gets the current tenant ID from the HTTP context.
    /// </summary>
    Guid TenantId { get; }

    /// <summary>
    /// Gets the current tenant ID or null if not available.
    /// </summary>
    Guid? TenantIdOrNull { get; }
}

/// <summary>
/// HTTP context-based tenant context accessor.
/// </summary>
public sealed class HttpTenantContextAccessor : ITenantContextAccessor
{
    private const string TenantIdHeader = "X-Tenant-Id";
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
                throw new InvalidOperationException($"{TenantIdHeader} header is required");
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

            if (httpContext.Request.Headers.TryGetValue(TenantIdHeader, out var tenantIdHeader) &&
                Guid.TryParse(tenantIdHeader.FirstOrDefault(), out var tenantId) &&
                tenantId != Guid.Empty)
            {
                return tenantId;
            }

            return null;
        }
    }
}
