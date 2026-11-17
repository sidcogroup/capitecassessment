using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CapitecAssessment.Models;

/// <summary>Maps to cadb.transactions. Amount is always positive; <see cref="Type"/> gives the direction.</summary>
[Table("transactions")]
public class Transaction
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("account_id")]
    public Guid AccountId { get; set; }

    [Column("transaction_date")]
    public DateTime TransactionDate { get; set; }

    [Column("description")]
    [Required, StringLength(200)]
    public string Description { get; set; } = string.Empty;

    [Column("merchant")]
    [StringLength(100)]
    public string? Merchant { get; set; }

    [Column("amount", TypeName = "numeric(12,2)")]
    public decimal Amount { get; set; }

    /// <summary>See <see cref="TransactionTypes"/>.</summary>
    [Column("type")]
    [Required, StringLength(10)]
    public string Type { get; set; } = string.Empty;

    [Column("reference")]
    [StringLength(50)]
    public string? Reference { get; set; }

    public Account? Account { get; set; }
    public ICollection<Dispute> Disputes { get; set; } = new List<Dispute>();
}
