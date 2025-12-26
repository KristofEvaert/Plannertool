using Microsoft.AspNetCore.Mvc;
using TransportPlanner.Application.DTOs;
using TransportPlanner.Application.Interfaces;

namespace TransportPlanner.Api.Controllers;

[ApiController]
[Route("api/routes")]
public class RoutesController : ControllerBase
{
    private readonly IRouteExecutionService _routeExecutionService;

    public RoutesController(IRouteExecutionService routeExecutionService)
    {
        _routeExecutionService = routeExecutionService;
    }

    /// <summary>
    /// Starts a route
    /// </summary>
    /// <param name="routeId">Route ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Route action result</returns>
    [HttpPost("{routeId}/start")]
    [ProducesResponseType(typeof(RouteActionResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RouteActionResultDto>> StartRoute(
        [FromRoute] int routeId,
        CancellationToken cancellationToken = default)
    {
        var result = await _routeExecutionService.StartRouteAsync(routeId, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Marks a stop as arrived
    /// </summary>
    /// <param name="routeId">Route ID</param>
    /// <param name="stopId">Stop ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Stop action result</returns>
    [HttpPost("{routeId}/stops/{stopId}/arrive")]
    [ProducesResponseType(typeof(StopActionResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<StopActionResultDto>> ArriveStop(
        [FromRoute] int routeId,
        [FromRoute] int stopId,
        CancellationToken cancellationToken = default)
    {
        var result = await _routeExecutionService.ArriveStopAsync(routeId, stopId, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Marks a stop as completed
    /// </summary>
    /// <param name="routeId">Route ID</param>
    /// <param name="stopId">Stop ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Stop action result</returns>
    [HttpPost("{routeId}/stops/{stopId}/complete")]
    [ProducesResponseType(typeof(StopActionResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<StopActionResultDto>> CompleteStop(
        [FromRoute] int routeId,
        [FromRoute] int stopId,
        CancellationToken cancellationToken = default)
    {
        var result = await _routeExecutionService.CompleteStopAsync(routeId, stopId, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Adds a note to a stop
    /// </summary>
    /// <param name="routeId">Route ID</param>
    /// <param name="stopId">Stop ID</param>
    /// <param name="request">Note request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Stop action result</returns>
    [HttpPost("{routeId}/stops/{stopId}/note")]
    [ProducesResponseType(typeof(StopActionResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<StopActionResultDto>> AddStopNote(
        [FromRoute] int routeId,
        [FromRoute] int stopId,
        [FromBody] AddStopNoteRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await _routeExecutionService.AddStopNoteAsync(routeId, stopId, request.Note, cancellationToken);
        return Ok(result);
    }
}

