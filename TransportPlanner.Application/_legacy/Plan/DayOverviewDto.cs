namespace TransportPlanner.Application.DTOs.Plan;

public class DayOverviewDto
{
    public DateTime Date { get; set; }
    public bool IsLocked { get; set; }
    public int ExtraWorkMinutes { get; set; }
    public List<DriverRouteSummaryDto> Drivers { get; set; } = new();
    public List<PoleDto> UnplannedPoles { get; set; } = new();
    
    // Backlog counts (existing)
    public int EligibleBacklogCount { get; set; }
    public int DueTodayCount { get; set; }
    public int OverdueCount { get; set; }
    public int DueTodayUnplannedCount { get; set; }
    public int FixedForDayCount { get; set; }
    public int FixedForDayUnplannedCount { get; set; }
    
    // New backlog metrics
    public int TotalOpenPolesCount { get; set; }
    public int UnplannedInHorizonCount { get; set; }
    public int PlannedInHorizonCount { get; set; }
    public int HorizonDays { get; set; }
    
    // Backlog-driven metrics (core)
    public int TotalBacklogCount { get; set; }
    public int PlannedTodayCount { get; set; }
    public int RemainingBacklogCount { get; set; }
    
    // Horizon-based metrics (primary)
    public int TotalToPlanInHorizonCount { get; set; }
    public int PlannedInHorizonCount { get; set; }
    public int RemainingInHorizonCount { get; set; }
    // Note: unplannedPoles is now backlog preview (top N urgent in horizon, not yet planned)
}

