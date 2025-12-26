using TransportPlanner.Application.DTOs;

namespace TransportPlanner.Application.Interfaces;

public interface IPoleImportService
{
    Task<ImportPolesResultDto> ImportDueWithinAsync(int days, CancellationToken cancellationToken = default);
}
