using System.Diagnostics;

namespace CapitecAssessment.Middlewares;

public class RequestLoggingMiddleware : IMiddleware
{
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    public RequestLoggingMiddleware(ILogger<RequestLoggingMiddleware> logger)
    {
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var stopwatch = Stopwatch.StartNew();

        _logger.LogInformation("HTTP {RequestMethod} {RequestPath} started",
            context.Request.Method, context.Request.Path);

        try
        {
            await next(context);

            _logger.LogInformation("HTTP {RequestMethod} {RequestPath} completed with status {StatusCode} in {ElapsedMilliseconds}ms",
                context.Request.Method, context.Request.Path, context.Response.StatusCode, stopwatch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // Client aborted; not a failure. Let ExceptionHandlingMiddleware set the 499.
            throw;
        }
        catch (Exception)
        {
            // Log and rethrow - ExceptionHandlingMiddleware turns it into a response.
            _logger.LogWarning("HTTP {RequestMethod} {RequestPath} failed after {ElapsedMilliseconds}ms",
                context.Request.Method, context.Request.Path, stopwatch.ElapsedMilliseconds);
            throw;
        }
    }
}
