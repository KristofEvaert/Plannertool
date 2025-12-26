namespace TransportPlanner.Application.DTOs;

public class DriverAvailabilityBulkFailedEntry
{
    public string? Email { get; set; }
    public string? Date { get; set; }
    public int? StartMinuteOfDay { get; set; }
    public int? EndMinuteOfDay { get; set; }
    public string? RowRef { get; set; }
    public string? Message { get; set; }
}
