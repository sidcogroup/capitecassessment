using CapitecAssessment.DataAccess;
using CapitecAssessment.Services;
using CapitecAssessment.Tests.DataAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace CapitecAssessment.Tests.Services;

/// <summary>
/// Tests for <see cref="DatabaseHealthService"/>, behind <c>/api/v1/health/database</c>.
/// It reports whether the API can reach its PostgreSQL database.
/// </summary>
public class DatabaseHealthServiceTests
{
    /// <summary>
    /// When the database cannot be reached, the check returns an "Unhealthy" report.
    /// It must not throw an error, because the health page itself must keep working.
    /// </summary>
    [Fact]
    public async Task CheckAsync_DatabaseUnreachable_ReportsUnhealthyInsteadOfThrowing()
    {
        // Nothing listens on port 1, so the connection is refused straight away.
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=capiassesdb;Username=x;Password=x;Timeout=2")
            .Options;
        await using var db = new ApplicationDbContext(options);
        var service = new DatabaseHealthService(db, TimeProvider.System, NullLogger<DatabaseHealthService>.Instance);

        var result = await service.CheckAsync(CancellationToken.None);

        Assert.Equal("Unhealthy", result.Status);
        Assert.False(result.CanConnect);
        Assert.False(result.SchemaPresent);
    }

    /// <summary>
    /// Against a real database, the check reports "Healthy".
    /// Skipped unless the CAPITEC_TEST_CONNECTION environment variable is set (see <see cref="PostgresDbTests"/>).
    /// </summary>
    [PostgresFact]
    public async Task CheckAsync_DatabaseReachable_ReportsHealthy()
    {
        await using var db = PostgresTestDb.NewContext();
        var service = new DatabaseHealthService(db, TimeProvider.System, NullLogger<DatabaseHealthService>.Instance);

        var result = await service.CheckAsync(CancellationToken.None);

        Assert.Equal("Healthy", result.Status);
    }
}
