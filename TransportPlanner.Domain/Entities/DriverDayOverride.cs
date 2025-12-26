namespace TransportPlanner.Domain.Entities;

public class DriverDayOverride
{
    public int Id { get; set; }
    public int OwnerId { get; set; }
    public int ServiceTypeId { get; set; }
    public DateTime Date { get; set; } // Date-only
    public int DriverId { get; set; }
    public int ExtraWorkMinutes { get; set; } = 0; // Overtime capacity for that driver/day
    public bool IsLocked { get; set; } = false; // Locks this driver's route
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    
    // Navigation
    public Driver Driver { get; set; } = null!;
}

