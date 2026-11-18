using System.ComponentModel.DataAnnotations;
using CapitecAssessment.Models.Request;

namespace CapitecAssessment.Tests.Models;

public class SafeTextAttributeTests
{
    [Theory]
    [InlineData("Card skimmed at ATM", true)]
    [InlineData("It's not mine; R1,000 charged twice", true)]
    [InlineData("x'); DROP TABLE disputes;--", true)]   // allowed: parameterization makes it inert data
    [InlineData("line one\r\nline two\tindented", true)]
    [InlineData("null\0byte", false)]
    [InlineData("bell\a", false)]
    [InlineData("escape\u001b[31m", false)]
    public void IsValid_RejectsControlCharactersOnly(string value, bool expected) =>
        Assert.Equal(expected, new SafeTextAttribute().IsValid(value));

    [Fact]
    public void IsValid_AllowsNull() => Assert.True(new SafeTextAttribute().IsValid(null));

    [Fact]
    public void RequestModels_RejectNulCharacters()
    {
        var dispute = new CreateDisputeRequest { TransactionId = Guid.NewGuid(), Reason = "ok", Details = "bad\0" };
        var update = new UpdateDisputeRequest { Status = "Resolved", Note = "bad\0" };

        Assert.False(Validator.TryValidateObject(dispute, new ValidationContext(dispute), [], true));
        Assert.False(Validator.TryValidateObject(update, new ValidationContext(update), [], true));
    }
}
