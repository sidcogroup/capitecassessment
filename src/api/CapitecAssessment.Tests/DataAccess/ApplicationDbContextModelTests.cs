using CapitecAssessment.DataAccess;
using CapitecAssessment.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace CapitecAssessment.Tests.DataAccess;

/// <summary>
/// Offline checks that the EF model matches docs/database-schema.md. No database connection is made.
/// </summary>
public class ApplicationDbContextModelTests
{
    private static IModel BuildModel()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=capiassesdb")
            .Options;
        using var context = new ApplicationDbContext(options);
        return context.GetService<IDesignTimeModel>().Model;
    }

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
    public void Entity_MapsToTableInCadbSchema(Type entityType, string table)
    {
        var entity = BuildModel().FindEntityType(entityType);

        Assert.NotNull(entity);
        Assert.Equal(table, entity.GetTableName());
        Assert.Equal("cadb", entity.GetSchema());
    }

    [Fact]
    public void User_IdIsBigintGeneratedByDatabaseFunction()
    {
        var id = BuildModel().FindEntityType(typeof(User))!.FindProperty(nameof(User.Id))!;

        Assert.Equal(typeof(long), id.ClrType);
        Assert.Equal("cadb.generate_user_id()", id.GetDefaultValueSql());
    }

    [Fact]
    public void Account_MoneyAndCurrencyColumnTypesMatchSchema()
    {
        var entity = BuildModel().FindEntityType(typeof(Account))!;

        Assert.Equal("numeric(12,2)", entity.FindProperty(nameof(Account.Balance))!.GetColumnType());
        Assert.Equal("char(3)", entity.FindProperty(nameof(Account.Currency))!.GetColumnType());
        Assert.Equal("numeric(12,2)", BuildModel().FindEntityType(typeof(Transaction))!
            .FindProperty(nameof(Transaction.Amount))!.GetColumnType());
    }

    [Fact]
    public void Timestamps_AreTimestamptz()
    {
        var created = BuildModel().FindEntityType(typeof(User))!.FindProperty(nameof(User.CreatedAt))!;

        Assert.Equal("timestamptz", created.GetColumnType());
        Assert.Equal("now()", created.GetDefaultValueSql());
    }

    [Fact]
    public void Dispute_HasPartialUniqueIndexForOpenDisputes()
    {
        var index = BuildModel().FindEntityType(typeof(Dispute))!.GetIndexes()
            .Single(i => i.GetDatabaseName() == "ux_dispute_open_per_txn");

        Assert.True(index.IsUnique);
        Assert.Contains("Under Review", index.GetFilter());
    }

    [Fact]
    public void ColumnNamesAreSnakeCase()
    {
        var entity = BuildModel().FindEntityType(typeof(DisputeStatusHistory))!;
        var table = StoreObjectIdentifier.Table("dispute_status_history", "cadb");

        Assert.Equal("changed_by", entity.FindProperty(nameof(DisputeStatusHistory.ChangedBy))!.GetColumnName(table));
        Assert.Equal("dispute_id", entity.FindProperty(nameof(DisputeStatusHistory.DisputeId))!.GetColumnName(table));
    }

    [Fact]
    public void ForeignKeys_DoNotCascadeDelete()
    {
        var cascading = BuildModel().GetEntityTypes()
            .SelectMany(e => e.GetForeignKeys())
            .Where(fk => fk.DeleteBehavior == DeleteBehavior.Cascade);

        Assert.Empty(cascading);
    }
}
