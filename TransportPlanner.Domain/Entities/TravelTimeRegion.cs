namespace TransportPlanner.Domain.Entities;

public class TravelTimeRegion
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string CountryCode { get; set; } = string.Empty;
    public decimal BboxMinLat { get; set; }
    public decimal BboxMinLon { get; set; }
    public decimal BboxMaxLat { get; set; }
    public decimal BboxMaxLon { get; set; }
    public int Priority { get; set; }

    public ICollection<RegionSpeedProfile> SpeedProfiles { get; set; } = new List<RegionSpeedProfile>();
    public ICollection<LearnedTravelStats> LearnedStats { get; set; } = new List<LearnedTravelStats>();
}
