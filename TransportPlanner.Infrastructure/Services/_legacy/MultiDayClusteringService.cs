using TransportPlanner.Domain;
using TransportPlanner.Domain.Entities;

namespace TransportPlanner.Infrastructure.Services;

/// <summary>
/// Service that clusters poles across multiple days to avoid visiting the same location on consecutive days.
/// </summary>
public class MultiDayClusteringService
{
    private const double ClusterRadiusKm = 2.0; // Poles within 2km are considered a cluster
    private const double EarthRadiusKm = 6371.0;

    /// <summary>
    /// Groups poles into clusters based on geographic proximity.
    /// </summary>
    public List<PoleCluster> ClusterPoles(List<PoleWithDate> poles)
    {
        var clusters = new List<PoleCluster>();
        var unassignedPoles = new List<PoleWithDate>(poles);

        while (unassignedPoles.Any())
        {
            var seedPole = unassignedPoles[0];
            var cluster = new PoleCluster
            {
                CenterLatitude = (double)seedPole.Pole.Latitude,
                CenterLongitude = (double)seedPole.Pole.Longitude,
                Poles = new List<PoleWithDate> { seedPole }
            };

            unassignedPoles.RemoveAt(0);

            // Find all poles within cluster radius
            for (int i = unassignedPoles.Count - 1; i >= 0; i--)
            {
                var pole = unassignedPoles[i];
                var distance = CalculateDistance(
                    (double)seedPole.Pole.Latitude,
                    (double)seedPole.Pole.Longitude,
                    (double)pole.Pole.Latitude,
                    (double)pole.Pole.Longitude);

                if (distance <= ClusterRadiusKm)
                {
                    cluster.Poles.Add(pole);
                    unassignedPoles.RemoveAt(i);
                }
            }

            // Update cluster center to be the average of all poles
            if (cluster.Poles.Count > 1)
            {
                cluster.CenterLatitude = cluster.Poles.Average(p => (double)p.Pole.Latitude);
                cluster.CenterLongitude = cluster.Poles.Average(p => (double)p.Pole.Longitude);
            }

            clusters.Add(cluster);
        }

        return clusters;
    }

    /// <summary>
    /// Assigns poles from clusters to specific days, avoiding consecutive day visits to the same cluster.
    /// </summary>
    public Dictionary<DateTime, List<PoleWithDate>> AssignPolesToDays(
        List<PoleCluster> clusters,
        List<DateTime> availableDays)
    {
        var assignment = new Dictionary<DateTime, List<PoleWithDate>>();
        foreach (var day in availableDays)
        {
            assignment[day] = new List<PoleWithDate>();
        }

        // Track which clusters were visited on which days
        var clusterVisits = new Dictionary<int, List<DateTime>>();

        // Process clusters by size (larger clusters first to ensure they get assigned)
        var sortedClusters = clusters.OrderByDescending(c => c.Poles.Count).ToList();

        int clusterIndex = 0;
        foreach (var cluster in sortedClusters)
        {
            // Sort poles in cluster by priority: FixedDate first, then DueDate
            var sortedPoles = cluster.Poles
                .OrderBy(p => p.Pole.FixedDate.HasValue ? 0 : 1)
                .ThenBy(p => p.Pole.FixedDate ?? p.Pole.DueDate)
                .ToList();

            var clusterId = clusterIndex++; // Use index as unique cluster ID
            clusterVisits[clusterId] = new List<DateTime>();

            foreach (var pole in sortedPoles)
            {
                // Find best day for this pole
                var bestDay = FindBestDayForPole(
                    pole,
                    availableDays,
                    clusterId,
                    clusterVisits,
                    assignment);

                if (bestDay.HasValue)
                {
                    assignment[bestDay.Value].Add(pole);
                    if (!clusterVisits[clusterId].Contains(bestDay.Value))
                    {
                        clusterVisits[clusterId].Add(bestDay.Value);
                    }
                }
            }
        }

        return assignment;
    }

    private DateTime? FindBestDayForPole(
        PoleWithDate pole,
        List<DateTime> availableDays,
        int clusterId,
        Dictionary<int, List<DateTime>> clusterVisits,
        Dictionary<DateTime, List<PoleWithDate>> assignment)
    {
        // If pole has FixedDate, must assign to that day
        if (pole.Pole.FixedDate.HasValue)
        {
            var fixedDate = pole.Pole.FixedDate.Value.Date;
            if (availableDays.Contains(fixedDate))
            {
                return fixedDate;
            }
            return null; // Fixed date not in available days
        }

        // Find days where this pole can be assigned (DueDate <= day)
        var eligibleDays = availableDays
            .Where(day => day >= pole.Pole.DueDate.Date)
            .OrderBy(day => day)
            .ToList();

        if (!eligibleDays.Any())
        {
            return null; // No eligible days
        }

        // Prefer days where:
        // 1. Cluster hasn't been visited recently (avoid consecutive days)
        // 2. Earlier days (to avoid delays)
        // 3. Days with fewer assignments (balance load)

        var bestDay = eligibleDays
            .OrderBy(day =>
            {
                var score = 0;
                
                // Penalty for visiting same cluster on consecutive days
                if (clusterVisits.ContainsKey(clusterId))
                {
                    var lastVisit = clusterVisits[clusterId].LastOrDefault();
                    if (lastVisit != default)
                    {
                        var daysSinceLastVisit = Math.Abs((day - lastVisit).Days);
                        if (daysSinceLastVisit == 1)
                        {
                            score += 1000; // Heavy penalty for consecutive days
                        }
                        else if (daysSinceLastVisit == 2)
                        {
                            score += 100; // Medium penalty for day after next
                        }
                    }
                }

                // Prefer earlier days (smaller score is better)
                score += (day - eligibleDays[0]).Days * 10;

                // Prefer days with fewer assignments (balance load)
                score += assignment[day].Count;

                return score;
            })
            .First();

        return bestDay;
    }

    private static double CalculateDistance(double lat1, double lon1, double lat2, double lon2)
    {
        var dLat = DegreesToRadians(lat2 - lat1);
        var dLon = DegreesToRadians(lon2 - lon1);

        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(DegreesToRadians(lat1)) * Math.Cos(DegreesToRadians(lat2)) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return EarthRadiusKm * c;
    }

    private static double DegreesToRadians(double degrees)
    {
        return degrees * (Math.PI / 180.0);
    }
}

public class PoleCluster
{
    public double CenterLatitude { get; set; }
    public double CenterLongitude { get; set; }
    public List<PoleWithDate> Poles { get; set; } = new();
}

public class PoleWithDate
{
    public Pole Pole { get; set; } = null!;
    public DateTime EligibleFromDate { get; set; }
    public DateTime? MustBeOnDate { get; set; } // For FixedDate poles
}

