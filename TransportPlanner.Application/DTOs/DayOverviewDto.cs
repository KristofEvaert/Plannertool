namespace TransportPlanner.Application.DTOs;

public class DayOverviewDto
{
    public DateOnly Date { get; set; }
    public bool IsLocked { get; set; }
    public List<RouteOverviewDto> Routes { get; set; } = new();
}

public class RouteOverviewDto
{
    public int RouteId { get; set; }
    public int DriverId { get; set; }
    public string DriverName { get; set; } = string.Empty;
    public bool IsLocked { get; set; }
    public int TotalMinutes { get; set; }
    public decimal TotalKm { get; set; }
    public int StopCount { get; set; }
}

