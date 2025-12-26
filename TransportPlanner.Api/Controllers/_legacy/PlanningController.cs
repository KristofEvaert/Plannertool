using Microsoft.AspNetCore.Mvc;
using TransportPlanner.Application.Interfaces;

namespace TransportPlanner.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PlanningController : ControllerBase
{
    private readonly IQueryService _queryService;
    private readonly IPlanningService _planningService;
    private readonly ILockService _lockService;

    public PlanningController(
        IQueryService queryService,
        IPlanningService planningService,
        ILockService lockService)
    {
        _queryService = queryService;
        _planningService = planningService;
        _lockService = lockService;
    }

    /// <summary>
    /// Gets the day overview with routes and unplanned items
    /// </summary>
    /// <param name="date">The date to get the overview for</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Day overview with routes</returns>
    [HttpGet("{date}")]
    public async Task<ActionResult> GetDayOverview(
        [FromRoute] DateOnly date,
        CancellationToken cancellationToken = default)
    {
        var overview = await _queryService.GetDayOverviewAsync(date, cancellationToken);
        return Ok(overview);
    }

    /// <summary>
    /// Gets the driver route for a specific day
    /// </summary>
    /// <param name="date">The date</param>
    /// <param name="driverId">The driver ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Driver day with route details</returns>
    [HttpGet("{date}/driver/{driverId}")]
    public async Task<ActionResult> GetDriverDay(
        [FromRoute] DateOnly date,
        [FromRoute] int driverId,
        CancellationToken cancellationToken = default)
    {
        var driverDay = await _queryService.GetDriverDayAsync(driverId, date, cancellationToken);
        return Ok(driverDay);
    }

    /// <summary>
    /// Generates a plan for the specified date range
    /// </summary>
    /// <param name="from">Start date</param>
    /// <param name="days">Number of days to plan</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Success result</returns>
    [HttpPost("generate")]
    public async Task<ActionResult> GeneratePlan(
        [FromQuery] DateOnly from,
        [FromQuery] int days = 14,
        CancellationToken cancellationToken = default)
    {
        await _planningService.GeneratePlanAsync(from, days, cancellationToken);
        return Ok(new { message = "Plan generation started" });
    }

    /// <summary>
    /// Locks a planning day
    /// </summary>
    /// <param name="date">The date to lock</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Success result</returns>
    [HttpPost("{date}/lock")]
    public async Task<ActionResult> LockDay(
        [FromRoute] DateOnly date,
        CancellationToken cancellationToken = default)
    {
        await _lockService.LockDayAsync(date, cancellationToken);
        return Ok(new { message = $"Day {date} locked" });
    }

    /// <summary>
    /// Unlocks a planning day
    /// </summary>
    /// <param name="date">The date to unlock</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Success result</returns>
    [HttpPost("{date}/unlock")]
    public async Task<ActionResult> UnlockDay(
        [FromRoute] DateOnly date,
        CancellationToken cancellationToken = default)
    {
        await _lockService.UnlockDayAsync(date, cancellationToken);
        return Ok(new { message = $"Day {date} unlocked" });
    }
}

