namespace TransportPlanner.Domain.Entities;

public class DayPlanLock
{
    public int Id { get; set; }
    public int OwnerId { get; set; }
    public int ServiceTypeId { get; set; }
    public DateTime Date { get; set; } // Date-only
    public bool IsLocked { get; set; } = true;
    public int ExtraWorkMinutesAllDrivers { get; set; } = 0; // Overtime capacity for all drivers that day
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

