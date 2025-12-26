namespace TransportPlanner.Domain.Entities;

public class Route
{
    public int Id { get; set; }
    public DateTime Date { get; set; } // Date-only
    public int OwnerId { get; set; }
    public int ServiceTypeId { get; set; }
    public int DriverId { get; set; }
    public int TotalMinutes { get; set; }
    // DB column is float(18) (SQL Server 4-byte float), so use float to match and avoid Single->Double cast errors
    public float TotalKm { get; set; }
    public RouteStatus Status { get; set; } = RouteStatus.Temp;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? Notes { get; set; }
    
    // Navigation
    public Driver Driver { get; set; } = null!;
    public ICollection<RouteStop> Stops { get; set; } = new List<RouteStop>();
}

