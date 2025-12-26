namespace TransportPlanner.Application.DTOs.Plan;

public class DriverRouteSummaryDto
{
    public int DriverId { get; set; }
    public string DriverName { get; set; } = string.Empty;
    public int? RouteId { get; set; }
    public bool IsRouteLocked { get; set; }
    public int StopCount { get; set; }
    public int TotalMinutes { get; set; }
    public double TotalKm { get; set; }
    public double StartLatitude { get; set; }
    public double StartLongitude { get; set; }
    public string? ImageUrl { get; set; }
    public List<RouteStopDto> Stops { get; set; } = new();
}

public class RouteStopDto
{
    public int Sequence { get; set; }
    public int PoleId { get; set; }
    public string Serial { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string? Address { get; set; }
}

