namespace TransportPlanner.Domain.Entities;

public class PlanningClusterItem
{
    public int Id { get; set; }
    public int PlanningClusterId { get; set; }
    public int ServiceLocationId { get; set; }
    
    // Navigation
    public PlanningCluster PlanningCluster { get; set; } = null!;
    public ServiceLocation ServiceLocation { get; set; } = null!;
}

