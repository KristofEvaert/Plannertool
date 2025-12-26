namespace TransportPlanner.Api.Options;

public class AuditTrailOptions
{
    public const string SectionName = "AuditTrail";

    public string Path { get; set; } = "App_Data/audit-trail.txt";
    public int MaxBodyBytes { get; set; } = 65536;
}
