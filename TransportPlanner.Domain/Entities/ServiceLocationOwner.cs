namespace TransportPlanner.Domain.Entities;

public class ServiceLocationOwner
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty; // e.g. "TRESCAL_ANTWERP", "TRESCAL_ZOETERMEER"
    public string Name { get; set; } = string.Empty; // e.g. "Trescal Antwerp"
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public ICollection<ServiceType> ServiceTypes { get; set; } = new List<ServiceType>();
}

