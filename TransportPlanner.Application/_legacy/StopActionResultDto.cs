namespace TransportPlanner.Application.DTOs;

public class StopActionResultDto
{
    public int RouteId { get; set; }
    public int StopId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime? ArrivedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? Note { get; set; }
}

