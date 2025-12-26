using TransportPlanner.Application.DTOs.Plan;

namespace TransportPlanner.Application.Interfaces;

public interface IDriverPlanQueries
{
    Task<DriverDayDto?> GetDriverDayAsync(DateTime date, int driverId, CancellationToken cancellationToken = default);
}

