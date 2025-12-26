namespace TransportPlanner.Application.DTOs;

public class DriverAvailabilityExportResponse
{
    public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;
    public Dictionary<string, DriverAvailabilityExportDriver> Drivers { get; set; } = new();
}

public class DriverAvailabilityExportDriver
{
    public string Email { get; set; } = string.Empty;
    public Guid DriverToolId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int OwnerId { get; set; }
    public List<DriverAvailabilityExportEntry> Availabilities { get; set; } = new();
}

public class DriverAvailabilityExportEntry
{
    public string Date { get; set; } = string.Empty; // yyyy-MM-dd
    public int StartMinuteOfDay { get; set; }
    public int EndMinuteOfDay { get; set; }
}
