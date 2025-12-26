namespace TransportPlanner.Domain.Entities;

public class DriverAvailability
{
    public int Id { get; set; }
    public int DriverId { get; set; }
    public DateTime Date { get; set; } // Date-only, normalized
    public int StartMinuteOfDay { get; set; } // 0..1439
    public int EndMinuteOfDay { get; set; } // 1..1440, must be > StartMinuteOfDay
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    
    // Navigation
    public Driver Driver { get; set; } = null!;
    
    // Computed property
    public int AvailableMinutes => EndMinuteOfDay - StartMinuteOfDay;
}

