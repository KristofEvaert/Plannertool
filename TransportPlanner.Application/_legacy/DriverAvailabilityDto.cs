namespace TransportPlanner.Application.DTOs;

public class DriverAvailabilityDto
{
    public int Id { get; set; }
    public int DriverId { get; set; }
    public DateTime Date { get; set; }
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
}

