using CapitecAssessment.DataAccess;
using CapitecAssessment.Services;
using CapitecAssessment.Tests.DataAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace CapitecAssessment.Tests.Services;

public class DatabaseHealthServiceTests
{
    [Fact]
    public async Task CheckAsync_DatabaseUnreachable_ReportsUnhealthyInsteadOfThrowing()
    {
        // Nothing listens on port 1, so the connection is refused quickly.
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

    [PostgresFact]
    public async Task CheckAsync_DatabaseReachable_ReportsHealthy()
    {
        await using var db = PostgresTestDb.NewContext();
        var service = new DatabaseHealthService(db, TimeProvider.System, NullLogger<DatabaseHealthService>.Instance);

        var result = await service.CheckAsync(CancellationToken.None);

        Assert.Equal("Healthy", result.Status);
    }
}
