namespace TransportPlanner.Domain.Entities;

public enum RouteStopType
{
    Cluster = 0,
    Location = 1
}

public class RouteStop
{
    public int Id { get; set; }
    public int RouteId { get; set; }
    public int Sequence { get; set; }
    public RouteStopType StopType { get; set; }
    
    // For StopType=Cluster: PlanningClusterId is set
    // For StopType=Location: ServiceLocationId is set (unclustered)
    public int? PlanningClusterId { get; set; }
    public int? ServiceLocationId { get; set; }
    
    // Location coordinates (centroid for cluster, actual for location)
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    
    public int ServiceMinutes { get; set; }
    // DB column is float(18) (SQL Server 4-byte float), so use float to match and avoid Single->Double cast errors
    public float TravelKmFromPrev { get; set; }
    public int TravelMinutesFromPrev { get; set; }
    
    // Optional v1 - can be null initially
    public DateTime? PlannedStart { get; set; }
    public DateTime? PlannedEnd { get; set; }
    
    public RouteStopStatus Status { get; set; } = RouteStopStatus.Pending;
    public bool ManualAdded { get; set; } = false; // True if manually added by user
    public DateTime? ArrivedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    
    /// <summary>
    /// Actual visit duration in minutes (recorded by driver). Nullable until the stop is completed or manually entered.
    /// </summary>
    public int? ActualServiceMinutes { get; set; }
    public string? Note { get; set; }
    
    // Navigation
    public Route Route { get; set; } = null!;
    public PlanningCluster? PlanningCluster { get; set; }
    public ServiceLocation? ServiceLocation { get; set; }
}

