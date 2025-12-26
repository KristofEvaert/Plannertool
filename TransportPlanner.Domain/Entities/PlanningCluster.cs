namespace TransportPlanner.Domain.Entities;

public class PlanningCluster
{
    public int Id { get; set; }
    public int OwnerId { get; set; }
    public int ServiceTypeId { get; set; }
    
    // ClusterDate = MIN(OrderDate) of items in cluster
    // OrderDate = PriorityDate ?? DueDate for ServiceLocation
    public DateTime ClusterDate { get; set; } // Date-only
    
    // Centroid (average of member locations)
    public double CentroidLatitude { get; set; }
    public double CentroidLongitude { get; set; }
    
    // Total service minutes for all locations in cluster
    public int TotalServiceMinutes { get; set; }
    
    // Number of locations in cluster
    public int LocationCount { get; set; }
    
    // Scheduling fields
    public DateTime? PlannedDate { get; set; } // Date-only, the day assigned by planner
    public bool IsLocked { get; set; } = false; // Lock to PlannedDate
    public DateTime? LockedAtUtc { get; set; }
    public string? LockedBy { get; set; } // Optional for audit
    
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    
    // Navigation
    public ICollection<PlanningClusterItem> Items { get; set; } = new List<PlanningClusterItem>();
}

