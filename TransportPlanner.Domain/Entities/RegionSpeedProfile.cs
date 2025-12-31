namespace TransportPlanner.Domain.Entities;

public class RegionSpeedProfile
{
    public int Id { get; set; }
    public int RegionId { get; set; }
    public DayType DayType { get; set; }
    public int BucketStartHour { get; set; }
    public int BucketEndHour { get; set; }
    public decimal AvgMinutesPerKm { get; set; }

    public TravelTimeRegion Region { get; set; } = null!;
}
