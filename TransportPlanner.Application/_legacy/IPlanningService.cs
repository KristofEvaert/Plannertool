namespace TransportPlanner.Application.Interfaces;

public interface IPlanningService
{
    Task GeneratePlanAsync(DateOnly from, int days, CancellationToken cancellationToken = default);
}

