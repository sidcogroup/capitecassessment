using CapitecAssessment.Abstractions.Services;
using CapitecAssessment.Models.Response;

namespace CapitecAssessment.Services;

public class HealthService : IHealthService
{
    private readonly IHostEnvironment _environment;
    private readonly TimeProvider _timeProvider;

    public HealthService(IHostEnvironment environment, TimeProvider timeProvider)
    {
        _environment = environment;
        _timeProvider = timeProvider;
    }

    public HealthResponse GetHealth() => new()
    {
        Status = "Healthy",
        Service = _environment.ApplicationName,
        Environment = _environment.EnvironmentName,
        TimestampUtc = _timeProvider.GetUtcNow().UtcDateTime
    };
}
