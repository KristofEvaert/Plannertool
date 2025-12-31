namespace TransportPlanner.Domain.Entities;

public class RouteMessage
{
    public int Id { get; set; }
    public int RouteId { get; set; }
    public int? RouteStopId { get; set; }
    public int DriverId { get; set; }
    public Guid? PlannerId { get; set; }
    public string MessageText { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; }
    public RouteMessageStatus Status { get; set; } = RouteMessageStatus.New;
    public RouteMessageCategory Category { get; set; } = RouteMessageCategory.Info;

    public Route Route { get; set; } = null!;
    public RouteStop? RouteStop { get; set; }
    public Driver Driver { get; set; } = null!;
}
