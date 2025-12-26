using TransportPlanner.Application.DTOs;

namespace TransportPlanner.Application.Interfaces;

public interface IPlanGenerationService
{
    Task<GeneratePlanResultDto> GenerateAsync(DateTime fromDate, int days, CancellationToken cancellationToken = default);
    Task<GeneratePlanResultDto> GenerateDayAsync(DateTime date, CancellationToken cancellationToken = default);
}

