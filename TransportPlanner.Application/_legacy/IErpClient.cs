using TransportPlanner.Application.DTOs;

namespace TransportPlanner.Application.Interfaces;

public interface IErpClient
{
    Task<List<ErpPoleDto>> GetPolesDueWithinAsync(int days, CancellationToken cancellationToken = default);
}

