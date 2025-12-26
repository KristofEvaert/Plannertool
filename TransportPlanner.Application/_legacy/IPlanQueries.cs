using TransportPlanner.Application.DTOs.Plan;

namespace TransportPlanner.Application.Interfaces;

public interface IPlanQueries
{
    Task<DayOverviewDto> GetDayOverviewAsync(DateTime date, int horizonDays = 14, CancellationToken cancellationToken = default);
}

