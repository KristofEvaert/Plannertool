using TransportPlanner.Domain.Entities;

namespace TransportPlanner.Application.Services;

/// <summary>
/// Service for building planning units from clusters and unclustered locations.
/// </summary>
public interface IPlanningUnitService
{
    /// <summary>
    /// Builds planning units from clusters and unclustered service locations.
    /// </summary>
    /// <param name="clusters">All clusters for the owner/service type</param>
    /// <param name="unclusteredLocations">Service locations not in any cluster</param>
    /// <returns>List of planning units sorted by PriorityDate</returns>
    Task<List<PlanningUnit>> BuildUnitsAsync(
        IReadOnlyList<PlanningCluster> clusters,
        IReadOnlyList<ServiceLocation> unclusteredLocations,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Calculates the centroid (average lat/lon) for a cluster.
    /// </summary>
    (double latitude, double longitude) CalculateClusterCentroid(PlanningCluster cluster);

    /// <summary>
    /// Calculates the total service minutes for a cluster.
    /// </summary>
    int CalculateClusterServiceMinutes(PlanningCluster cluster);
}

