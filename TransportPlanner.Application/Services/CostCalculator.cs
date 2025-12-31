namespace TransportPlanner.Application.Services;

public static class CostCalculator
{
    public static double CalculateTravelCost(
        double distanceKm,
        double travelMinutes,
        decimal fuelCostPerKm,
        decimal personnelCostPerHour)
    {
        var safeDistance = Math.Max(0, distanceKm);
        var safeMinutes = Math.Max(0, travelMinutes);

        var fuelCost = (double)(fuelCostPerKm * (decimal)safeDistance);
        var personnelCost = (double)(personnelCostPerHour * (decimal)(safeMinutes / 60.0));
        return fuelCost + personnelCost;
    }
}
