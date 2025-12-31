namespace TransportPlanner.Application.DTOs;

public class DriverServiceTypesBulkRequest
{
    public List<DriverServiceTypesBulkItem> Drivers { get; set; } = new();
}

public class DriverServiceTypesBulkItem
{
    public string? Email { get; set; }
    public Guid? DriverToolId { get; set; }
    public int? DriverErpId { get; set; }
    public string? ServiceTypeIds { get; set; }
}
