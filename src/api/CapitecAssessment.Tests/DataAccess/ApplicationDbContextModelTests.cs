using CapitecAssessment.DataAccess;
using CapitecAssessment.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace CapitecAssessment.Tests.DataAccess;

/// <summary>
/// Checks that the C# classes are mapped to the database exactly as described in docs/database-schema.md
/// (table names, column names and types, indexes, delete rules).
///
/// These tests only inspect Entity Framework's mapping in memory. No database connection is made,
/// so they always run.
/// </summary>
public class ApplicationDbContextModelTests
{
    /// <summary>Builds Entity Framework's picture of the database from the C# code (no connection is opened).</summary>
    private static IModel BuildModel()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=capiassesdb")
            .Options;
        using var context = new ApplicationDbContext(options);
        return context.GetService<IDesignTimeModel>().Model;
    }

    /// <summary>Every class is stored in the expected table, inside the "cadb" schema.</summary>
    [Theory]
    [InlineData(typeof(User), "users")]
    [InlineData(typeof(UserProfile), "user_profiles")]
    [InlineData(typeof(UserContact), "user_contact")]
    [InlineData(typeof(OtpCode), "otp_codes")]
    [InlineData(typeof(Account), "accounts")]
    [InlineData(typeof(Transaction), "transactions")]
    [InlineData(typeof(Dispute), "disputes")]
    [InlineData(typeof(DisputeStatusHistory), "dispute_status_history")]
    [InlineData(typeof(Notification), "notifications")]
    public void Entity_MapsToTableInCadbSchema(Type entityType, string expectedTable)
    {
        var entity = BuildModel().FindEntityType(entityType);

        Assert.NotNull(entity);
        Assert.Equal(expectedTable, entity.GetTableName());
        Assert.Equal("cadb", entity.GetSchema());
    }

    /// <summary>User IDs are 64-bit numbers created by a database function, not by the C# code.</summary>
    [Fact]
    public void User_IdIsBigintGeneratedByDatabaseFunction()
    {
        var id = BuildModel().FindEntityType(typeof(User))!.FindProperty(nameof(User.Id))!;

        Assert.Equal(typeof(long), id.ClrType);
        Assert.Equal("cadb.generate_user_id()", id.GetDefaultValueSql());
    }

    /// <summary>
    /// Money is stored as an exact decimal with 2 decimal places (never a floating-point number, which can
    /// introduce rounding errors), and currency as a 3-letter code such as "ZAR".
    /// </summary>
    [Fact]
    public void Account_MoneyAndCurrencyColumnTypesMatchSchema()
    {
        var model = BuildModel();
        var account = model.FindEntityType(typeof(Account))!;
        var transaction = model.FindEntityType(typeof(Transaction))!;

        Assert.Equal("numeric(12,2)", account.FindProperty(nameof(Account.Balance))!.GetColumnType());
        Assert.Equal("char(3)", account.FindProperty(nameof(Account.Currency))!.GetColumnType());
        Assert.Equal("numeric(12,2)", transaction.FindProperty(nameof(Transaction.Amount))!.GetColumnType());
    }

    /// <summary>Dates are stored with their time zone ("timestamptz"), and the database fills in the creation time.</summary>
    [Fact]
    public void Timestamps_AreTimestamptz()
    {
        var created = BuildModel().FindEntityType(typeof(User))!.FindProperty(nameof(User.CreatedAt))!;

        Assert.Equal("timestamptz", created.GetColumnType());
        Assert.Equal("now()", created.GetDefaultValueSql());
    }

    /// <summary>
    /// A transaction can have at most one OPEN dispute at a time. This is enforced by a unique index
    /// that only covers disputes that are still open (e.g. "Under Review").
    /// </summary>
    [Fact]
    public void Dispute_HasPartialUniqueIndexForOpenDisputes()
    {
        var index = BuildModel().FindEntityType(typeof(Dispute))!.GetIndexes()
            .Single(i => i.GetDatabaseName() == "ux_dispute_open_per_txn");

        Assert.True(index.IsUnique);
        Assert.Contains("Under Review", index.GetFilter());
    }

    /// <summary>C# property names like ChangedBy become database column names like changed_by ("snake_case").</summary>
    [Fact]
    public void ColumnNamesAreSnakeCase()
    {
        var entity = BuildModel().FindEntityType(typeof(DisputeStatusHistory))!;
        var table = StoreObjectIdentifier.Table("dispute_status_history", "cadb");

        Assert.Equal("changed_by", entity.FindProperty(nameof(DisputeStatusHistory.ChangedBy))!.GetColumnName(table));
        Assert.Equal("dispute_id", entity.FindProperty(nameof(DisputeStatusHistory.DisputeId))!.GetColumnName(table));
    }

    /// <summary>
    /// Deleting a row never silently deletes related rows (e.g. deleting a user does not wipe their
    /// transactions). For banking records, losing data by accident is worse than a failed delete.
    /// </summary>
    [Fact]
    public void ForeignKeys_DoNotCascadeDelete()
    {
        var cascadingDeletes = BuildModel().GetEntityTypes()
            .SelectMany(e => e.GetForeignKeys())
            .Where(fk => fk.DeleteBehavior == DeleteBehavior.Cascade);

        Assert.Empty(cascadingDeletes);
    }
}
