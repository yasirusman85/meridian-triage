using Microsoft.AspNetCore.Diagnostics;

namespace Meridian.Web.Api;

/// <summary>Turns malformed request bodies (e.g. invalid JSON) into 400 problem details instead of 500.</summary>
internal sealed class BadRequestExceptionHandler : IExceptionHandler
{
    private readonly IProblemDetailsService _problemDetails;

    public BadRequestExceptionHandler(IProblemDetailsService problemDetails)
    {
        _problemDetails = problemDetails;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not BadHttpRequestException badRequest)
        {
            return false;
        }

        httpContext.Response.StatusCode = badRequest.StatusCode;
        return await _problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails =
            {
                Status = badRequest.StatusCode,
                Title = "The request body could not be read.",
            },
        });
    }
}
