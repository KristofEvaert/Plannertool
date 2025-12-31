namespace TransportPlanner.Application.DTOs;

public class DriverServiceTypesBulkExportResponse
{
    public DateTime GeneratedAtUtc { get; set; }
    public List<DriverServiceTypesBulkItem> Drivers { get; set; } = new();
}
