using System.ComponentModel.DataAnnotations;

namespace CapitecAssessment.Models.Request;

/// <summary>Body for a consultant moving a dispute to a new status, with a note the client will be emailed.</summary>
public sealed class UpdateDisputeRequest
{
    /// <summary>The new status: "Under Review", "Resolved" or "Rejected".</summary>
    [Required(AllowEmptyStrings = false)]
    [AllowedValues(DisputeStatuses.UnderReview, DisputeStatuses.Resolved, DisputeStatuses.Rejected,
        ErrorMessage = "Status must be 'Under Review', 'Resolved' or 'Rejected'.")]
    public string Status { get; init; } = string.Empty;

    /// <summary>Recorded in the dispute history and sent to the client. Matches dispute_status_history.note (500).</summary>
    [Required(AllowEmptyStrings = false), StringLength(500), SafeText]
    public string Note { get; init; } = string.Empty;
}
