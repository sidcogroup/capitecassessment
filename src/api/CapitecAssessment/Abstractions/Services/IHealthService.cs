using CapitecAssessment.Models.Response;

namespace CapitecAssessment.Abstractions.Services;

public interface IHealthService
{
    HealthResponse GetHealth();
}
