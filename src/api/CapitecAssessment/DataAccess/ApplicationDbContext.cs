using CapitecAssessment.Models;
using Microsoft.EntityFrameworkCore;

namespace CapitecAssessment.DataAccess;

/// <summary>
/// EF Core context over the existing PostgreSQL schema (<see cref="SchemaName"/>).
/// The schema is owned by docs/database-schema.md, not by EF migrations: the model here is
/// mapped to match it, and nothing in the API creates or alters tables.
/// </summary>
public class ApplicationDbContext : DbContext
{
    public const string SchemaName = "cadb";

    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();
    public DbSet<UserContact> UserContacts => Set<UserContact>();
    public DbSet<OtpCode> OtpCodes => Set<OtpCode>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<Dispute> Disputes => Set<Dispute>();
    public DbSet<DisputeStatusHistory> DisputeStatusHistory => Set<DisputeStatusHistory>();
    public DbSet<Notification> Notifications => Set<Notification>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Every timestamp column in the schema is TIMESTAMPTZ. Npgsql only accepts UTC DateTime values for it.
        configurationBuilder.Properties<DateTime>().HaveColumnType("timestamptz");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasDefaultSchema(SchemaName);

        // The schema declares no ON DELETE actions (NO ACTION), so don't let EF assume cascades.
        foreach (var foreignKey in modelBuilder.Model.GetEntityTypes().SelectMany(e => e.GetForeignKeys()))
        {
            foreignKey.DeleteBehavior = DeleteBehavior.Restrict;
        }

        // ===== users =====
        modelBuilder.Entity<User>(entity =>
        {
            // BIGINT default produced by cadb.generate_user_id(); leave Id unset on insert.
            entity.Property(u => u.Id).HasDefaultValueSql($"{SchemaName}.generate_user_id()");
            entity.Property(u => u.CreatedAt).HasDefaultValueSql("now()");
            entity.Property(u => u.Role).HasDefaultValue(UserRoles.Customer);
            entity.HasIndex(u => u.Email).IsUnique();
            entity.HasIndex(u => u.Username).IsUnique();

            entity.HasOne(u => u.Profile).WithOne(p => p.User).HasForeignKey<UserProfile>(p => p.UserId);
            entity.HasOne(u => u.Contact).WithOne(c => c.User).HasForeignKey<UserContact>(c => c.UserId);
        });

        modelBuilder.Entity<UserProfile>(entity =>
        {
            entity.Property(p => p.CreatedAt).HasDefaultValueSql("now()");
            entity.Property(p => p.UpdatedAt).HasDefaultValueSql("now()");
        });

        modelBuilder.Entity<UserContact>(entity =>
        {
            entity.Property(c => c.CreatedAt).HasDefaultValueSql("now()");
            entity.Property(c => c.UpdatedAt).HasDefaultValueSql("now()");
        });

        // ===== otp_codes =====
        modelBuilder.Entity<OtpCode>(entity =>
        {
            entity.Property(o => o.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(o => o.CreatedAt).HasDefaultValueSql("now()");
            entity.Property(o => o.Attempts).HasDefaultValue((short)0);
            entity.HasIndex(o => new { o.UserId, o.CreatedAt }, "ix_otp_user")
                .IsDescending(false, true);

            entity.HasOne(o => o.User).WithMany(u => u.OtpCodes).HasForeignKey(o => o.UserId);
        });

        // ===== accounts =====
        modelBuilder.Entity<Account>(entity =>
        {
            entity.Property(a => a.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(a => a.CreatedAt).HasDefaultValueSql("now()");
            entity.Property(a => a.Currency).HasDefaultValue("ZAR");
            entity.Property(a => a.Balance).HasDefaultValue(0m);
            entity.HasIndex(a => a.AccountNumber).IsUnique();
            entity.HasIndex(a => a.UserId, "ix_accounts_user");

            entity.HasOne(a => a.User).WithMany(u => u.Accounts).HasForeignKey(a => a.UserId);
        });

        // ===== transactions =====
        modelBuilder.Entity<Transaction>(entity =>
        {
            entity.Property(t => t.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.HasIndex(t => new { t.AccountId, t.TransactionDate }, "ix_txn_account_date")
                .IsDescending(false, true);

            entity.HasOne(t => t.Account).WithMany(a => a.Transactions).HasForeignKey(t => t.AccountId);
        });

        // ===== disputes =====
        modelBuilder.Entity<Dispute>(entity =>
        {
            entity.Property(d => d.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(d => d.CreatedAt).HasDefaultValueSql("now()");
            entity.Property(d => d.UpdatedAt).HasDefaultValueSql("now()");
            entity.Property(d => d.Status).HasDefaultValue(DisputeStatuses.Submitted);

            // One open dispute per transaction.
            entity.HasIndex(d => d.TransactionId, "ux_dispute_open_per_txn")
                .IsUnique()
                .HasFilter("status IN ('Submitted','Under Review')");

            entity.HasOne(d => d.Transaction).WithMany(t => t.Disputes).HasForeignKey(d => d.TransactionId);
        });

        // ===== dispute_status_history =====
        modelBuilder.Entity<DisputeStatusHistory>(entity =>
        {
            entity.Property(h => h.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(h => h.ChangedAt).HasDefaultValueSql("now()");
            entity.HasIndex(h => new { h.DisputeId, h.ChangedAt }, "ix_history_dispute");

            entity.HasOne(h => h.Dispute).WithMany(d => d.StatusHistory).HasForeignKey(h => h.DisputeId);
            entity.HasOne(h => h.ChangedByUser).WithMany().HasForeignKey(h => h.ChangedBy);
        });

        // ===== notifications =====
        modelBuilder.Entity<Notification>(entity =>
        {
            entity.Property(n => n.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(n => n.CreatedAt).HasDefaultValueSql("now()");
            entity.Property(n => n.Channel).HasDefaultValue("Email");

            entity.HasOne(n => n.User).WithMany(u => u.Notifications).HasForeignKey(n => n.UserId);
            entity.HasOne(n => n.Dispute).WithMany().HasForeignKey(n => n.DisputeId);
        });
    }
}
