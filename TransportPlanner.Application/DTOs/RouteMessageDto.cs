namespace TransportPlanner.Application.DTOs;

public class RouteMessageDto
{
    public int Id { get; set; }
    public int RouteId { get; set; }
    public int? RouteStopId { get; set; }
    public int DriverId { get; set; }
    public string DriverName { get; set; } = string.Empty;
    public Guid? PlannerId { get; set; }
    public string MessageText { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
}

public class CreateRouteMessageRequest
{
    public int RouteId { get; set; }
    public int? RouteStopId { get; set; }
    public string MessageText { get; set; } = string.Empty;
    public string Category { get; set; } = "Info";
}
