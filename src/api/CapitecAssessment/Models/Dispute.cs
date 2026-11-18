using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CapitecAssessment.Models;

/// <summary>
/// Maps to cadb.disputes. A transaction can have only one open (Submitted / Under Review) dispute,
/// enforced by the ux_dispute_open_per_txn partial unique index.
/// </summary>
[Table("disputes")]
public class Dispute
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    /// <summary>Customer-facing reference, e.g. DISP0B91CD64.</summary>
    [Column("dispute_reference")]
    [StringLength(50)]
    public string DisputeReference { get; set; } = string.Empty;

    [Column("transaction_id")]
    public Guid TransactionId { get; set; }

    [Column("reason")]
    [Required, StringLength(100)]
    public string Reason { get; set; } = string.Empty;

    [Column("details")]
    [StringLength(1000)]
    public string? Details { get; set; }

    /// <summary>See <see cref="DisputeStatuses"/>.</summary>
    [Column("status")]
    [Required, StringLength(20)]
    public string Status { get; set; } = DisputeStatuses.Submitted;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }

    public Transaction? Transaction { get; set; }
    public ICollection<DisputeStatusHistory> StatusHistory { get; set; } = new List<DisputeStatusHistory>();
}
