using TransportPlanner.Application.Services;

namespace TransportPlanner.Infrastructure.Services;

/// <summary>
/// Implementation of travel time service using Haversine distance and rush hour multipliers.
/// </summary>
public class TravelTimeService : ITravelTimeService
{
    private const double EarthRadiusKm = 6371.0;
    
    // Rush hour windows
    private static readonly TimeSpan RushStart1 = new(7, 0, 0);   // 07:00
    private static readonly TimeSpan RushEnd1 = new(9, 30, 0);   // 09:30
    private static readonly TimeSpan RushStart2 = new(16, 0, 0); // 16:00
    private static readonly TimeSpan RushEnd2 = new(19, 0, 0);   // 19:00

    public double GetKm(double lat1, double lon1, double lat2, double lon2)
    {
        var dLat = DegreesToRadians(lat2 - lat1);
        var dLon = DegreesToRadians(lon2 - lon1);

        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(DegreesToRadians(lat1)) * Math.Cos(DegreesToRadians(lat2)) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return EarthRadiusKm * c;
    }

    public int GetTravelMinutes(double km, TimeSpan departureTime)
    {
        bool isRushHour = IsRushHour(departureTime);
        double minutesPerKm = isRushHour ? 2.0 : 1.0;
        return (int)Math.Ceiling(km * minutesPerKm);
    }

    public int GetTravelMinutes(double km, DateTime departureTime)
    {
        return GetTravelMinutes(km, departureTime.TimeOfDay);
    }

    private static bool IsRushHour(TimeSpan timeOfDay)
    {
        return (timeOfDay >= RushStart1 && timeOfDay < RushEnd1) ||
               (timeOfDay >= RushStart2 && timeOfDay < RushEnd2);
    }

    private static double DegreesToRadians(double degrees)
    {
        return degrees * (Math.PI / 180.0);
    }
}

