namespace TransportPlanner.Infrastructure.Options;

public class TravelTimeModelQualitySettings
{
    public const string SectionName = "TravelTimeModelQualitySettings";

    public bool UseLearnedOnlyIfApproved { get; set; } = true;
    public int LearnedSampleThreshold { get; set; } = 30;
    public int StaleAfterDays { get; set; } = 60;
    public decimal PlausibleMinutesPerKmMin { get; set; } = 0.6m;
    public decimal PlausibleMinutesPerKmMax { get; set; } = 3.0m;
    public decimal DeviationVsBaselineWarnPercent { get; set; } = 50m;
}
