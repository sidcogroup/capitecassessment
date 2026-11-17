using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CapitecAssessment.Models;

/// <summary>Maps to cadb.notifications (log of messages sent to users).</summary>
[Table("notifications")]
public class Notification
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("user_id")]
    public long UserId { get; set; }

    /// <summary>See <see cref="NotificationTypes"/>.</summary>
    [Column("type")]
    [Required, StringLength(30)]
    public string Type { get; set; } = string.Empty;

    [Column("channel")]
    [Required, StringLength(20)]
    public string Channel { get; set; } = "Email";

    [Column("recipient")]
    [Required, StringLength(254)]
    public string Recipient { get; set; } = string.Empty;

    [Column("subject")]
    [StringLength(200)]
    public string? Subject { get; set; }

    [Column("body")]
    [StringLength(2000)]
    public string? Body { get; set; }

    /// <summary>See <see cref="NotificationStatuses"/>.</summary>
    [Column("status")]
    [Required, StringLength(10)]
    public string Status { get; set; } = string.Empty;

    [Column("dispute_id")]
    public Guid? DisputeId { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    public User? User { get; set; }
    public Dispute? Dispute { get; set; }
}
