namespace TransportPlanner.Infrastructure.Services;

public interface ITravelTimeModelService
{
    Task<double> EstimateMinutesAsync(
        DateTime date,
        int departureMinute,
        double distanceKm,
        double startLat,
        double startLng,
        double endLat,
        double endLng,
        CancellationToken cancellationToken = default);

    Task UpdateLearnedStatsAsync(
        DateTime date,
        int departureMinute,
        double distanceKm,
        double travelMinutes,
        double? stopServiceMinutes,
        double startLat,
        double startLng,
        double endLat,
        double endLng,
        CancellationToken cancellationToken = default);
}
