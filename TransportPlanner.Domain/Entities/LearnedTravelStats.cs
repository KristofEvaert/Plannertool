namespace TransportPlanner.Domain.Entities;

public class LearnedTravelStats
{
    public int Id { get; set; }
    public int RegionId { get; set; }
    public DayType DayType { get; set; }
    public int BucketStartHour { get; set; }
    public int BucketEndHour { get; set; }
    public decimal DistanceBandKmMin { get; set; }
    public decimal DistanceBandKmMax { get; set; }
    public int SampleCount { get; set; }
    public decimal AvgMinutesPerKm { get; set; }
    public decimal? AvgStopServiceMinutes { get; set; }

    public TravelTimeRegion Region { get; set; } = null!;
}
