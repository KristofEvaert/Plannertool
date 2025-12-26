namespace TransportPlanner.Application.DTOs;

public class ErpPoleDto
{
    public string Serial { get; set; } = string.Empty;
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }
    public DateTime DueDate { get; set; }
}

