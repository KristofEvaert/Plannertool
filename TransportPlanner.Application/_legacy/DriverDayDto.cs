namespace TransportPlanner.Application.DTOs;

public class DriverDayDto
{
    public int DriverId { get; set; }
    public string DriverName { get; set; } = string.Empty;
    public DateOnly Date { get; set; }
    public bool IsLocked { get; set; }
    public RouteDetailDto? Route { get; set; }
}

public class RouteDetailDto
{
    public int RouteId { get; set; }
    public bool IsLocked { get; set; }
    public int TotalMinutes { get; set; }
    public decimal TotalKm { get; set; }
    public List<RouteStopDto> Stops { get; set; } = new();
}

public class RouteStopDto
{
    public int StopId { get; set; }
    public int Sequence { get; set; }
    public int PoleId { get; set; }
    public string PoleSerial { get; set; } = string.Empty;
    public DateTime PlannedStart { get; set; }
    public DateTime PlannedEnd { get; set; }
    public int TravelMinutesFromPrev { get; set; }
    public decimal TravelKmFromPrev { get; set; }
}

