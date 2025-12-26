namespace TransportPlanner.Application.DTOs;

public class GeneratePlanResultDto
{
    public DateTime FromDate { get; set; }
    public int Days { get; set; }
    public int GeneratedDays { get; set; }
    public int SkippedLockedDays { get; set; }
    public int PlannedPolesCount { get; set; }
    public int UnplannedPolesCount { get; set; }
    public int LatePolesCount { get; set; }
    
    // End-of-window shortage indicators
    public int DueTodayUnplannedCountAtEnd { get; set; }
    public int OverdueCountAtEnd { get; set; }
    
    // Generation result details
    public int PlannedStops { get; set; }
    public int CreatedRoutes { get; set; }
    public int RemainingInHorizonCountAtEnd { get; set; }
    public int RemainingBacklogCount { get; set; }
}

