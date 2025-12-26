using TransportPlanner.Application.DTOs;

namespace TransportPlanner.Application.Interfaces;

public interface IPlanDaySettingsService
{
    Task<PlanDaySettingsDto> GetAsync(DateTime date, CancellationToken cancellationToken = default);
    Task<PlanDaySettingsDto> SetAsync(DateTime date, int extraWorkMinutes, CancellationToken cancellationToken = default);
}

