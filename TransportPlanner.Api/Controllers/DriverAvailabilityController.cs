using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using TransportPlanner.Application.DTOs;
using TransportPlanner.Domain.Entities;
using TransportPlanner.Infrastructure.Data;
using TransportPlanner.Infrastructure.Identity;

namespace TransportPlanner.Api.Controllers;

[ApiController]
[Route("api/drivers/{toolId:guid}/availability")]
[Authorize(Policy = "RequireStaff")]
public class DriverAvailabilityController : ControllerBase
{
    private readonly TransportPlannerDbContext _dbContext;
    private bool IsSuperAdmin => User.IsInRole(AppRoles.SuperAdmin);
    private int? CurrentOwnerId => int.TryParse(User.FindFirstValue("ownerId"), out var id) ? id : null;

    public DriverAvailabilityController(TransportPlannerDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>
    /// Gets driver availability for a date range
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<DriverAvailabilityDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<List<DriverAvailabilityDto>>> GetAvailability(
        [FromRoute] Guid toolId,
        [FromQuery] DateTime from,
        [FromQuery] DateTime to,
        CancellationToken cancellationToken = default)
    {
        // Validate date range
        if (to < from)
        {
            return Problem(
                detail: "To date must be >= from date",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Validation Error");
        }

        // Find driver
        var driver = await _dbContext.Drivers
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.ToolId == toolId, cancellationToken);

        if (driver == null)
        {
            return NotFound(new { error = $"Driver with ToolId {toolId} not found" });
        }

        if (!IsSuperAdmin)
        {
            if (!CurrentOwnerId.HasValue || driver.OwnerId != CurrentOwnerId.Value)
            {
                return Forbid();
            }
        }

        var fromDate = from.Date;
        var toDate = to.Date;

        var availabilitiesData = await _dbContext.DriverAvailabilities
            .AsNoTracking()
            .Where(da => da.DriverId == driver.Id && da.Date >= fromDate && da.Date <= toDate)
            .OrderBy(da => da.Date)
            .ToListAsync(cancellationToken);

        var availabilities = availabilitiesData.Select(da => new DriverAvailabilityDto
        {
            Date = da.Date.ToString("yyyy-MM-dd"), // Ensure consistent date format
            StartMinuteOfDay = da.StartMinuteOfDay,
            EndMinuteOfDay = da.EndMinuteOfDay,
            AvailableMinutes = da.EndMinuteOfDay - da.StartMinuteOfDay
        }).ToList();

        return Ok(availabilities);
    }

    /// <summary>
    /// Upserts availability for a specific date
    /// </summary>
    [HttpPut("{date:datetime}")]
    [ProducesResponseType(typeof(DriverAvailabilityDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DriverAvailabilityDto>> UpsertAvailability(
        [FromRoute] Guid toolId,
        [FromRoute] DateTime date,
        [FromBody] UpsertAvailabilityRequest request,
        CancellationToken cancellationToken = default)
    {
        // Validation
        if (request.StartMinuteOfDay < 0 || request.StartMinuteOfDay > 1439)
        {
            return Problem(
                detail: "StartMinuteOfDay must be between 0 and 1439",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Validation Error");
        }

        if (request.EndMinuteOfDay < 1 || request.EndMinuteOfDay > 1440)
        {
            return Problem(
                detail: "EndMinuteOfDay must be between 1 and 1440",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Validation Error");
        }

        if (request.EndMinuteOfDay <= request.StartMinuteOfDay)
        {
            return Problem(
                detail: "EndMinuteOfDay must be greater than StartMinuteOfDay",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Validation Error");
        }

        // Find driver
        var driver = await _dbContext.Drivers
            .FirstOrDefaultAsync(d => d.ToolId == toolId, cancellationToken);

        if (driver == null)
        {
            return NotFound(new { error = $"Driver with ToolId {toolId} not found" });
        }

        if (!IsSuperAdmin)
        {
            if (!CurrentOwnerId.HasValue || driver.OwnerId != CurrentOwnerId.Value)
            {
                return Forbid();
            }
        }

        var dateOnly = date.Date;
        var now = DateTime.UtcNow;

        // Find existing or create new
        var availability = await _dbContext.DriverAvailabilities
            .FirstOrDefaultAsync(da => da.DriverId == driver.Id && da.Date == dateOnly, cancellationToken);

        var isNew = availability == null;
        if (availability == null)
        {
            availability = new DriverAvailability
            {
                DriverId = driver.Id,
                Date = dateOnly,
                StartMinuteOfDay = request.StartMinuteOfDay,
                EndMinuteOfDay = request.EndMinuteOfDay,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            _dbContext.DriverAvailabilities.Add(availability);
        }
        else
        {
            availability.StartMinuteOfDay = request.StartMinuteOfDay;
            availability.EndMinuteOfDay = request.EndMinuteOfDay;
            availability.UpdatedAtUtc = now;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        var dto = new DriverAvailabilityDto
        {
            Date = availability.Date.ToString("yyyy-MM-dd"), // Ensure consistent date format
            StartMinuteOfDay = availability.StartMinuteOfDay,
            EndMinuteOfDay = availability.EndMinuteOfDay,
            AvailableMinutes = availability.AvailableMinutes
        };

        return isNew ? CreatedAtAction(nameof(GetAvailability), new { toolId, from = dateOnly, to = dateOnly }, dto) : Ok(dto);
    }

    /// <summary>
    /// Deletes availability for a specific date
    /// </summary>
    [HttpDelete("{date:datetime}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteAvailability(
        [FromRoute] Guid toolId,
        [FromRoute] DateTime date,
        CancellationToken cancellationToken = default)
    {
        // Find driver
        var driver = await _dbContext.Drivers
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.ToolId == toolId, cancellationToken);

        if (driver == null)
        {
            return NotFound(new { error = $"Driver with ToolId {toolId} not found" });
        }

        var dateOnly = date.Date;

        var availability = await _dbContext.DriverAvailabilities
            .FirstOrDefaultAsync(da => da.DriverId == driver.Id && da.Date == dateOnly, cancellationToken);

        if (availability == null)
        {
            return NotFound(new { error = $"Availability for date {dateOnly:yyyy-MM-dd} not found" });
        }

        _dbContext.DriverAvailabilities.Remove(availability);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return NoContent();
    }
}
