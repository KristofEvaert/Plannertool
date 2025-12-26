namespace TransportPlanner.Application.DTOs;

public class ServiceLocationBulkExportResponse
{
    public DateTime GeneratedAtUtc { get; set; }
    public int ServiceTypeId { get; set; }
    public int OwnerId { get; set; }
    public List<BulkServiceLocationInsertDto> Items { get; set; } = new();
}
