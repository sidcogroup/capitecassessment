using CapitecAssessment.DataAccess;
using CapitecAssessment.Models;
using Microsoft.EntityFrameworkCore;

namespace CapitecAssessment.Tests.DataAccess;

/// <summary>
/// Use <c>[PostgresFact]</c> instead of <c>[Fact]</c> for a test that needs a real PostgreSQL database.
/// The test is automatically skipped (not failed) when no database is configured, e.g. in CI.
/// </summary>
public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrEmpty(PostgresTestDb.ConnectionString))
        {
            Skip = $"{PostgresTestDb.EnvironmentVariable} is not set";
        }
    }
}

/// <summary>Where the database tests get their connection from.</summary>
public static class PostgresTestDb
{
    public const string EnvironmentVariable = "CAPITEC_TEST_CONNECTION";

    public static string? ConnectionString => Environment.GetEnvironmentVariable(EnvironmentVariable);

    public static ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(ConnectionString).Options);
}

/// <summary>
/// Checks the code and the real database schema agree.
///
/// HOW TO RUN: point the tests at a THROWAWAY PostgreSQL database that has docs/database-schema.md applied:
///   set CAPITEC_TEST_CONNECTION=Host=localhost;Database=capiassesdb_test;Username=postgres;Password=...
/// When the variable is not set, these tests are skipped.
/// </summary>
public class PostgresDbTests
{
    /// <summary>
    /// Reads one row from every table. If any table or column the code expects is missing or misnamed,
    /// PostgreSQL returns an error and the test fails.
    /// </summary>
    [PostgresFact]
    public async Task EveryTable_CanBeQueried()
    {
        await using var db = PostgresTestDb.NewContext();

        await db.Users.AsNoTracking().Take(1).ToListAsync();
        await db.UserProfiles.AsNoTracking().Take(1).ToListAsync();
        await db.UserContacts.AsNoTracking().Take(1).ToListAsync();
        await db.OtpCodes.AsNoTracking().Take(1).ToListAsync();
        await db.Accounts.AsNoTracking().Take(1).ToListAsync();
        await db.Transactions.AsNoTracking().Take(1).ToListAsync();
        await db.Disputes.AsNoTracking().Take(1).ToListAsync();
        await db.DisputeStatusHistory.AsNoTracking().Take(1).ToListAsync();
        await db.Notifications.AsNoTracking().Take(1).ToListAsync();
    }

    /// <summary>
    /// When a user is saved, the database fills in the ID (a 10-digit number), the default role (Customer)
    /// and the creation time.
    /// </summary>
    [PostgresFact]
    public async Task InsertUser_UsesDatabaseGeneratedIdAndTimestamp()
    {
        await using var db = PostgresTestDb.NewContext();
        await using var tx = await db.Database.BeginTransactionAsync();

        var suffix = Guid.NewGuid().ToString("N")[..10];
        var user = new User { Email = $"{suffix}@example.test", Username = $"u{suffix}" };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        Assert.InRange(user.Id, 1_000_000_000L, 9_999_999_999L);
        Assert.Equal(UserRoles.Customer, user.Role);
        Assert.True(user.CreatedAt > DateTime.UtcNow.AddMinutes(-5));

        await tx.RollbackAsync(); // leave no data behind
    }
}
