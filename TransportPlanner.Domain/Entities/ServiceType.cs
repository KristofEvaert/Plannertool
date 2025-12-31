namespace TransportPlanner.Domain.Entities;

public class ServiceType
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty; // e.g. "CHARGING_POST", "PHARMA"
    public string Name { get; set; } = string.Empty; // Display name
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    public ICollection<DriverServiceType> DriverServiceTypes { get; set; } = new List<DriverServiceType>();
}

