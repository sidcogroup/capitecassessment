using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CapitecAssessment.Models;

/// <summary>Maps to cadb.accounts.</summary>
[Table("accounts")]
public class Account
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("user_id")]
    public long UserId { get; set; }

    [Column("account_number")]
    [Required, StringLength(20)]
    public string AccountNumber { get; set; } = string.Empty;

    /// <summary>See <see cref="AccountTypes"/>.</summary>
    [Column("account_type")]
    [Required, StringLength(20)]
    public string AccountType { get; set; } = string.Empty;

    [Column("name")]
    [Required, StringLength(50)]
    public string Name { get; set; } = string.Empty;

    [Column("currency", TypeName = "char(3)")]
    [Required, StringLength(3)]
    public string Currency { get; set; } = "ZAR";

    [Column("balance", TypeName = "numeric(12,2)")]
    public decimal Balance { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    public User? User { get; set; }
    public ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();
}
