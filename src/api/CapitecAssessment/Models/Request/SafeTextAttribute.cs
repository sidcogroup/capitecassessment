using System.ComponentModel.DataAnnotations;

namespace CapitecAssessment.Models.Request;

/// <summary>
/// Free-text hardening. SQL injection is prevented by parameterized queries (EF Core), not by filtering
/// characters, so quotes, semicolons and the like are allowed. This only rejects NUL and other control
/// characters (tab, CR and LF are allowed), which have no place in user text and which PostgreSQL rejects.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class SafeTextAttribute : ValidationAttribute
{
    public SafeTextAttribute() : base("The text contains invalid control characters.") { }

    public override bool IsValid(object? value) =>
        value is not string text || !text.Any(c => char.IsControl(c) && c is not ('\t' or '\r' or '\n'));
}
