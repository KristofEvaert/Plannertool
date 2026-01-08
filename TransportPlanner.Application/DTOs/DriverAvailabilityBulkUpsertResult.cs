namespace TransportPlanner.Application.DTOs;

public class DriverAvailabilityBulkUpsertResult
{
    public int Inserted { get; set; }
    public int Updated { get; set; }
    public int Deleted { get; set; }
    public List<BulkErrorDto> Errors { get; set; } = new();
    public List<DriverAvailabilityBulkFailedEntry> FailedEntries { get; set; } = new();
    public List<DriverAvailabilityBulkConflictEntry> Conflicts { get; set; } = new();
}
