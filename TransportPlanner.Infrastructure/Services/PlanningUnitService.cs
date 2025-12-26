using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TransportPlanner.Application.Services;
using TransportPlanner.Domain.Entities;
using TransportPlanner.Infrastructure.Data;

namespace TransportPlanner.Infrastructure.Services;

public class PlanningUnitService : IPlanningUnitService
{
    private readonly TransportPlannerDbContext _dbContext;
    private readonly ILogger<PlanningUnitService> _logger;

    public PlanningUnitService(
        TransportPlannerDbContext dbContext,
        ILogger<PlanningUnitService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<List<PlanningUnit>> BuildUnitsAsync(
        IReadOnlyList<PlanningCluster> clusters,
        IReadOnlyList<ServiceLocation> unclusteredLocations,
        CancellationToken cancellationToken = default)
    {
        var units = new List<PlanningUnit>();

        // Add cluster units
        foreach (var cluster in clusters)
        {
            // Load items if not already loaded
            if (!cluster.Items.Any())
            {
                await _dbContext.Entry(cluster)
                    .Collection(c => c.Items)
                    .LoadAsync(cancellationToken);
            }

            var centroid = CalculateClusterCentroid(cluster);
            var serviceMinutes = CalculateClusterServiceMinutes(cluster);

            // ClusterDate = MIN(OrderDate) of items
            // OrderDate = PriorityDate ?? DueDate for ServiceLocation
            var clusterDate = await GetClusterDateAsync(cluster, cancellationToken);

            units.Add(new PlanningUnit
            {
                UnitId = $"C:{cluster.Id}",
                PriorityDate = DateOnly.FromDateTime(clusterDate),
                CentroidLatitude = centroid.latitude,
                CentroidLongitude = centroid.longitude,
                ServiceMinutes = serviceMinutes,
                IsCluster = true,
                ClusterId = cluster.Id,
                IsLocked = cluster.IsLocked,
                LockedDate = cluster.PlannedDate.HasValue 
                    ? DateOnly.FromDateTime(cluster.PlannedDate.Value) 
                    : null
            });
        }

        // Add unclustered location units
        foreach (var location in unclusteredLocations)
        {
            // OrderDate = PriorityDate ?? DueDate
            var orderDate = location.PriorityDate?.Date ?? location.DueDate.Date;

            units.Add(new PlanningUnit
            {
                UnitId = $"L:{location.Id}",
                PriorityDate = DateOnly.FromDateTime(orderDate),
                CentroidLatitude = location.Latitude ?? 0,
                CentroidLongitude = location.Longitude ?? 0,
                ServiceMinutes = location.ServiceMinutes,
                IsCluster = false,
                ServiceLocationId = location.Id,
                IsLocked = false,
                LockedDate = null
            });
        }

        // Sort by PriorityDate ascending (earliest first)
        return units.OrderBy(u => u.PriorityDate).ToList();
    }

    public (double latitude, double longitude) CalculateClusterCentroid(PlanningCluster cluster)
    {
        if (!cluster.Items.Any())
        {
            return (cluster.CentroidLatitude, cluster.CentroidLongitude);
        }

        // Load service locations if needed
        var locations = cluster.Items
            .Select(item => item.ServiceLocation)
            .Where(sl => sl != null)
            .ToList();

        if (!locations.Any())
        {
            return (cluster.CentroidLatitude, cluster.CentroidLongitude);
        }

        var avgLat = locations.Average(sl => sl!.Latitude ?? 0);
        var avgLon = locations.Average(sl => sl!.Longitude ?? 0);

        return (avgLat, avgLon);
    }

    public int CalculateClusterServiceMinutes(PlanningCluster cluster)
    {
        if (!cluster.Items.Any())
        {
            return cluster.TotalServiceMinutes;
        }

        return cluster.Items
            .Select(item => item.ServiceLocation)
            .Where(sl => sl != null)
            .Sum(sl => sl!.ServiceMinutes);
    }

    private async Task<DateTime> GetClusterDateAsync(
        PlanningCluster cluster,
        CancellationToken cancellationToken)
    {
        // If cluster has ClusterDate set, use it
        if (cluster.ClusterDate != default)
        {
            return cluster.ClusterDate;
        }

        // Otherwise calculate from items: MIN(OrderDate) where OrderDate = PriorityDate ?? DueDate
        if (!cluster.Items.Any())
        {
            await _dbContext.Entry(cluster)
                .Collection(c => c.Items)
                .LoadAsync(cancellationToken);
        }

        var locations = cluster.Items
            .Select(item => item.ServiceLocation)
            .Where(sl => sl != null)
            .ToList();

        if (!locations.Any())
        {
            return DateTime.Today;
        }

        var minDate = locations
            .Select(sl => sl!.PriorityDate?.Date ?? sl.DueDate.Date)
            .Min();

        return minDate;
    }
}

