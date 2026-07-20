using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using MorphDB.Service.Models.Api;

namespace MorphDB.Service.Filters;

/// <summary>
/// Exception filter that handles project-related exceptions and returns appropriate HTTP responses.
/// </summary>
public sealed class ProjectExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is UnauthorizedAccessException ex &&
            ex.Message.Contains("API key"))
        {
            context.Result = new BadRequestObjectResult(new ErrorResponse
            {
                Error = "InvalidProject",
                Message = "Valid project ID or API key is required"
            });
            context.ExceptionHandled = true;
        }
    }
}
