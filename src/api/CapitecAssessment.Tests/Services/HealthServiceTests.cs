using CapitecAssessment.Services;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace CapitecAssessment.Tests.Services;

/// <summary>Tests for <see cref="HealthService"/>, which builds the body of the <c>/api/v1/health</c> response.</summary>
public class HealthServiceTests
{
    /// <summary>The health report says "Healthy" and includes the app's name, environment and the current time.</summary>
    [Fact]
    public void GetHealth_ReturnsHealthyWithEnvironmentDetails()
    {
        // A fixed clock, so we know exactly what time the report should show.
        var now = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var service = new HealthService(new FakeHostEnvironment("Testing", "TestApp"), new FixedTimeProvider(now));

        var result = service.GetHealth();

        Assert.Equal("Healthy", result.Status);
        Assert.Equal("TestApp", result.Service);
        Assert.Equal("Testing", result.Environment);
        Assert.Equal(now.UtcDateTime, result.TimestampUtc);
    }

    /// <summary>A clock that always returns the same time.</summary>
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    /// <summary>Pretends the app is running with the given environment and application name.</summary>
    private sealed class FakeHostEnvironment(string environmentName, string applicationName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = applicationName;
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
