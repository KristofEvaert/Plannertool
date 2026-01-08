namespace TransportPlanner.Application.DTOs;

public class BulkServiceLocationInsertDto
{
    public Guid? ToolId { get; set; }
    public int? ErpId { get; set; }
    public string? AccountId { get; set; }
    public string? SerialNumber { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Address { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public DateOnly DueDate { get; set; }
    public DateOnly? PriorityDate { get; set; }
    public int? ServiceMinutes { get; set; }
    public string? DriverInstruction { get; set; }
    public List<string>? ExtraInstructions { get; set; }
    public int? MinVisitDurationMinutes { get; set; }
    public int? MaxVisitDurationMinutes { get; set; }
    public List<ServiceLocationOpeningHoursDto>? OpeningHours { get; set; }
    public List<ServiceLocationExceptionDto>? Exceptions { get; set; }
}
