namespace TransportPlanner.Domain.Entities;

public class RouteChangeNotification
{
    public int Id { get; set; }
    public int RouteId { get; set; }
    public int RouteVersionId { get; set; }
    public int DriverId { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime? AcknowledgedUtc { get; set; }
    public RouteChangeSeverity Severity { get; set; } = RouteChangeSeverity.Info;

    public Route Route { get; set; } = null!;
    public RouteVersion RouteVersion { get; set; } = null!;
    public Driver Driver { get; set; } = null!;
}
