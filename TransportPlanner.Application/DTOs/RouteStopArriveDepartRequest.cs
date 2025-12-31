namespace TransportPlanner.Application.DTOs;

public class RouteStopArriveRequest
{
    public DateTime? ArrivedAtUtc { get; set; }
}

public class RouteStopDepartRequest
{
    public DateTime? DepartedAtUtc { get; set; }
}
