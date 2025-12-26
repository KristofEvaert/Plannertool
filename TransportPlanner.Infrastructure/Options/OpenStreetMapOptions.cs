namespace TransportPlanner.Infrastructure.Options;

public class OpenStreetMapOptions
{
    public const string SectionName = "OpenStreetMap";
    public string UserAgent { get; set; } = "TransportPlanner/1.0";
    public string? Email { get; set; }
}
