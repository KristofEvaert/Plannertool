namespace TransportPlanner.Application.DTOs.Plan;

public class DriverDayDto
{
    public DateTime Date { get; set; }
    public int DriverId { get; set; }
    public string DriverName { get; set; } = string.Empty;
    public bool DayIsLocked { get; set; }
    public int? RouteId { get; set; }
    public bool RouteIsLocked { get; set; }
    public int TotalMinutes { get; set; }
    public double TotalKm { get; set; }
    public double StartLatitude { get; set; }
    public double StartLongitude { get; set; }
    public string? ImageUrl { get; set; }
    public List<DriverStopDto> Stops { get; set; } = new();
}

