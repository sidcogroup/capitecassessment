using System.Net;
using System.Net.Http.Json;
using CapitecAssessment.Models.Response;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CapitecAssessment.Tests.Controllers;

/// <summary>
/// HTTP tests for <c>GET /api/v1/health</c>, the "is the API running?" check used by monitoring.
/// It needs no sign-in and no database, so the API is started with no fakes at all.
/// </summary>
public class HealthApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public HealthApiTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    /// <summary>The health endpoint answers 200 OK with status "Healthy".</summary>
    [Fact]
    public async Task GetHealth_ReturnsOkWithHealthyStatus()
    {
        var response = await _client.GetAsync("/api/v1/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<HealthResponse>();
        Assert.NotNull(body);
        Assert.Equal("Healthy", body.Status);
    }

    /// <summary>A URL the API does not have returns 404 Not Found (not a 500 error or an empty 200).</summary>
    [Fact]
    public async Task UnknownRoute_ReturnsNotFound()
    {
        var response = await _client.GetAsync("/api/v1/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
