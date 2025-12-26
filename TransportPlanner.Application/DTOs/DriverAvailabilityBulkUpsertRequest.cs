namespace TransportPlanner.Application.DTOs;

public class DriverAvailabilityBulkUpsertRequest
{
    public Dictionary<string, DriverAvailabilityBulkDriver> Drivers { get; set; } = new();
}

public class DriverAvailabilityBulkDriver
{
    public string? Email { get; set; }
    public Guid? DriverToolId { get; set; }
    public string? Name { get; set; }
    public int? OwnerId { get; set; }
    public List<DriverAvailabilityBulkEntry> Availabilities { get; set; } = new();
}

public class DriverAvailabilityBulkEntry
{
    public string Date { get; set; } = string.Empty; // yyyy-MM-dd
    public int? StartMinuteOfDay { get; set; }
    public int? EndMinuteOfDay { get; set; }
}
