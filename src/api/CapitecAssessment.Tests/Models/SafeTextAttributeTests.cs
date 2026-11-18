using System.ComponentModel.DataAnnotations;
using CapitecAssessment.Models.Request;

namespace CapitecAssessment.Tests.Models;

/// <summary>
/// Tests for <c>[SafeText]</c>, the validation rule on free-text fields (dispute reason, details, notes).
///
/// It blocks invisible "control characters" (null bytes, terminal escape codes, etc.) that can corrupt
/// logs or emails. It deliberately ALLOWS text that looks like SQL: the database layer always passes
/// values as parameters, so such text is stored as harmless plain text.
/// </summary>
public class SafeTextAttributeTests
{
    [Theory]
    [InlineData("Card skimmed at ATM", true)]                        // normal text
    [InlineData("It's not mine; R1,000 charged twice", true)]        // punctuation is fine
    [InlineData("x'); DROP TABLE disputes;--", true)]                // looks like SQL, but is stored as harmless text
    [InlineData("line one\r\nline two\tindented", true)]             // new lines and tabs are fine
    [InlineData("null\0byte", false)]                                // null byte
    [InlineData("bell\a", false)]                                    // "bell" control character
    [InlineData("escape\u001b[31m", false)]                          // terminal colour escape code
    public void IsValid_RejectsControlCharactersOnly(string value, bool expectedValid) =>
        Assert.Equal(expectedValid, new SafeTextAttribute().IsValid(value));

    /// <summary>An empty (null) value passes. Whether a field is required is checked by a different rule.</summary>
    [Fact]
    public void IsValid_AllowsNull() => Assert.True(new SafeTextAttribute().IsValid(null));

    /// <summary>The rule is actually applied to the dispute request models (not just defined).</summary>
    [Fact]
    public void RequestModels_RejectNulCharacters()
    {
        var createDispute = new CreateDisputeRequest { TransactionId = Guid.NewGuid(), Reason = "ok", Details = "bad\0" };
        var updateDispute = new UpdateDisputeRequest { Status = "Resolved", Note = "bad\0" };

        Assert.False(Validator.TryValidateObject(createDispute, new ValidationContext(createDispute), [], validateAllProperties: true));
        Assert.False(Validator.TryValidateObject(updateDispute, new ValidationContext(updateDispute), [], validateAllProperties: true));
    }
}
