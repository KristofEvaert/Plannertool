namespace TransportPlanner.Domain.Entities;

public class DriverServiceType
{
    public int DriverId { get; set; }
    public int ServiceTypeId { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public Driver Driver { get; set; } = null!;
    public ServiceType ServiceType { get; set; } = null!;
}
