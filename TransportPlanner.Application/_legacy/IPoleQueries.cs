using TransportPlanner.Application.DTOs;

namespace TransportPlanner.Application.Interfaces;

public interface IPoleQueries
{
    Task<PagedResult<PoleListItemDto>> GetPolesAsync(
        DateTime? from,
        DateTime? to,
        string? status,
        bool? hasFixedDate,
        string? search,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);
}

