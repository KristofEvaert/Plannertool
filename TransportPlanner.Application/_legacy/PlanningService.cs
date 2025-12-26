using TransportPlanner.Application.Interfaces;

namespace TransportPlanner.Application.Services;

public class PlanningService : IPlanningService
{
    public async Task GeneratePlanAsync(DateOnly from, int days, CancellationToken cancellationToken = default)
    {
        // TODO: Implement route generation logic (later with OR-Tools)
        await Task.CompletedTask;
    }
}

