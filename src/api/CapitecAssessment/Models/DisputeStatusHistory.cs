using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CapitecAssessment.Models;

/// <summary>Maps to cadb.dispute_status_history (audit trail of status changes).</summary>
[Table("dispute_status_history")]
public class DisputeStatusHistory
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("dispute_id")]
    public Guid DisputeId { get; set; }

    [Column("status")]
    [Required, StringLength(20)]
    public string Status { get; set; } = string.Empty;

    [Column("note")]
    [StringLength(500)]
    public string? Note { get; set; }

    /// <summary>User id of whoever made the change.</summary>
    [Column("changed_by")]
    public long ChangedBy { get; set; }

    [Column("changed_at")]
    public DateTime ChangedAt { get; set; }

    public Dispute? Dispute { get; set; }
    public User? ChangedByUser { get; set; }
}
