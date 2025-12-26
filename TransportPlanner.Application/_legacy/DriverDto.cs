namespace TransportPlanner.Application.DTOs;

public class DriverDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public double StartLatitude { get; set; }
    public double StartLongitude { get; set; }
    public int DefaultServiceMinutes { get; set; }
    public int MaxWorkMinutesPerDay { get; set; }
}
