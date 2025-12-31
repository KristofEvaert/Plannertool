namespace TransportPlanner.Domain.Entities;

public class ServiceLocation
{
    public int Id { get; set; }
    public Guid ToolId { get; set; } // Unique ID for this tool
    public int ErpId { get; set; } // ERP ID, unique
    public string Name { get; set; } = string.Empty;
    public string? Address { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public DateTime DueDate { get; set; } // Store as date-only (normalize .Date)
    public DateTime? PriorityDate { get; set; } // Store as date-only, optional
    public int ServiceMinutes { get; set; } = 20;
    public int ServiceTypeId { get; set; } // Just an int column, no FK constraint
    // Navigation property removed - no FK constraint
    public int OwnerId { get; set; } // Required, just an int column, no FK constraint
    // Navigation property removed - no FK constraint
    public ServiceLocationStatus Status { get; set; } = ServiceLocationStatus.Open;
    public bool IsActive { get; set; } = true;
    public string? DriverInstruction { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    public ICollection<ServiceLocationOpeningHours> OpeningHours { get; set; } = new List<ServiceLocationOpeningHours>();
    public ICollection<ServiceLocationException> Exceptions { get; set; } = new List<ServiceLocationException>();
    public ServiceLocationConstraint? Constraint { get; set; }
    public ICollection<WeightTemplateLocationLink> WeightTemplateLinks { get; set; } = new List<WeightTemplateLocationLink>();
}
