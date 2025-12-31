namespace TransportPlanner.Domain.Entities;

public class Driver
{
    public int Id { get; set; }
    public Guid ToolId { get; set; } // Unique ID for this tool
    public int ErpId { get; set; } // ERP ID, unique
    public string Name { get; set; } = string.Empty;
    public string? StartAddress { get; set; }
    public double? StartLatitude { get; set; }
    public double? StartLongitude { get; set; }
    public int DefaultServiceMinutes { get; set; } = 20;
    public int MaxWorkMinutesPerDay { get; set; } = 480;
    public int OwnerId { get; set; } // Required, just an int column, no FK constraint
    // Navigation property removed - no FK constraint
    public Guid? UserId { get; set; } // Linked application user
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    
    // Navigation
    public ICollection<DriverAvailability> Availabilities { get; set; } = new List<DriverAvailability>();
    public ICollection<DriverServiceType> DriverServiceTypes { get; set; } = new List<DriverServiceType>();
}
