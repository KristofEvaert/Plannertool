namespace TransportPlanner.Application.Services;

/// <summary>
/// Service for calculating travel distances and times with rush hour multipliers.
/// </summary>
public interface ITravelTimeService
{
    /// <summary>
    /// Calculates the distance in kilometers between two coordinates using Haversine formula.
    /// </summary>
    double GetKm(double lat1, double lon1, double lat2, double lon2);

    /// <summary>
    /// Calculates travel time in minutes based on distance and departure time.
    /// Rush hours (07:00-09:30 and 16:00-19:00) use 2 min/km, otherwise 1 min/km.
    /// </summary>
    /// <param name="km">Distance in kilometers</param>
    /// <param name="departureTime">Departure time of day</param>
    /// <returns>Travel time in minutes (rounded up)</returns>
    int GetTravelMinutes(double km, TimeSpan departureTime);

    /// <summary>
    /// Calculates travel time in minutes based on distance and departure time.
    /// Uses a fixed multiplier based on whether the departure time is in rush hour.
    /// </summary>
    /// <param name="km">Distance in kilometers</param>
    /// <param name="departureTime">Departure time of day</param>
    /// <returns>Travel time in minutes (rounded up)</returns>
    int GetTravelMinutes(double km, DateTime departureTime);
}

