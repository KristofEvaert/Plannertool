namespace TransportPlanner.Application.DTOs;

public class BulkServiceLocationFailedItem
{
    public string RowRef { get; set; } = string.Empty;
    public BulkServiceLocationInsertDto? Item { get; set; }
}
