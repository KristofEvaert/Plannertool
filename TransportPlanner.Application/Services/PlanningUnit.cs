namespace TransportPlanner.Application.Services;

/// <summary>
/// Represents a planning unit - either a cluster or a single unclustered location.
/// </summary>
public class PlanningUnit
{
    /// <summary>
    /// Unit identifier: "C:{clusterId}" for clusters or "L:{serviceLocationId}" for locations
    /// </summary>
    public string UnitId { get; set; } = string.Empty;

    /// <summary>
    /// Priority date for scheduling (earliest first)
    /// For clusters: ClusterDate = MIN(OrderDate) of items
    /// For locations: OrderDate = PriorityDate ?? DueDate
    /// </summary>
    public DateOnly PriorityDate { get; set; }

    /// <summary>
    /// Due date (latest possible execution date)
    /// </summary>
    public DateOnly DueDate { get; set; }

    /// <summary>
    /// Name of the service location (for ordering and display)
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Centroid latitude
    /// For clusters: average of member locations
    /// For locations: its own latitude
    /// </summary>
    public double CentroidLatitude { get; set; }

    /// <summary>
    /// Centroid longitude
    /// For clusters: average of member locations
    /// For locations: its own longitude
    /// </summary>
    public double CentroidLongitude { get; set; }

    /// <summary>
    /// Total service minutes
    /// For clusters: sum of member ServiceMinutes
    /// For locations: its ServiceMinutes
    /// </summary>
    public int ServiceMinutes { get; set; }

    /// <summary>
    /// True if this is a cluster, false if single location
    /// </summary>
    public bool IsCluster { get; set; }

    /// <summary>
    /// Cluster ID if IsCluster=true, null otherwise
    /// </summary>
    public int? ClusterId { get; set; }

    /// <summary>
    /// ServiceLocation ID if IsCluster=false, null otherwise
    /// </summary>
    public int? ServiceLocationId { get; set; }

    /// <summary>
    /// True if this cluster is locked to a specific date
    /// </summary>
    public bool IsLocked { get; set; }

    /// <summary>
    /// Locked planned date (if IsLocked=true)
    /// </summary>
    public DateOnly? LockedDate { get; set; }
}

