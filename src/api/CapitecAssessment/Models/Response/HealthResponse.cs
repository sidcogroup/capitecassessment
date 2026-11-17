namespace CapitecAssessment.Models.Response;

public class HealthResponse
{
    public string Status { get; set; } = string.Empty;
    public string Service { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public DateTime TimestampUtc { get; set; }
}
