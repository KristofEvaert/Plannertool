namespace TransportPlanner.Application.DTOs;

public class CreateServiceLocationRequest
{
    public int ErpId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Address { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public DateTime DueDate { get; set; } // Date-only
    public DateTime? PriorityDate { get; set; } // Date-only, optional
    public int? ServiceMinutes { get; set; }
    public int ServiceTypeId { get; set; } // Required
    public int OwnerId { get; set; } // Required
    public string? DriverInstruction { get; set; }
    public List<string>? ExtraInstructions { get; set; }
}
