namespace TransportPlanner.Application.DTOs;

public class GeneratePlanRequest
{
    public DateTime FromDate { get; set; }
    public int Days { get; set; } = 14;
}

