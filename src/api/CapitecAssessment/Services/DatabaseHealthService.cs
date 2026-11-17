using CapitecAssessment.Abstractions.Services;
using CapitecAssessment.DataAccess;
using CapitecAssessment.Models.Response;
using Microsoft.EntityFrameworkCore;

namespace CapitecAssessment.Services;

public class DatabaseHealthService : IDatabaseHealthService
{
    private readonly ApplicationDbContext _db;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DatabaseHealthService> _logger;

    public DatabaseHealthService(ApplicationDbContext db, TimeProvider timeProvider, ILogger<DatabaseHealthService> logger)
    {
        _db = db;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<DatabaseHealthResponse> CheckAsync(CancellationToken cancellationToken)
    {
        var canConnect = false;
        var schemaPresent = false;

        try
        {
            canConnect = await _db.Database.CanConnectAsync(cancellationToken);

            if (canConnect)
            {
                // Touches the table so a reachable-but-empty database (schema not applied) is reported.
                // Cheap: LIMIT 1 on a primary-keyed table.
                await _db.Users.AsNoTracking().Select(u => u.Id).Take(1).ToListAsync(cancellationToken);
                schemaPresent = true;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Database health check failed");
        }

        return new DatabaseHealthResponse
        {
            Status = canConnect && schemaPresent ? "Healthy" : "Unhealthy",
            CanConnect = canConnect,
            SchemaPresent = schemaPresent,
            TimestampUtc = _timeProvider.GetUtcNow().UtcDateTime
        };
    }
}
