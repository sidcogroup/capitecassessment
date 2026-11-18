using System.Text.Json;
using CapitecAssessment.Middlewares;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace CapitecAssessment.Tests.Middlewares;

/// <summary>
/// Tests for the safety net that catches any error thrown while handling a request and turns it
/// into a tidy JSON response, instead of crashing or showing a stack trace to the caller.
///
/// Each test feeds the middleware a fake "next step" that throws a chosen exception,
/// then reads back the status code and JSON body the middleware wrote.
/// </summary>
public class ExceptionHandlingMiddlewareTests
{
    /// <summary>
    /// Known error types map to a specific status code, and their message is safe to show:
    ///   UnauthorizedAccessException -> 401
    ///   InvalidOperationException   -> 400
    /// </summary>
    [Theory]
    [InlineData(typeof(UnauthorizedAccessException), 401, "denied")]
    [InlineData(typeof(InvalidOperationException), 400, "bad input")]
    public async Task InvokeAsync_MapsKnownExceptions(Type exceptionType, int expectedStatus, string message)
    {
        var exception = (Exception)Activator.CreateInstance(exceptionType, message)!;

        var (status, body) = await RunMiddlewareThatThrows(exception);

        Assert.Equal(expectedStatus, status);
        Assert.Equal(message, body.GetProperty("message").GetString());
        Assert.Equal(expectedStatus, body.GetProperty("status").GetInt32());
    }

    /// <summary>
    /// Any other error becomes a generic 500. Its message is hidden, because it might reveal
    /// internal details (database names, file paths, etc.) to an attacker.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_UnknownException_ReturnsGeneric500WithoutLeakingDetails()
    {
        var (status, body) = await RunMiddlewareThatThrows(new Exception("secret internals"));

        Assert.Equal(500, status);
        Assert.DoesNotContain("secret", body.GetProperty("message").GetString());
    }

    /// <summary>When nothing goes wrong, the middleware does not interfere with the response.</summary>
    [Fact]
    public async Task InvokeAsync_NoException_PassesThrough()
    {
        var middleware = new ExceptionHandlingMiddleware(NullLogger<ExceptionHandlingMiddleware>.Instance);
        var context = new DefaultHttpContext();

        await middleware.InvokeAsync(context, _ =>
        {
            context.Response.StatusCode = 204; // the "next step" succeeds and sets its own status
            return Task.CompletedTask;
        });

        Assert.Equal(204, context.Response.StatusCode);
    }

    /// <summary>Runs the middleware with a next step that throws <paramref name="exception"/>, and returns what it wrote.</summary>
    private static async Task<(int Status, JsonElement Body)> RunMiddlewareThatThrows(Exception exception)
    {
        var middleware = new ExceptionHandlingMiddleware(NullLogger<ExceptionHandlingMiddleware>.Instance);
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream(); // capture the response so it can be read back

        await middleware.InvokeAsync(context, _ => throw exception);

        context.Response.Body.Position = 0;
        var body = await JsonSerializer.DeserializeAsync<JsonElement>(context.Response.Body);
        return (context.Response.StatusCode, body);
    }
}
