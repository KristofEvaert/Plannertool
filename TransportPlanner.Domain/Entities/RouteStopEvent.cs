namespace TransportPlanner.Domain.Entities;

public class RouteStopEvent
{
    public int Id { get; set; }
    public int RouteStopId { get; set; }
    public RouteStopEventType EventType { get; set; }
    public DateTime EventUtc { get; set; }
    public string? PayloadJson { get; set; }

    public RouteStop RouteStop { get; set; } = null!;
}
