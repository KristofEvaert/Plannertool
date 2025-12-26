namespace TransportPlanner.Application.DTOs.Plan;

public class PoleDto
{
    public int PoleId { get; set; }
    public string Serial { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public DateTime DueDate { get; set; }
    public DateTime? FixedDate { get; set; }
    public int ServiceMinutes { get; set; }
}

