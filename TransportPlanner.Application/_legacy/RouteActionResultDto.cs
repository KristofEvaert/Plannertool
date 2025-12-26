namespace TransportPlanner.Application.DTOs;

public class RouteActionResultDto
{
    public int RouteId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

