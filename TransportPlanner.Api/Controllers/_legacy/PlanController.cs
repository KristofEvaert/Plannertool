using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using TransportPlanner.Application.DTOs;
using TransportPlanner.Application.DTOs.Plan;
using TransportPlanner.Application.Interfaces;
using DayOverviewDto = TransportPlanner.Application.DTOs.Plan.DayOverviewDto;
using DriverDayDto = TransportPlanner.Application.DTOs.Plan.DriverDayDto;

namespace TransportPlanner.Api.Controllers;

[ApiController]
[Route("api/plan")]
public class PlanController : ControllerBase
{
    private readonly IPlanQueries _planQueries;
    private readonly IPlanLockService _planLockService;
    private readonly IDriverPlanQueries _driverPlanQueries;
    private readonly IPlanGenerationService _planGenerationService;
    private readonly IPlanDaySettingsService _planDaySettingsService;
    private readonly IValidator<SetExtraWorkMinutesRequest> _setExtraWorkMinutesValidator;

    public PlanController(
        IPlanQueries planQueries, 
        IPlanLockService planLockService,
        IDriverPlanQueries driverPlanQueries,
        IPlanGenerationService planGenerationService,
        IPlanDaySettingsService planDaySettingsService,
        IValidator<SetExtraWorkMinutesRequest> setExtraWorkMinutesValidator)
    {
        _planQueries = planQueries;
        _planLockService = planLockService;
        _driverPlanQueries = driverPlanQueries;
        _planGenerationService = planGenerationService;
        _planDaySettingsService = planDaySettingsService;
        _setExtraWorkMinutesValidator = setExtraWorkMinutesValidator;
    }

    /// <summary>
    /// Gets the day overview with routes and unplanned poles
    /// </summary>
    /// <param name="date">Date in format yyyy-MM-dd</param>
    /// <param name="horizonDays">Horizon days for backlog calculation (default 14, range 1-60)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Day overview</returns>
    [HttpGet("day/{date}")]
    [ProducesResponseType(typeof(DayOverviewDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<DayOverviewDto>> GetDayOverview(
        [FromRoute] string date,
        [FromQuery] int horizonDays = 14,
        CancellationToken cancellationToken = default)
    {
        if (!DateTime.TryParseExact(date, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var parsedDate))
        {
            return BadRequest(new { error = "Invalid date format. Use yyyy-MM-dd" });
        }

        if (horizonDays < 1 || horizonDays > 60)
        {
            return BadRequest(new { error = "horizonDays must be between 1 and 60" });
        }

        var overview = await _planQueries.GetDayOverviewAsync(parsedDate, horizonDays, cancellationToken);
        return Ok(overview);
    }

    /// <summary>
    /// Locks a planning day
    /// </summary>
    /// <param name="date">Date in format yyyy-MM-dd</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Lock status</returns>
    [HttpPost("day/{date}/lock")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> LockDay(
        [FromRoute] string date,
        CancellationToken cancellationToken = default)
    {
        if (!DateTime.TryParseExact(date, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var parsedDate))
        {
            return BadRequest(new { error = "Invalid date format. Use yyyy-MM-dd" });
        }

        await _planLockService.LockDayAsync(parsedDate, cancellationToken);
        return Ok(new { date = parsedDate.ToString("yyyy-MM-dd"), isLocked = true });
    }

    /// <summary>
    /// Unlocks a planning day
    /// </summary>
    /// <param name="date">Date in format yyyy-MM-dd</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Lock status</returns>
    [HttpPost("day/{date}/unlock")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> UnlockDay(
        [FromRoute] string date,
        CancellationToken cancellationToken = default)
    {
        if (!DateTime.TryParseExact(date, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var parsedDate))
        {
            return BadRequest(new { error = "Invalid date format. Use yyyy-MM-dd" });
        }

        await _planLockService.UnlockDayAsync(parsedDate, cancellationToken);
        return Ok(new { date = parsedDate.ToString("yyyy-MM-dd"), isLocked = false });
    }

    /// <summary>
    /// Gets the driver day detail with full route
    /// </summary>
    /// <param name="date">Date in format yyyy-MM-dd</param>
    /// <param name="driverId">Driver ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Driver day detail</returns>
    [HttpGet("day/{date}/drivers/{driverId}")]
    [ProducesResponseType(typeof(DriverDayDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DriverDayDto>> GetDriverDay(
        [FromRoute] string date,
        [FromRoute] int driverId,
        CancellationToken cancellationToken = default)
    {
        if (!DateTime.TryParseExact(date, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var parsedDate))
        {
            return BadRequest(new { error = "Invalid date format. Use yyyy-MM-dd" });
        }

        var driverDay = await _driverPlanQueries.GetDriverDayAsync(parsedDate, driverId, cancellationToken);
        
        if (driverDay == null)
        {
            return NotFound(new { error = "Driver not found" });
        }

        return Ok(driverDay);
    }

    /// <summary>
    /// Generates a plan for the specified date range
    /// </summary>
    /// <param name="request">Generate plan request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Generation result</returns>
    [HttpPost("generate")]
    [ProducesResponseType(typeof(GeneratePlanResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<GeneratePlanResultDto>> GeneratePlan(
        [FromBody] GeneratePlanRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Days < 1 || request.Days > 31)
        {
            return BadRequest(new { error = "Days must be between 1 and 31" });
        }

        var fromDate = request.FromDate.Date;
        var result = await _planGenerationService.GenerateAsync(fromDate, request.Days, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Generates a plan for a single day
    /// </summary>
    /// <param name="date">Date in format yyyy-MM-dd</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Generation result</returns>
    [HttpPost("generate/day/{date}")]
    [ProducesResponseType(typeof(GeneratePlanResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<GeneratePlanResultDto>> GenerateDay(
        [FromRoute] string date,
        CancellationToken cancellationToken = default)
    {
        if (!DateTime.TryParseExact(date, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var parsedDate))
        {
            return BadRequest(new { error = "Invalid date format. Use yyyy-MM-dd" });
        }

        var result = await _planGenerationService.GenerateDayAsync(parsedDate, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Gets the day settings (extra work minutes)
    /// </summary>
    /// <param name="date">Date in format yyyy-MM-dd</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Day settings</returns>
    [HttpGet("day/{date}/settings")]
    [ProducesResponseType(typeof(PlanDaySettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PlanDaySettingsDto>> GetDaySettings(
        [FromRoute] string date,
        CancellationToken cancellationToken = default)
    {
        if (!DateTime.TryParseExact(date, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var parsedDate))
        {
            return BadRequest(new { error = "Invalid date format. Use yyyy-MM-dd" });
        }

        var settings = await _planDaySettingsService.GetAsync(parsedDate, cancellationToken);
        return Ok(settings);
    }

    /// <summary>
    /// Sets the extra work minutes for a day
    /// </summary>
    /// <param name="date">Date in format yyyy-MM-dd</param>
    /// <param name="request">Set extra work minutes request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Updated day settings</returns>
    [HttpPost("day/{date}/settings")]
    [ProducesResponseType(typeof(PlanDaySettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PlanDaySettingsDto>> SetDaySettings(
        [FromRoute] string date,
        [FromBody] SetExtraWorkMinutesRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!DateTime.TryParseExact(date, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var parsedDate))
        {
            return BadRequest(new { error = "Invalid date format. Use yyyy-MM-dd" });
        }

        var validationResult = await _setExtraWorkMinutesValidator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            return Problem(
                detail: string.Join("; ", validationResult.Errors.Select(e => e.ErrorMessage)),
                statusCode: StatusCodes.Status400BadRequest,
                title: "Validation Error");
        }

        var settings = await _planDaySettingsService.SetAsync(parsedDate, request.ExtraWorkMinutes, cancellationToken);
        return Ok(settings);
    }
}

