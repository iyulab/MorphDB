using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using MorphDB.Service.Models.Api;

namespace MorphDB.Service.Filters;

/// <summary>
/// Exception filter that handles tenant-related exceptions and returns appropriate HTTP responses.
/// </summary>
public sealed class TenantExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is UnauthorizedAccessException ex &&
            ex.Message.Contains("API key"))
        {
            context.Result = new BadRequestObjectResult(new ErrorResponse
            {
                Error = "InvalidTenant",
                Message = "Valid tenant ID or API key is required"
            });
            context.ExceptionHandled = true;
        }
    }
}
