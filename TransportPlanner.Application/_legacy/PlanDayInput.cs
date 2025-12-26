namespace TransportPlanner.Application.DTOs;

public class PlanDayInput
{
    public DateTime Date { get; set; }
    public List<DriverInput> Drivers { get; set; } = new();
    public List<PoleInput> Poles { get; set; } = new();
}

public class DriverInput
{
    public int Id { get; set; }
    public decimal StartLatitude { get; set; }
    public decimal StartLongitude { get; set; }
    public int AvailabilityMinutes { get; set; }
    public int MaxWorkMinutesPerDay { get; set; }
    public int DefaultServiceMinutes { get; set; }
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
}

public class PoleInput
{
    public int Id { get; set; }
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }
    public int ServiceMinutes { get; set; }
    public DateTime DueDate { get; set; }
    public DateTime? FixedDate { get; set; }
    public bool IsFixedForDate { get; set; }
}

