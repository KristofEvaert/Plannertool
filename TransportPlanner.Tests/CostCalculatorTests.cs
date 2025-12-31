using TransportPlanner.Application.Services;
using Xunit;

namespace TransportPlanner.Tests;

public class CostCalculatorTests
{
    [Fact]
    public void CalculateTravelCost_CombinesFuelAndPersonnel()
    {
        var cost = CostCalculator.CalculateTravelCost(
            distanceKm: 10,
            travelMinutes: 30,
            fuelCostPerKm: 0.2m,
            personnelCostPerHour: 20m);

        Assert.Equal(12.0, cost, 2);
    }
}
