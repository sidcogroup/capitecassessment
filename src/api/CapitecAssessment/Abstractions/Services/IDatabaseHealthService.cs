using CapitecAssessment.Models.Response;

namespace CapitecAssessment.Abstractions.Services;

public interface IDatabaseHealthService
{
    /// <summary>Checks the API can reach the database and that the expected schema is present.</summary>
    Task<DatabaseHealthResponse> CheckAsync(CancellationToken cancellationToken);
}
