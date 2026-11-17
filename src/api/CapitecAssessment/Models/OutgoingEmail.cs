namespace CapitecAssessment.Models;

public sealed record OutgoingEmail(string Recipient, string Subject, string PlainText, string Html);
