using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Omnichannel.Api.Middleware;

/// <summary>Converts unhandled exceptions into RFC 7807 ProblemDetails without leaking internals.</summary>
public sealed partial class ProblemDetailsExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<ProblemDetailsExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        LogUnhandledException(logger, httpContext.TraceIdentifier, exception);

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "An unexpected error occurred.",
                Type = "https://tools.ietf.org/html/rfc7231#section-6.6.1",
            },
        });
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception. TraceId: {TraceId}")]
    private static partial void LogUnhandledException(ILogger logger, string traceId, Exception exception);
}
