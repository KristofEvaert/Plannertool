using Microsoft.Extensions.Options;
using TransportPlanner.Application.Interfaces;
using TransportPlanner.Domain;

namespace TransportPlanner.Infrastructure.Services;

/// <summary>
/// Dummy implementation of travel time calculation using Haversine formula for great-circle distance.
/// This implementation uses a configurable average speed to convert distance to travel time.
/// </summary>
public class DummyTravelTimeService : ITravelTimeService
{
    private const double EarthRadiusKm = 6371.0; // Earth radius in kilometers
    private readonly double _averageSpeedKmPerHour;

    public DummyTravelTimeService(IOptions<TravelTimeOptions> options)
    {
        _averageSpeedKmPerHour = options.Value.AverageSpeedKmh;
    }

    /// <summary>
    /// Calculates travel time matrix using Haversine distance formula and average speed.
    /// </summary>
    public Task<int[,]> GetTravelTimeMatrixAsync(
        IReadOnlyList<GeoLocation> locations,
        CancellationToken cancellationToken = default)
    {
        var count = locations.Count;
        var matrix = new int[count, count];

        for (int i = 0; i < count; i++)
        {
            for (int j = 0; j < count; j++)
            {
                if (i == j)
                {
                    // Diagonal values are 0 (same location)
                    matrix[i, j] = 0;
                }
                else
                {
                    var distanceKm = CalculateHaversineDistance(
                        locations[i].Latitude,
                        locations[i].Longitude,
                        locations[j].Latitude,
                        locations[j].Longitude);

                    var travelTimeHours = distanceKm / _averageSpeedKmPerHour;
                    var travelTimeMinutes = (int)Math.Ceiling(travelTimeHours * 60);
                    matrix[i, j] = travelTimeMinutes;
                }
            }
        }

        return Task.FromResult(matrix);
    }

    /// <summary>
    /// Calculates travel time between two locations.
    /// </summary>
    public Task<int> GetTravelTimeAsync(
        GeoLocation from,
        GeoLocation to,
        CancellationToken cancellationToken = default)
    {
        var distanceKm = CalculateHaversineDistance(
            from.Latitude,
            from.Longitude,
            to.Latitude,
            to.Longitude);

        var travelTimeHours = distanceKm / _averageSpeedKmPerHour;
        var travelTimeMinutes = (int)Math.Ceiling(travelTimeHours * 60);

        return Task.FromResult(travelTimeMinutes);
    }

    /// <summary>
    /// Calculates the great-circle distance between two points on Earth using the Haversine formula.
    /// </summary>
    /// <param name="lat1">Latitude of the first point in degrees</param>
    /// <param name="lon1">Longitude of the first point in degrees</param>
    /// <param name="lat2">Latitude of the second point in degrees</param>
    /// <param name="lon2">Longitude of the second point in degrees</param>
    /// <returns>Distance in kilometers</returns>
    private static double CalculateHaversineDistance(double lat1, double lon1, double lat2, double lon2)
    {
        var dLat = DegreesToRadians(lat2 - lat1);
        var dLon = DegreesToRadians(lon2 - lon1);

        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(DegreesToRadians(lat1)) * Math.Cos(DegreesToRadians(lat2)) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        var distance = EarthRadiusKm * c;

        return distance;
    }

    private static double DegreesToRadians(double degrees)
    {
        return degrees * (Math.PI / 180.0);
    }
}

/// <summary>
/// Configuration options for travel time calculation.
/// </summary>
public class TravelTimeOptions
{
    public const string SectionName = "TravelTime";

    /// <summary>
    /// Average speed in kilometers per hour used for travel time calculation.
    /// Default: 60 km/h
    /// </summary>
    public double AverageSpeedKmh { get; set; } = 60;
}

