using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CapitecAssessment.Models;

/// <summary>Maps to cadb.user_contact (one-to-one with users).</summary>
[Table("user_contact")]
public class UserContact
{
    [Key]
    [Column("user_id")]
    public long UserId { get; set; }

    [Column("contact_number")]
    [Required, StringLength(13)]
    public string ContactNumber { get; set; } = string.Empty;

    [Column("contact_email")]
    [Required, StringLength(254)]
    public string ContactEmail { get; set; } = string.Empty;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }

    public User? User { get; set; }
}
