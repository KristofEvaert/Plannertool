namespace TransportPlanner.Application.DTOs;

public class TravelTimeModelContributorDto
{
    public int DriverId { get; set; }
    public string DriverName { get; set; } = string.Empty;
    public int SampleCount { get; set; }
    public DateTime? LastContributionUtc { get; set; }
}

public class TravelTimeModelLearnedStatDto
{
    public int Id { get; set; }
    public int RegionId { get; set; }
    public string RegionName { get; set; } = string.Empty;
    public string DayType { get; set; } = string.Empty;
    public int BucketStartHour { get; set; }
    public int BucketEndHour { get; set; }
    public decimal DistanceBandKmMin { get; set; }
    public decimal DistanceBandKmMax { get; set; }
    public string Status { get; set; } = string.Empty;
    public int TotalSampleCount { get; set; }
    public decimal? AvgMinutesPerKm { get; set; }
    public decimal? MinMinutesPerKm { get; set; }
    public decimal? MaxMinutesPerKm { get; set; }
    public DateTime? LastSampleAtUtc { get; set; }
    public decimal BaselineMinutesPerKm { get; set; }
    public decimal ExpectedRangeMin { get; set; }
    public decimal ExpectedRangeMax { get; set; }
    public decimal? DeviationPercent { get; set; }
    public bool IsOutOfRange { get; set; }
    public bool IsStale { get; set; }
    public bool IsLowSample { get; set; }
    public bool IsHighDeviation { get; set; }
    public decimal SuspiciousRatio { get; set; }
    public List<TravelTimeModelContributorDto> Contributors { get; set; } = new();
}

public class TravelTimeModelStatusUpdateRequest
{
    public string Status { get; set; } = string.Empty;
    public string? Note { get; set; }
}
