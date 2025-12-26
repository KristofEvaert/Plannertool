using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace TransportPlanner.Api.Services.Routing;

// Uses OSRM public demo server. If you self-host OSRM later, just change BaseAddress in Program.cs.
public class OsrmRoutingService : IRoutingService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<OsrmRoutingService> _logger;

    public OsrmRoutingService(HttpClient httpClient, ILogger<OsrmRoutingService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<RouteLegResult> GetDrivingLegAsync(
        double fromLat,
        double fromLng,
        double toLat,
        double toLng,
        CancellationToken cancellationToken = default)
    {
        var result = await GetDrivingRouteAsync(
            new[]
            {
                new RoutePoint(fromLat, fromLng),
                new RoutePoint(toLat, toLng)
            },
            cancellationToken);

        // Single leg
        return result.Legs.FirstOrDefault() ?? new RouteLegResult(0, 0);
    }

    public async Task<DrivingRouteResult> GetDrivingRouteAsync(
        IReadOnlyList<RoutePoint> points,
        CancellationToken cancellationToken = default)
    {
        if (points.Count < 2)
        {
            return new DrivingRouteResult(0, 0, Array.Empty<RouteLegResult>(), Array.Empty<RoutePoint>());
        }

        // OSRM expects lon,lat; supports multiple coordinates.
        var coordString = string.Join(";", points.Select(p => $"{p.Lng},{p.Lat}"));
        var url = $"route/v1/driving/{coordString}?overview=full&geometries=geojson";

        try
        {
            var response = await _httpClient.GetFromJsonAsync<OsrmRouteResponse>(url, cancellationToken);
            var route = response?.Routes?.FirstOrDefault();
            if (route == null)
            {
                return new DrivingRouteResult(0, 0, Array.Empty<RouteLegResult>(), Array.Empty<RoutePoint>());
            }

            var totalKm = route.DistanceMeters / 1000.0;
            var totalMinutes = (int)Math.Round(route.DurationSeconds / 60.0);

            var legs = (route.Legs ?? new List<OsrmLeg>())
                .Select(l =>
                    new RouteLegResult(
                        l.DistanceMeters / 1000.0,
                        (int)Math.Round(l.DurationSeconds / 60.0)))
                .ToList();

            var geometryPoints = new List<RoutePoint>();
            var coords = route.Geometry?.Coordinates ?? new List<List<double>>();
            foreach (var c in coords)
            {
                if (c.Count >= 2)
                {
                    // geojson coordinate order: [lon, lat]
                    geometryPoints.Add(new RoutePoint(c[1], c[0]));
                }
            }

            return new DrivingRouteResult(
                totalKm,
                Math.Max(0, totalMinutes),
                legs,
                geometryPoints);
        }
        catch (Exception ex)
        {
            // Fallback (bird-flight) if OSRM is unreachable
            _logger.LogWarning(ex, "OSRM routing failed, falling back to straight-line distance.");

            var legs = new List<RouteLegResult>();
            var totalKm = 0.0;
            var totalMinutes = 0;

            for (var i = 0; i < points.Count - 1; i++)
            {
                var a = points[i];
                var b = points[i + 1];
                var km = HaversineKm(a.Lat, a.Lng, b.Lat, b.Lng);
                var minutes = (int)Math.Round((km / 50.0) * 60.0);
                legs.Add(new RouteLegResult(km, Math.Max(0, minutes)));
                totalKm += km;
                totalMinutes += minutes;
            }

            // Straight polyline through the input points
            return new DrivingRouteResult(totalKm, totalMinutes, legs, points.ToList());
        }
    }

    private static double HaversineKm(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371.0;
        var dLat = DegreesToRadians(lat2 - lat1);
        var dLon = DegreesToRadians(lon2 - lon1);
        var a =
            Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
            Math.Cos(DegreesToRadians(lat1)) * Math.Cos(DegreesToRadians(lat2)) *
            Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return R * c;
    }

    private static double DegreesToRadians(double deg) => deg * (Math.PI / 180.0);

    private sealed class OsrmRouteResponse
    {
        public List<OsrmRoute>? Routes { get; set; }
    }

    private sealed class OsrmRoute
    {
        // "distance" in meters
        public double DistanceMeters { get; set; }

        // "duration" in seconds
        public double DurationSeconds { get; set; }

        // Map JSON fields
        public double distance
        {
            set => DistanceMeters = value;
        }

        public double duration
        {
            set => DurationSeconds = value;
        }

        public List<OsrmLeg>? Legs { get; set; }

        public OsrmGeometry? Geometry { get; set; }
    }

    private sealed class OsrmLeg
    {
        public double DistanceMeters { get; set; }
        public double DurationSeconds { get; set; }

        public double distance
        {
            set => DistanceMeters = value;
        }

        public double duration
        {
            set => DurationSeconds = value;
        }
    }

    private sealed class OsrmGeometry
    {
        // geojson: coordinates are [lon, lat]
        public List<List<double>>? Coordinates { get; set; }

        public List<List<double>>? coordinates
        {
            set => Coordinates = value;
        }
    }
}


