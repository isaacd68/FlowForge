using System.Diagnostics;
using System.Net;
using System.Text.Json;

namespace FlowForge.Api.Middleware;

/// <summary>
/// Middleware for handling exceptions globally.
/// </summary>
public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var (statusCode, errorCode, message) = exception switch
        {
            ArgumentException ex => (HttpStatusCode.BadRequest, "INVALID_ARGUMENT", ex.Message),
            KeyNotFoundException ex => (HttpStatusCode.NotFound, "NOT_FOUND", ex.Message),
            InvalidOperationException ex => (HttpStatusCode.Conflict, "INVALID_OPERATION", ex.Message),
            UnauthorizedAccessException ex => (HttpStatusCode.Unauthorized, "UNAUTHORIZED", ex.Message),
            OperationCanceledException => (HttpStatusCode.RequestTimeout, "TIMEOUT", "Operation was cancelled"),
            _ => (HttpStatusCode.InternalServerError, "INTERNAL_ERROR", "An unexpected error occurred")
        };

        _logger.LogError(exception, "Request failed: {ErrorCode} - {Message}", errorCode, message);

        context.Response.StatusCode = (int)statusCode;
        context.Response.ContentType = "application/json";

        var response = new ErrorResponse
        {
            Error = errorCode,
            Message = message,
            TraceId = Activity.Current?.Id ?? context.TraceIdentifier
        };

        await context.Response.WriteAsJsonAsync(response);
    }
}

/// <summary>
/// Middleware for request logging.
/// </summary>
public class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        var requestId = Guid.NewGuid().ToString("N")[..8];

        // Log request
        _logger.LogInformation(
            "[{RequestId}] {Method} {Path} started",
            requestId,
            context.Request.Method,
            context.Request.Path);

        try
        {
            await _next(context);
        }
        finally
        {
            stopwatch.Stop();

            var logLevel = context.Response.StatusCode >= 500 ? LogLevel.Error
                : context.Response.StatusCode >= 400 ? LogLevel.Warning
                : LogLevel.Information;

            _logger.Log(
                logLevel,
                "[{RequestId}] {Method} {Path} completed with {StatusCode} in {Duration}ms",
                requestId,
                context.Request.Method,
                context.Request.Path,
                context.Response.StatusCode,
                stopwatch.ElapsedMilliseconds);
        }
    }
}

/// <summary>
/// Standard error response format.
/// </summary>
public class ErrorResponse
{
    public required string Error { get; set; }
    public required string Message { get; set; }
    public string? TraceId { get; set; }
    public Dictionary<string, string[]>? ValidationErrors { get; set; }
}
