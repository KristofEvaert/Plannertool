using TransportPlanner.Application.DTOs;

namespace TransportPlanner.Application.Interfaces;

public interface IQueryService
{
    Task<DayOverviewDto> GetDayOverviewAsync(DateOnly date, CancellationToken cancellationToken = default);
    Task<DriverDayDto> GetDriverDayAsync(int driverId, DateOnly date, CancellationToken cancellationToken = default);
}

