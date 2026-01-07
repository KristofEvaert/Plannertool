namespace TransportPlanner.Application.DTOs;

public class ServiceLocationDto
{
    public int Id { get; set; }
    public Guid ToolId { get; set; }
    public int ErpId { get; set; }
    public string? AccountId { get; set; }
    public string? SerialNumber { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Address { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public DateTime DueDate { get; set; } // Date-only, returned as DateTime
    public DateTime? PriorityDate { get; set; } // Date-only, optional
    public int ServiceMinutes { get; set; }
    public int ServiceTypeId { get; set; }
    public string? ServiceTypeName { get; set; } // Convenience field
    public int OwnerId { get; set; }
    public string? OwnerName { get; set; } // Convenience field
    public string? DriverInstruction { get; set; }
    public List<string> ExtraInstructions { get; set; } = new();
    public string Status { get; set; } = "Open"; // "Open", "Done", "Cancelled"
    public bool IsActive { get; set; }
    public string? Remark { get; set; }
}
