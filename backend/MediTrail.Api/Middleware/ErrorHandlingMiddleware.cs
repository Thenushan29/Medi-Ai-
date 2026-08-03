using System.Net;
using System.Text.Json;
using MediTrail.Api.Services;

namespace MediTrail.Api.Middleware;

/// <summary>Consistent error envelope across every endpoint (§13 conventions).</summary>
public sealed record ApiError
{
    public required string Code { get; init; }

    /// <summary>Written for the person reading the screen, not for a stack trace (§15 usability).</summary>
    public required string Message { get; init; }

    public string? TraceId { get; init; }
}

public sealed class ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> logger)
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            var (status, code, message) = ex switch
            {
                NotFoundException => (HttpStatusCode.NotFound, "not_found", ex.Message),
                StorageException => (HttpStatusCode.BadGateway, "storage_unavailable",
                    "Document storage is not reachable right now. Please try again in a moment."),
                BadHttpRequestException => (HttpStatusCode.BadRequest, "bad_request", ex.Message),
                OperationCanceledException => (HttpStatusCode.RequestTimeout, "cancelled",
                    "The request was cancelled."),
                // Never leak an internal exception message to the client.
                _ => (HttpStatusCode.InternalServerError, "internal_error",
                    "Something went wrong on our side. Please try again.")
            };

            if (status == HttpStatusCode.InternalServerError)
            {
                logger.LogError(ex, "Unhandled exception on {Method} {Path}", context.Request.Method, context.Request.Path);
            }
            else
            {
                logger.LogWarning("{Code} on {Method} {Path}: {Message}", code, context.Request.Method, context.Request.Path, ex.Message);
            }

            if (context.Response.HasStarted)
            {
                // Too late to rewrite the response; the exception is already logged.
                throw;
            }

            context.Response.Clear();
            context.Response.StatusCode = (int)status;
            context.Response.ContentType = "application/json";

            await context.Response.WriteAsync(JsonSerializer.Serialize(new ApiError
            {
                Code = code,
                Message = message,
                TraceId = context.TraceIdentifier
            }, JsonOptions));
        }
    }
}
