using System.Text.Json;
using CapitecAssessment.Middlewares;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace CapitecAssessment.Tests.Middlewares;

public class ExceptionHandlingMiddlewareTests
{
    [Theory]
    [InlineData(typeof(UnauthorizedAccessException), 401, "denied")]
    [InlineData(typeof(InvalidOperationException), 400, "bad input")]
    public async Task InvokeAsync_MapsKnownExceptions(Type exceptionType, int expectedStatus, string message)
    {
        var (status, body) = await RunAsync((Exception)Activator.CreateInstance(exceptionType, message)!);

        Assert.Equal(expectedStatus, status);
        Assert.Equal(message, body.GetProperty("message").GetString());
        Assert.Equal(expectedStatus, body.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task InvokeAsync_UnknownException_ReturnsGeneric500WithoutLeakingDetails()
    {
        var (status, body) = await RunAsync(new Exception("secret internals"));

        Assert.Equal(500, status);
        Assert.DoesNotContain("secret", body.GetProperty("message").GetString());
    }

    [Fact]
    public async Task InvokeAsync_NoException_PassesThrough()
    {
        var middleware = new ExceptionHandlingMiddleware(NullLogger<ExceptionHandlingMiddleware>.Instance);
        var context = new DefaultHttpContext();

        await middleware.InvokeAsync(context, _ =>
        {
            context.Response.StatusCode = 204;
            return Task.CompletedTask;
        });

        Assert.Equal(204, context.Response.StatusCode);
    }

    private static async Task<(int Status, JsonElement Body)> RunAsync(Exception exception)
    {
        var middleware = new ExceptionHandlingMiddleware(NullLogger<ExceptionHandlingMiddleware>.Instance);
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context, _ => throw exception);

        context.Response.Body.Position = 0;
        var body = await JsonSerializer.DeserializeAsync<JsonElement>(context.Response.Body);
        return (context.Response.StatusCode, body);
    }
}
