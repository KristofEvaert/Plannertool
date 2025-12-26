namespace TransportPlanner.Application.DTOs;

public class PoleListItemDto
{
    public int PoleId { get; set; }
    public string Serial { get; set; } = string.Empty;
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }
    public DateTime DueDate { get; set; }
    public DateTime? FixedDate { get; set; }
    public int ServiceMinutes { get; set; }
    public string Status { get; set; } = string.Empty;
}

