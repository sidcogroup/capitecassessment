namespace CapitecAssessment.Models.Response;

public class DatabaseHealthResponse
{
    public string Status { get; set; } = string.Empty;
    public bool CanConnect { get; set; }
    public bool SchemaPresent { get; set; }
    public DateTime TimestampUtc { get; set; }
}
