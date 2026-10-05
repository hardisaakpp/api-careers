using Careers.Application;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Careers.Api;

/// <summary>Maps the expected failures to HTTP, with a stable <c>code</c> the web client translates.</summary>
public sealed class CareersExceptionHandler(IProblemDetailsService problems, ILogger<CareersExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception exception, CancellationToken ct)
    {
        var (status, title) = exception switch
        {
            NotFoundException => (StatusCodes.Status404NotFound, "Not found"),
            ConflictException => (StatusCodes.Status409Conflict, "Conflict"),
            ValidationFailedException => (StatusCodes.Status422UnprocessableEntity, "Validation failed"),
            ImportFailedException { TooLarge: true } => (StatusCodes.Status413PayloadTooLarge, "The file is too large"),
            ImportFailedException => (StatusCodes.Status422UnprocessableEntity, "The file could not be imported"),
            AiUnavailableException => (StatusCodes.Status503ServiceUnavailable, "The AI service is unavailable"),
            UnauthorizedAccessException => (StatusCodes.Status401Unauthorized, "Unauthorized"),
            BadHttpRequestException => (StatusCodes.Status400BadRequest, "Bad request"),
            _ => (0, string.Empty),
        };
        if (status == 0)
        {
            return false;
        }

        if (exception is AiUnavailableException)
        {
            logger.LogWarning(exception, "AI service unavailable");
        }

        var detail = new ProblemDetails { Status = status, Title = title, Detail = exception is CareersException ? exception.Message : null };
        if (exception is CareersException known)
        {
            detail.Extensions["code"] = known.Code;
        }

        if (exception is ValidationFailedException invalid)
        {
            detail.Extensions["errors"] = invalid.Errors;
        }

        http.Response.StatusCode = status;
        return await problems.TryWriteAsync(new ProblemDetailsContext { HttpContext = http, ProblemDetails = detail, Exception = exception });
    }
}
