namespace TransportPlanner.Domain.Entities;

public class RouteVersion
{
    public int Id { get; set; }
    public int RouteId { get; set; }
    public int VersionNumber { get; set; }
    public DateTime CreatedUtc { get; set; }
    public Guid CreatedBy { get; set; }
    public string? ChangeSummary { get; set; }

    public Route Route { get; set; } = null!;
    public ICollection<RouteChangeNotification> Notifications { get; set; } = new List<RouteChangeNotification>();
}
