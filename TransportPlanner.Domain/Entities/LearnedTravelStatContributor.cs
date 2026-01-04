namespace TransportPlanner.Domain.Entities;

public class LearnedTravelStatContributor
{
    public int Id { get; set; }
    public int LearnedTravelStatsId { get; set; }
    public int DriverId { get; set; }
    public int SampleCount { get; set; }
    public DateTime? LastContributionUtc { get; set; }

    public LearnedTravelStats LearnedTravelStats { get; set; } = null!;
    public Driver Driver { get; set; } = null!;
}
