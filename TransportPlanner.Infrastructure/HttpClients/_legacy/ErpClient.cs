using Refit;
using TransportPlanner.Application.DTOs;
using TransportPlanner.Application.Interfaces;

namespace TransportPlanner.Infrastructure.HttpClients;

public class ErpClient : IErpClient
{
    private readonly IErpClientApi _api;

    public ErpClient(IErpClientApi api)
    {
        _api = api;
    }

    public async Task<List<ErpPoleDto>> GetPolesDueWithinAsync(int days, CancellationToken cancellationToken = default)
    {
        return await _api.GetPolesDueWithinAsync(days, cancellationToken);
    }
}

public interface IErpClientApi
{
    [Get("/api/poles/due-within")]
    Task<List<ErpPoleDto>> GetPolesDueWithinAsync(
        [Query] int days,
        CancellationToken cancellationToken = default);
}

