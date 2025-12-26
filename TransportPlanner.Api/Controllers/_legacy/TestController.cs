using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TransportPlanner.Application.DTOs;
using TransportPlanner.Application.Interfaces;
using TransportPlanner.Infrastructure.Data;

namespace TransportPlanner.Api.Controllers;

/// <summary>
/// Test controller for debugging and testing route generation
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class TestController : ControllerBase
{
    private readonly TransportPlannerDbContext _dbContext;
    private readonly IPlanGenerationService _planGenerationService;

    public TestController(
        TransportPlannerDbContext dbContext,
        IPlanGenerationService planGenerationService)
    {
        _dbContext = dbContext;
        _planGenerationService = planGenerationService;
    }

    /// <summary>
    /// Gets test data summary (drivers, poles, availability)
    /// </summary>
    [HttpGet("data-summary")]
    public async Task<ActionResult> GetDataSummary(CancellationToken cancellationToken = default)
    {
        var today = DateTime.Today;
        
        var driverCount = await _dbContext.Drivers.CountAsync(cancellationToken);
        var poleCount = await _dbContext.Poles.CountAsync(cancellationToken);
        var availabilityCount = await _dbContext.DriverAvailabilities.CountAsync(cancellationToken);
        
        var polesToday = await _dbContext.Poles
            .Where(p => p.DueDate <= today && p.Status != Domain.Entities.PoleStatus.Done && p.Status != Domain.Entities.PoleStatus.Cancelled)
            .CountAsync(cancellationToken);
        
        var polesFuture = await _dbContext.Poles
            .Where(p => p.DueDate > today)
            .CountAsync(cancellationToken);
        
        var availabilityToday = await _dbContext.DriverAvailabilities
            .Where(da => da.Date == today)
            .CountAsync(cancellationToken);
        
        var routesToday = await _dbContext.Routes
            .Where(r => r.Date == today)
            .CountAsync(cancellationToken);

        return Ok(new
        {
            drivers = driverCount,
            poles = new
            {
                total = poleCount,
                availableToday = polesToday,
                future = polesFuture
            },
            availability = new
            {
                total = availabilityCount,
                today = availabilityToday
            },
            routes = new
            {
                today = routesToday
            }
        });
    }

    /// <summary>
    /// Tests route generation for today
    /// </summary>
    [HttpPost("generate-today")]
    public async Task<ActionResult<GeneratePlanResultDto>> GenerateToday(CancellationToken cancellationToken = default)
    {
        var today = DateTime.Today;
        var result = await _planGenerationService.GenerateDayAsync(today, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Gets detailed information about what would be generated for a specific date
    /// </summary>
    [HttpGet("generation-info/{date}")]
    public async Task<ActionResult> GetGenerationInfo(
        [FromRoute] string date,
        CancellationToken cancellationToken = default)
    {
        if (!DateTime.TryParseExact(date, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var parsedDate))
        {
            return BadRequest(new { error = "Invalid date format. Use yyyy-MM-dd" });
        }

        var dateOnly = parsedDate.Date;

        // Load drivers
        var drivers = await _dbContext.Drivers
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        // Load driver availability for this day
        var availabilities = await _dbContext.DriverAvailabilities
            .AsNoTracking()
            .Where(da => da.Date == dateOnly)
            .ToListAsync(cancellationToken);

        var availableDrivers = drivers
            .Where(d => availabilities.Any(a => a.DriverId == d.Id))
            .ToList();

        // Get candidate poles for this day
        var allPoles = await _dbContext.Poles
            .AsNoTracking()
            .Where(p =>
                (p.FixedDate.HasValue && p.FixedDate.Value == dateOnly) ||
                (p.FixedDate == null && p.DueDate <= dateOnly && p.Status != Domain.Entities.PoleStatus.Done && p.Status != Domain.Entities.PoleStatus.Cancelled))
            .ToListAsync(cancellationToken);

        // Exclude already planned poles
        var routesForDate = await _dbContext.Routes
            .AsNoTracking()
            .Where(r => r.Date == dateOnly)
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);

        var plannedPoleIds = routesForDate.Any()
            ? await _dbContext.RouteStops
                .AsNoTracking()
                .Where(rs => routesForDate.Contains(rs.RouteId))
                .Select(rs => rs.PoleId)
                .Distinct()
                .ToListAsync(cancellationToken)
            : new List<int>();

        var candidatePoles = allPoles
            .Where(p => !plannedPoleIds.Contains(p.Id))
            .ToList();

        return Ok(new
        {
            date = dateOnly.ToString("yyyy-MM-dd"),
            drivers = new
            {
                total = drivers.Count,
                available = availableDrivers.Count,
                availableDrivers = availableDrivers.Select(d => new
                {
                    id = d.Id,
                    name = d.Name,
                    availability = availabilities.FirstOrDefault(a => a.DriverId == d.Id) != null
                        ? new
                        {
                            start = availabilities.First(a => a.DriverId == d.Id).StartTime,
                            end = availabilities.First(a => a.DriverId == d.Id).EndTime
                        }
                        : null
                })
            },
            poles = new
            {
                total = allPoles.Count,
                candidate = candidatePoles.Count,
                alreadyPlanned = plannedPoleIds.Count,
                candidatePoles = candidatePoles.Select(p => new
                {
                    id = p.Id,
                    serial = p.Serial,
                    dueDate = p.DueDate.ToString("yyyy-MM-dd"),
                    fixedDate = p.FixedDate?.ToString("yyyy-MM-dd"),
                    status = p.Status.ToString()
                })
            }
        });
    }
}

