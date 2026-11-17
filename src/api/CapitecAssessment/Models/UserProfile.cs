using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CapitecAssessment.Models;

/// <summary>Maps to cadb.user_profiles (one-to-one with users).</summary>
[Table("user_profiles")]
public class UserProfile
{
    [Key]
    [Column("user_id")]
    public long UserId { get; set; }

    [Column("first_name")]
    [Required, StringLength(50)]
    public string FirstName { get; set; } = string.Empty;

    [Column("last_name")]
    [Required, StringLength(50)]
    public string LastName { get; set; } = string.Empty;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }

    public User? User { get; set; }
}
