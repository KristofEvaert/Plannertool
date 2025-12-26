namespace TransportPlanner.Application.DTOs;

public class BulkServiceLocationInsertDto
{
    public int ErpId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Address { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public DateOnly DueDate { get; set; }
    public DateOnly? PriorityDate { get; set; }
    public int? ServiceMinutes { get; set; }
    public string? DriverInstruction { get; set; }
}
