namespace CapitecAssessment.Models;

// Allowed values for the columns guarded by CHECK constraints in cadb.
// The database is the source of truth; these avoid magic strings in code.

public static class UserRoles
{
    public const string Customer = "Customer";
    public const string Consultant = "Consultant";
}

public static class AccountTypes
{
    public const string Transact = "Transact";
    public const string Savings = "Savings";
    public const string Credit = "Credit";
}

public static class TransactionTypes
{
    public const string Debit = "Debit";
    public const string Credit = "Credit";
}

public static class DisputeStatuses
{
    public const string Submitted = "Submitted";
    public const string UnderReview = "Under Review";
    public const string Resolved = "Resolved";
    public const string Rejected = "Rejected";

    /// <summary>Statuses that count as "open" for the one-open-dispute-per-transaction rule.</summary>
    public static readonly string[] Open = [Submitted, UnderReview];
}

public static class NotificationTypes
{
    public const string Otp = "Otp";
    public const string DisputeReceived = "DisputeReceived";
    public const string DisputeProgress = "DisputeProgress";
    public const string DisputeFinalised = "DisputeFinalised";
}

public static class NotificationStatuses
{
    public const string Sent = "Sent";
    public const string Failed = "Failed";
}
