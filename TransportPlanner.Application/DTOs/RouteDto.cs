namespace TransportPlanner.Application.DTOs;

public class RouteGeometryPointDto
{
    public double Lat { get; set; }
    public double Lng { get; set; }
}

public class RouteDto
{
    public int Id { get; set; }
    public DateTime Date { get; set; }
    public int OwnerId { get; set; }
    public int ServiceTypeId { get; set; }
    public int DriverId { get; set; }
    public string DriverName { get; set; } = string.Empty;
    public double? DriverStartLatitude { get; set; }
    public double? DriverStartLongitude { get; set; }
    public int TotalMinutes { get; set; }
    public double TotalKm { get; set; }
    public string Status { get; set; } = string.Empty; // RouteStatus as string
    public List<RouteStopDto> Stops { get; set; } = new List<RouteStopDto>();

    // Optional: road geometry points for drawing the route polyline on the map
    public List<RouteGeometryPointDto>? Geometry { get; set; }
}

