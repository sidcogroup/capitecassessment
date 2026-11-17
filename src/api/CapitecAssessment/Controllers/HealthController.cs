using Asp.Versioning;
using CapitecAssessment.Abstractions.Services;
using CapitecAssessment.Models.Response;
using Microsoft.AspNetCore.Mvc;

namespace CapitecAssessment.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/health")]
public class HealthController : ControllerBase
{
    private readonly IHealthService _healthService;
    private readonly IDatabaseHealthService _databaseHealthService;

    public HealthController(IHealthService healthService, IDatabaseHealthService databaseHealthService)
    {
        _healthService = healthService;
        _databaseHealthService = databaseHealthService;
    }

    /// <summary>Liveness: the API process is up. Does not touch the database.</summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<HealthResponse> GetHealth()
    {
        return Ok(_healthService.GetHealth());
    }

    /// <summary>Readiness: the API can reach the database and the schema is applied.</summary>
    [HttpGet("database")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<DatabaseHealthResponse>> GetDatabaseHealth(CancellationToken cancellationToken)
    {
        var result = await _databaseHealthService.CheckAsync(cancellationToken);

        return result.Status == "Healthy"
            ? Ok(result)
            : StatusCode(StatusCodes.Status503ServiceUnavailable, result);
    }
}
