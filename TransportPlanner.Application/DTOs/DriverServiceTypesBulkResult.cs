namespace TransportPlanner.Application.DTOs;

public class DriverServiceTypesBulkResult
{
    public int Updated { get; set; }
    public List<BulkErrorDto> Errors { get; set; } = new();
    public List<DriverServiceTypesBulkFailedItem> FailedItems { get; set; } = new();
}

public class DriverServiceTypesBulkFailedItem
{
    public string? Email { get; set; }
    public string? ServiceTypeIds { get; set; }
    public string? RowRef { get; set; }
    public string? Message { get; set; }
}
