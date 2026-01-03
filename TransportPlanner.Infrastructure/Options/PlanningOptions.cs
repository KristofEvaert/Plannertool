namespace TransportPlanner.Infrastructure.Options;

/// <summary>
/// Configuration options for planning engine selection.
/// Set "Planning:Engine" in appsettings.json to "Greedy" or "OrTools"
/// </summary>
public class PlanningOptions
{
    public const string SectionName = "Planning";

    /// <summary>
    /// Planning engine to use: "Greedy" or "OrTools"
    /// Defaults to "Greedy" if not specified
    /// </summary>
    public string Engine { get; set; } = "Greedy";

    /// <summary>
    /// OR-Tools specific configuration
    /// </summary>
    public OrToolsOptions OrTools { get; set; } = new();
}

/// <summary>
/// Configuration options for OR-Tools route planner
/// </summary>
public class OrToolsOptions
{
    /// <summary>
    /// Maximum time limit for solver in seconds
    /// </summary>
    public int TimeLimitSeconds { get; set; } = 2;

    /// <summary>
    /// Maximum number of solutions to find before stopping
    /// </summary>
    public int SolutionLimit { get; set; } = 1;

    /// <summary>
    /// First solution strategy: "PATH_CHEAPEST_ARC", "SAVINGS", etc.
    /// </summary>
    public string FirstSolutionStrategy { get; set; } = "PATH_CHEAPEST_ARC";

    /// <summary>
    /// Local search metaheuristic: "GUIDED_LOCAL_SEARCH", "TABU_SEARCH", etc.
    /// </summary>
    public string LocalSearchMetaheuristic { get; set; } = "GUIDED_LOCAL_SEARCH";

    /// <summary>
    /// Maximum number of candidate poles to consider per day (for performance)
    /// </summary>
    public int MaxDailyCandidates { get; set; } = 300;

    /// <summary>
    /// Due-date cost cap expressed as km-equivalent for normalization.
    /// </summary>
    public double DueCostCapKm { get; set; } = 35;

    /// <summary>
    /// Detour cost cap expressed as km-equivalent for normalization.
    /// </summary>
    public double DetourCostCapKm { get; set; } = 30;

    /// <summary>
    /// Detour reference kilometers for normalization.
    /// </summary>
    public double DetourRefKm { get; set; } = 30;

    /// <summary>
    /// Lateness reference minutes for due-date penalty normalization.
    /// </summary>
    public int LateRefMinutes { get; set; } = 240;
}

