namespace TransportPlanner.Application.DTOs;

public class RouteChangeNotificationDto
{
    public int Id { get; set; }
    public int RouteId { get; set; }
    public int RouteVersionId { get; set; }
    public string Severity { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; }
    public DateTime? AcknowledgedUtc { get; set; }
    public string? ChangeSummary { get; set; }
}
