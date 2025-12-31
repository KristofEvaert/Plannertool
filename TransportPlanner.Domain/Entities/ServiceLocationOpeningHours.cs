namespace TransportPlanner.Domain.Entities;

public class ServiceLocationOpeningHours
{
    public int Id { get; set; }
    public int ServiceLocationId { get; set; }
    public int DayOfWeek { get; set; } // 0..6
    public TimeSpan? OpenTime { get; set; }
    public TimeSpan? CloseTime { get; set; }
    public TimeSpan? OpenTime2 { get; set; }
    public TimeSpan? CloseTime2 { get; set; }
    public bool IsClosed { get; set; }

    public ServiceLocation ServiceLocation { get; set; } = null!;
}
