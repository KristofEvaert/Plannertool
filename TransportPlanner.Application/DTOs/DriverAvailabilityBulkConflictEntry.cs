namespace TransportPlanner.Application.DTOs;

public class DriverAvailabilityBulkConflictEntry
{
    public string? Email { get; set; }
    public string? DriverName { get; set; }
    public string? Date { get; set; }
    public int? ExistingStartMinuteOfDay { get; set; }
    public int? ExistingEndMinuteOfDay { get; set; }
    public int? NewStartMinuteOfDay { get; set; }
    public int? NewEndMinuteOfDay { get; set; }
    public string? RowRef { get; set; }
    public string? Reason { get; set; }
}
