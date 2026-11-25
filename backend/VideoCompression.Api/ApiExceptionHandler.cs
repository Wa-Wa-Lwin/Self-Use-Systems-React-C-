using Microsoft.AspNetCore.Diagnostics;
using VideoCompression.Api.Ffmpeg;

namespace VideoCompression.Api;

/// <summary>Turns unhandled exceptions into ProblemDetails JSON the frontend can show.</summary>
public sealed class ApiExceptionHandler(IProblemDetailsService problemDetails, ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        // The client went away (e.g. an estimate was superseded); there's nobody to answer.
        if (context.RequestAborted.IsCancellationRequested) return true;

        var (status, title, detail) = exception switch
        {
            FfmpegUnavailableException e => (StatusCodes.Status503ServiceUnavailable, "FFmpeg is not available", e.Message),
            BadHttpRequestException e => (e.StatusCode, "Bad request", e.Message),
            _ => (StatusCodes.Status500InternalServerError, "Something went wrong", exception.Message),
        };

        if (status >= 500 && exception is not FfmpegUnavailableException)
            logger.LogError(exception, "Unhandled error for {Method} {Path}", context.Request.Method, context.Request.Path);

        context.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            Exception = exception,
            ProblemDetails = { Status = status, Title = title, Detail = detail },
        });
    }
}
