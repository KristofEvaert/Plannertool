namespace TransportPlanner.Api.Services.Routing;

public record RouteLegResult(double DistanceKm, int DurationMinutes);

public record RoutePoint(double Lat, double Lng);

public record DrivingRouteResult(
    double TotalDistanceKm,
    int TotalDurationMinutes,
    IReadOnlyList<RouteLegResult> Legs,
    IReadOnlyList<RoutePoint> GeometryPoints);

public interface IRoutingService
{
    Task<RouteLegResult> GetDrivingLegAsync(
        double fromLat,
        double fromLng,
        double toLat,
        double toLng,
        CancellationToken cancellationToken = default);

    Task<DrivingRouteResult> GetDrivingRouteAsync(
        IReadOnlyList<RoutePoint> points,
        CancellationToken cancellationToken = default);
}


