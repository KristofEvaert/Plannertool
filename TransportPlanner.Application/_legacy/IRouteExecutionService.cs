using TransportPlanner.Application.DTOs;

namespace TransportPlanner.Application.Interfaces;

public interface IRouteExecutionService
{
    Task<RouteActionResultDto> StartRouteAsync(int routeId, CancellationToken cancellationToken = default);
    Task<StopActionResultDto> ArriveStopAsync(int routeId, int stopId, CancellationToken cancellationToken = default);
    Task<StopActionResultDto> CompleteStopAsync(int routeId, int stopId, CancellationToken cancellationToken = default);
    Task<StopActionResultDto> AddStopNoteAsync(int routeId, int stopId, string note, CancellationToken cancellationToken = default);
}

