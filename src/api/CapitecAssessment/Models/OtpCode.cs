using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CapitecAssessment.Models;

/// <summary>Maps to cadb.otp_codes. Only a hash of the code is stored.</summary>
[Table("otp_codes")]
public class OtpCode
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("user_id")]
    public long UserId { get; set; }

    [Column("code_hash")]
    [Required, StringLength(100)]
    public string CodeHash { get; set; } = string.Empty;

    [Column("expires_at")]
    public DateTime ExpiresAt { get; set; }

    [Column("attempts")]
    public short Attempts { get; set; }

    [Column("consumed_at")]
    public DateTime? ConsumedAt { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    public User? User { get; set; }
}
