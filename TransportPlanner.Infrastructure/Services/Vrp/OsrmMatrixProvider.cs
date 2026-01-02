using System.Net.Http.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace TransportPlanner.Infrastructure.Services.Vrp;

public class OsrmMatrixProvider : IMatrixProvider
{
    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _cache;
    private readonly ILogger<OsrmMatrixProvider> _logger;

    public OsrmMatrixProvider(HttpClient httpClient, IMemoryCache cache, ILogger<OsrmMatrixProvider> logger)
    {
        _httpClient = httpClient;
        _cache = cache;
        _logger = logger;
    }

    public async Task<MatrixResult> GetMatrixAsync(string cacheKey, IReadOnlyList<MatrixPoint> points, CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(cacheKey, out MatrixResult? cached) && cached != null)
        {
            return cached;
        }

        if (points.Count == 0)
        {
            return new MatrixResult(new int[0, 0], new double[0, 0]);
        }

        var coordString = string.Join(";", points.Select(p => $"{p.Longitude},{p.Latitude}"));
        var url = $"table/v1/driving/{coordString}?annotations=duration,distance";

        try
        {
            var response = await _httpClient.GetFromJsonAsync<OsrmTableResponse>(url, cancellationToken);
            var durations = response?.Durations;
            var distances = response?.Distances;

            if (durations == null || distances == null)
            {
                return CacheAndReturn(cacheKey, BuildFallback(points));
            }

            var size = points.Count;
            var travelMinutes = new int[size, size];
            var distanceKm = new double[size, size];

            for (var i = 0; i < size; i++)
            {
                for (var j = 0; j < size; j++)
                {
                    var seconds = durations[i][j];
                    var meters = distances[i][j];

                    if (!seconds.HasValue || !meters.HasValue)
                    {
                        var fallbackKm = HaversineKm(points[i], points[j]);
                        var fallbackMinutes = EstimateMinutes(fallbackKm);
                        travelMinutes[i, j] = fallbackMinutes;
                        distanceKm[i, j] = fallbackKm;
                        continue;
                    }

                    travelMinutes[i, j] = (int)Math.Round(seconds.Value / 60.0, MidpointRounding.AwayFromZero);
                    distanceKm[i, j] = meters.Value / 1000.0;
                }
            }

            return CacheAndReturn(cacheKey, new MatrixResult(travelMinutes, distanceKm));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OSRM table request failed, falling back to straight-line matrix.");
            return CacheAndReturn(cacheKey, BuildFallback(points));
        }
    }

    private MatrixResult CacheAndReturn(string cacheKey, MatrixResult result)
    {
        _cache.Set(cacheKey, result, new MemoryCacheEntryOptions
        {
            SlidingExpiration = TimeSpan.FromMinutes(15)
        });

        return result;
    }

    private static MatrixResult BuildFallback(IReadOnlyList<MatrixPoint> points)
    {
        var size = points.Count;
        var travelMinutes = new int[size, size];
        var distanceKm = new double[size, size];

        for (var i = 0; i < size; i++)
        {
            for (var j = 0; j < size; j++)
            {
                if (i == j)
                {
                    travelMinutes[i, j] = 0;
                    distanceKm[i, j] = 0;
                    continue;
                }

                var km = HaversineKm(points[i], points[j]);
                travelMinutes[i, j] = EstimateMinutes(km);
                distanceKm[i, j] = km;
            }
        }

        return new MatrixResult(travelMinutes, distanceKm);
    }

    private static double HaversineKm(MatrixPoint a, MatrixPoint b)
    {
        const double R = 6371.0;
        var dLat = DegreesToRadians(b.Latitude - a.Latitude);
        var dLon = DegreesToRadians(b.Longitude - a.Longitude);
        var lat1 = DegreesToRadians(a.Latitude);
        var lat2 = DegreesToRadians(b.Latitude);
        var h = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(lat1) * Math.Cos(lat2) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(h), Math.Sqrt(1 - h));
        return R * c;
    }

    private static int EstimateMinutes(double distanceKm)
    {
        var safeKm = Math.Max(0, distanceKm);
        return (int)Math.Round((safeKm / 50.0) * 60.0, MidpointRounding.AwayFromZero);
    }

    private static double DegreesToRadians(double degrees) => degrees * (Math.PI / 180.0);

    private sealed class OsrmTableResponse
    {
        public List<List<double?>>? Durations { get; set; }
        public List<List<double?>>? Distances { get; set; }

        public List<List<double?>>? durations
        {
            set => Durations = value;
        }

        public List<List<double?>>? distances
        {
            set => Distances = value;
        }
    }
}
