namespace TransportPlanner.Application.DTOs;

public class RouteStopDto
{
    public int Id { get; set; }
    public int Sequence { get; set; }
    public int? ServiceLocationId { get; set; }
    public Guid? ServiceLocationToolId { get; set; } // Use ToolId for frontend matching
    public string? Name { get; set; } // Service location name (fallback for UI if map doesn't have the point)
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public int ServiceMinutes { get; set; }
    public int? ActualServiceMinutes { get; set; }
    public double TravelKmFromPrev { get; set; }
    public int TravelMinutesFromPrev { get; set; }
    public string Status { get; set; } = string.Empty; // RouteStopStatus as string
    public DateTime? ArrivedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string? Note { get; set; }
    public string? DriverInstruction { get; set; }
}
