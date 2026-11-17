using CapitecAssessment.Services;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace CapitecAssessment.Tests.Services;

public class HealthServiceTests
{
    [Fact]
    public void GetHealth_ReturnsHealthyWithEnvironmentDetails()
    {
        var now = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var service = new HealthService(new FakeHostEnvironment("Testing", "TestApp"), new FixedTimeProvider(now));

        var result = service.GetHealth();

        Assert.Equal("Healthy", result.Status);
        Assert.Equal("TestApp", result.Service);
        Assert.Equal("Testing", result.Environment);
        Assert.Equal(now.UtcDateTime, result.TimestampUtc);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeHostEnvironment(string environmentName, string applicationName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = applicationName;
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
