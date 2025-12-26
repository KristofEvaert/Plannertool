using TransportPlanner.Application.DTOs;
using TransportPlanner.Application.Interfaces;

namespace TransportPlanner.Application.Services;

public class QueryService : IQueryService
{
    public async Task<DayOverviewDto> GetDayOverviewAsync(DateOnly date, CancellationToken cancellationToken = default)
    {
        // TODO: Implement day overview query logic
        return new DayOverviewDto
        {
            Date = date,
            IsLocked = false,
            Routes = new List<RouteOverviewDto>()
        };
    }

    public async Task<DriverDayDto> GetDriverDayAsync(int driverId, DateOnly date, CancellationToken cancellationToken = default)
    {
        // TODO: Implement driver day query logic
        return new DriverDayDto
        {
            DriverId = driverId,
            DriverName = string.Empty,
            Date = date,
            IsLocked = false,
            Route = null
        };
    }
}

