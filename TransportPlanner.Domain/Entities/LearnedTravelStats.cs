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
    public decimal? AvgMinutesPerKm { get; set; }
    public decimal? AvgStopServiceMinutes { get; set; }
    public LearnedTravelStatStatus Status { get; set; } = LearnedTravelStatStatus.Draft;
    public Guid? ApprovedByUserId { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public DateTime? LastSampleAtUtc { get; set; }
    public decimal? MinMinutesPerKm { get; set; }
    public decimal? MaxMinutesPerKm { get; set; }
    public int SuspiciousSampleCount { get; set; }
    public int TotalSampleCount { get; set; }

    public TravelTimeRegion Region { get; set; } = null!;
    public ICollection<LearnedTravelStatContributor> Contributors { get; set; } = new List<LearnedTravelStatContributor>();
}
