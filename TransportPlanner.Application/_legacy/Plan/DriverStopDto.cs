namespace TransportPlanner.Application.DTOs.Plan;

public class DriverStopDto
{
    public int Sequence { get; set; }
    public int PoleId { get; set; }
    public string Serial { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string? Address { get; set; }
    public DateTime DueDate { get; set; }
    public DateTime? FixedDate { get; set; }
    public int ServiceMinutes { get; set; }
    public DateTime? PlannedStart { get; set; }
    public DateTime? PlannedEnd { get; set; }
    public int TravelMinutesFromPrev { get; set; }
    public double TravelKmFromPrev { get; set; }
}

