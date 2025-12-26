using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Security.Claims;
using TransportPlanner.Api.Services.Routing;
using TransportPlanner.Application.DTOs;
using TransportPlanner.Domain.Entities;
using TransportPlanner.Infrastructure.Data;
using TransportPlanner.Infrastructure.Identity;
using RouteEntity = TransportPlanner.Domain.Entities.Route;

namespace TransportPlanner.Api.Controllers;

[ApiController]
[Route("api/routes")]
[Authorize]
public class RoutesController : ControllerBase
{
    private readonly TransportPlannerDbContext _dbContext;
    private readonly ILogger<RoutesController> _logger;
    private readonly IRoutingService _routing;

    public RoutesController(
        TransportPlannerDbContext dbContext,
        ILogger<RoutesController> logger,
        IRoutingService routing)
    {
        _dbContext = dbContext;
        _logger = logger;
        _routing = routing;
    }

    private Guid? CurrentUserId =>
        Guid.TryParse(User.FindFirstValue("uid"), out var id) ? id : null;

    private bool IsDriver => User.IsInRole(AppRoles.Driver);
    private bool IsStaff => User.IsInRole(AppRoles.SuperAdmin) || User.IsInRole(AppRoles.Admin) || User.IsInRole(AppRoles.Planner);
    private bool IsSuperAdmin => User.IsInRole(AppRoles.SuperAdmin);
    private int? CurrentOwnerId => int.TryParse(User.FindFirstValue("ownerId"), out var id) ? id : null;
    private bool CanAccessOwner(int ownerId) => IsSuperAdmin || (CurrentOwnerId.HasValue && CurrentOwnerId.Value == ownerId);

    private async Task<Driver?> GetCurrentDriverAsync(CancellationToken cancellationToken)
    {
        var uid = CurrentUserId;
        if (uid == null) return null;
        return await _dbContext.Drivers.FirstOrDefaultAsync(d => d.UserId == uid, cancellationToken);
    }

    [HttpGet("driver-day")]
    [ProducesResponseType(typeof(RouteDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<RouteDto?>> GetDriverDayRoute(
        [FromQuery] DateTime date,
        [FromQuery] Guid driverToolId,
        [FromQuery] int ownerId,
        [FromQuery] bool includeGeometry = true,
        CancellationToken cancellationToken = default)
    {
        if (IsDriver)
        {
            var currentDriver = await GetCurrentDriverAsync(cancellationToken);
            if (currentDriver == null)
            {
                return Forbid();
            }
            driverToolId = currentDriver.ToolId;
            ownerId = currentDriver.OwnerId;
        }

        var driver = await _dbContext.Drivers
            .FirstOrDefaultAsync(d => d.ToolId == driverToolId, cancellationToken);

        if (driver == null)
        {
            return Ok(null);
        }

        if (IsDriver && driver.UserId != CurrentUserId)
        {
            return Forbid();
        }

        if (!IsDriver && !CanAccessOwner(ownerId))
        {
            return Forbid();
        }

        var route = await _dbContext.Routes
            .Include(r => r.Driver)
            .Include(r => r.Stops)
                .ThenInclude(s => s.ServiceLocation)
            .FirstOrDefaultAsync(r =>
                r.Date.Date == date.Date &&
                r.DriverId == driver.Id &&
                r.OwnerId == ownerId, cancellationToken);

        if (route == null)
        {
            return Ok(null);
        }

        var drivingRoute = includeGeometry && route.Stops.Count > 0
            ? await TryBuildDrivingRouteAsync(route, cancellationToken)
            : null;

        return Ok(ToDto(route, drivingRoute));
    }

    /// <summary>
    /// Gets routes for a specific date, driver, and owner (no service type)
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<RouteDto>), StatusCodes.Status200OK)]
    [Authorize(Policy = "RequireStaff")]
    public async Task<ActionResult<List<RouteDto>>> GetRoutes(
        [FromQuery] DateTime date,
        [FromQuery] Guid driverToolId,
        [FromQuery] int ownerId,
        CancellationToken cancellationToken = default)
    {
        if (!CanAccessOwner(ownerId))
        {
            return Forbid();
        }

        // Find driver by ToolId
        var driver = await _dbContext.Drivers
            .FirstOrDefaultAsync(d => d.ToolId == driverToolId, cancellationToken);
        if (driver == null)
        {
            return Ok(new List<RouteDto>()); // Return empty list if driver not found
        }

        if (driver.OwnerId != ownerId)
        {
            return Forbid();
        }

        var routes = await _dbContext.Routes
            .Include(r => r.Driver)
            .Include(r => r.Stops)
                .ThenInclude(s => s.ServiceLocation) // Include ServiceLocation to get ToolId
            .Where(r => r.Date.Date == date.Date &&
                       r.DriverId == driver.Id &&
                       r.OwnerId == ownerId)
            .OrderBy(r => r.Id)
            .ToListAsync(cancellationToken);

        var dtos = routes.Select(r => new RouteDto
        {
            Id = r.Id,
            Date = r.Date,
            OwnerId = r.OwnerId,
            ServiceTypeId = r.ServiceTypeId,
            DriverId = r.DriverId,
            DriverName = r.Driver.Name,
            DriverStartLatitude = r.Driver.StartLatitude ?? 0,
            DriverStartLongitude = r.Driver.StartLongitude ?? 0,
            TotalMinutes = r.TotalMinutes,
            TotalKm = r.TotalKm,
            Status = r.Status.ToString(),
            Stops = r.Stops
                .OrderBy(s => s.Sequence)
                .Select(s => new RouteStopDto
                {
                    Id = s.Id,
                    Sequence = s.Sequence,
                    ServiceLocationId = s.ServiceLocationId,
                    ServiceLocationToolId = s.ServiceLocation != null ? s.ServiceLocation.ToolId : (Guid?)null,
                    Name = s.ServiceLocation != null ? s.ServiceLocation.Name : $"Stop {s.Sequence}",
                    Latitude = s.Latitude,
                    Longitude = s.Longitude,
                    ServiceMinutes = s.ServiceMinutes,
                    ActualServiceMinutes = s.ActualServiceMinutes,
                    TravelKmFromPrev = s.TravelKmFromPrev,
                    TravelMinutesFromPrev = s.TravelMinutesFromPrev,
                    Status = s.Status.ToString(),
                    ArrivedAtUtc = s.ArrivedAt,
                    CompletedAtUtc = s.CompletedAt,
                    Note = s.Note,
                    DriverInstruction = s.ServiceLocation != null ? s.ServiceLocation.DriverInstruction : null
                })
                .ToList()
        }).ToList();

        return Ok(dtos);
    }

    /// <summary>
    /// Creates or updates a route (upsert by date, driver, service type, owner)
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(RouteDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [Authorize(Policy = "RequireStaff")]
    public async Task<ActionResult<RouteDto>> UpsertRoute(
        [FromBody] CreateRouteRequest request,
        CancellationToken cancellationToken = default)
    {
        // Validate driver exists by ToolId
        var driver = await _dbContext.Drivers
            .FirstOrDefaultAsync(d => d.ToolId == request.DriverToolId, cancellationToken);
        if (driver == null)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Validation Error",
                Detail = $"Driver with ToolId {request.DriverToolId} does not exist"
            });
        }

        if (!CanAccessOwner(request.OwnerId))
        {
            return Forbid();
        }

        if (driver.OwnerId != request.OwnerId)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Validation Error",
                Detail = "Driver does not belong to the requested owner"
            });
        }

        // Find existing route or create new (identified by Date, DriverId, OwnerId only)
        var existingRoute = await _dbContext.Routes
            .FirstOrDefaultAsync(r =>
                r.Date.Date == request.Date.Date &&
                r.DriverId == driver.Id &&
                r.OwnerId == request.OwnerId,
                cancellationToken);

        // Save route + stops atomically (no partial route saves).
        await using var tx = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            RouteEntity route;
            var nowUtc = DateTime.UtcNow;
            var previousServiceLocationIds = new List<int>();
            if (existingRoute != null)
            {
                route = existingRoute;
                route.Status = RouteStatus.Temp;

                previousServiceLocationIds = await _dbContext.RouteStops
                    .Where(rs => rs.RouteId == route.Id && rs.ServiceLocationId.HasValue)
                    .Select(rs => rs.ServiceLocationId!.Value)
                    .Distinct()
                    .ToListAsync(cancellationToken);

                // Delete existing stops via set-based delete to avoid optimistic concurrency errors
                // when multiple saves overlap (rapid clicking).
                await _dbContext.RouteStops
                    .Where(rs => rs.RouteId == route.Id)
                    .ExecuteDeleteAsync(cancellationToken);

                route.Stops.Clear();
            }
            else
            {
                route = new RouteEntity
                {
                    Date = request.Date.Date,
                    OwnerId = request.OwnerId,
                    ServiceTypeId = 0, // legacy column
                    DriverId = driver.Id,
                    Status = RouteStatus.Temp,
                    Driver = driver
                };
                _dbContext.Routes.Add(route);
                // Ensure we have a RouteId so we can de-duplicate stops across other routes.
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            _logger.LogInformation(
                "UpsertRoute: saving {StopCount} stops for DriverId={DriverId} OwnerId={OwnerId} Date={Date}",
                request.Stops.Count, driver.Id, request.OwnerId, request.Date.Date);

            // Resolve requested service locations up-front (we need their DB IDs for de-duplication).
            var resolvedStops = new List<(int Sequence, ServiceLocation ServiceLocation)>();
            foreach (var stopRequest in request.Stops.OrderBy(s => s.Sequence))
            {
                if (!stopRequest.ServiceLocationToolId.HasValue)
                {
                    continue;
                }

                var serviceLocation = await _dbContext.ServiceLocations
                    .FirstOrDefaultAsync(sl => sl.ToolId == stopRequest.ServiceLocationToolId.Value, cancellationToken);

                if (serviceLocation == null)
                {
                    _logger.LogWarning(
                        "UpsertRoute: could not resolve ServiceLocationToolId={ToolId} for Sequence={Sequence}. Skipping stop.",
                        stopRequest.ServiceLocationToolId, stopRequest.Sequence);
                    continue;
                }

                if (serviceLocation.OwnerId != request.OwnerId)
                {
                    _logger.LogWarning(
                        "UpsertRoute: ServiceLocationToolId={ToolId} is not part of OwnerId={OwnerId}. Skipping stop.",
                        stopRequest.ServiceLocationToolId, request.OwnerId);
                    continue;
                }

                resolvedStops.Add((stopRequest.Sequence, serviceLocation));
            }

            // Remove these service locations from ANY other routes (even other days), so a location
            // is only ever planned in one route at a time.
            var desiredServiceLocationIds = resolvedStops
                .Select(x => x.ServiceLocation.Id)
                .Distinct()
                .ToList();

            if (desiredServiceLocationIds.Count > 0)
            {
                var affectedRouteIds = await _dbContext.RouteStops
                    .Where(rs => rs.ServiceLocationId.HasValue
                        && desiredServiceLocationIds.Contains(rs.ServiceLocationId.Value)
                        && rs.RouteId != route.Id)
                    .Select(rs => rs.RouteId)
                    .Distinct()
                    .ToListAsync(cancellationToken);

                await _dbContext.RouteStops
                    .Where(rs => rs.ServiceLocationId.HasValue
                        && desiredServiceLocationIds.Contains(rs.ServiceLocationId.Value)
                        && rs.RouteId != route.Id)
                    .ExecuteDeleteAsync(cancellationToken);

                // Recalculate affected routes so their totals/geometry remain correct.
                foreach (var affectedRouteId in affectedRouteIds)
                {
                    var affectedRoute = await _dbContext.Routes
                        .Include(r => r.Driver)
                        .Include(r => r.Stops)
                        .FirstOrDefaultAsync(r => r.Id == affectedRouteId, cancellationToken);

                    if (affectedRoute == null)
                    {
                        continue;
                    }

                    var remainingStops = affectedRoute.Stops.OrderBy(s => s.Sequence).ToList();
                    if (remainingStops.Count == 0)
                    {
                        _dbContext.Routes.Remove(affectedRoute);
                        continue;
                    }

                    var affectedPoints = new List<RoutePoint>
                    {
                        new RoutePoint(affectedRoute.Driver.StartLatitude ?? 0, affectedRoute.Driver.StartLongitude ?? 0)
                    };
                    affectedPoints.AddRange(remainingStops.Select(s => new RoutePoint(s.Latitude, s.Longitude)));
                    affectedPoints.Add(new RoutePoint(affectedRoute.Driver.StartLatitude ?? 0, affectedRoute.Driver.StartLongitude ?? 0));

                    var affectedDrivingRoute = await _routing.GetDrivingRouteAsync(affectedPoints, cancellationToken);

                    for (var i = 0; i < remainingStops.Count; i++)
                    {
                        var leg = i < affectedDrivingRoute.Legs.Count ? affectedDrivingRoute.Legs[i] : new RouteLegResult(0, 0);
                        remainingStops[i].TravelKmFromPrev = (float)leg.DistanceKm;
                        remainingStops[i].TravelMinutesFromPrev = leg.DurationMinutes;
                    }

                    var affectedServiceMinutes = remainingStops.Sum(s => s.ServiceMinutes);
                    affectedRoute.TotalKm = (float)affectedDrivingRoute.TotalDistanceKm;
                    affectedRoute.TotalMinutes = affectedDrivingRoute.TotalDurationMinutes + affectedServiceMinutes;
                    affectedRoute.Status = RouteStatus.Temp;
                }
            }

            // Status sync (backend source of truth):
            // - Locations in the route => Planned
            // - Locations removed from THIS route => Open (only if they are not in any other route)
            if (desiredServiceLocationIds.Count > 0)
            {
                await _dbContext.ServiceLocations
                    // Never downgrade terminal states (Done/Cancelled) back to Planned by re-planning routes.
                    .Where(sl =>
                        desiredServiceLocationIds.Contains(sl.Id)
                        && sl.Status != ServiceLocationStatus.Done
                        && sl.Status != ServiceLocationStatus.Cancelled)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(sl => sl.Status, ServiceLocationStatus.Planned)
                        .SetProperty(sl => sl.UpdatedAtUtc, nowUtc), cancellationToken);
            }

            if (previousServiceLocationIds.Count > 0)
            {
                var removedFromThisRoute = previousServiceLocationIds
                    .Except(desiredServiceLocationIds)
                    .Distinct()
                    .ToList();

                if (removedFromThisRoute.Count > 0)
                {
                    // Only mark Open if not present in any route anymore.
                    var stillInAnyRoute = await _dbContext.RouteStops
                        .Where(rs => rs.ServiceLocationId.HasValue && removedFromThisRoute.Contains(rs.ServiceLocationId.Value))
                        .Select(rs => rs.ServiceLocationId!.Value)
                        .Distinct()
                        .ToListAsync(cancellationToken);

                    var openIds = removedFromThisRoute
                        .Except(stillInAnyRoute)
                        .Distinct()
                        .ToList();

                    if (openIds.Count > 0)
                    {
                        await _dbContext.ServiceLocations
                            // Only revert Planned -> Open. Never overwrite Done/Cancelled.
                            .Where(sl => openIds.Contains(sl.Id) && sl.Status == ServiceLocationStatus.Planned)
                            .ExecuteUpdateAsync(setters => setters
                                .SetProperty(sl => sl.Status, ServiceLocationStatus.Open)
                                .SetProperty(sl => sl.UpdatedAtUtc, nowUtc), cancellationToken);
                    }
                }
            }

            // Add stops to the current route (after de-duplication).
            foreach (var (sequence, serviceLocation) in resolvedStops.OrderBy(s => s.Sequence))
            {
                var stop = new RouteStop
                {
                    Route = route,
                    Sequence = sequence,
                    StopType = RouteStopType.Location,
                    ServiceLocationId = serviceLocation.Id,
                    Latitude = serviceLocation.Latitude ?? 0,
                    Longitude = serviceLocation.Longitude ?? 0,
                    ServiceMinutes = serviceLocation.ServiceMinutes,
                    // Will be set using road distance below
                    TravelKmFromPrev = 0,
                    TravelMinutesFromPrev = 0
                };

                route.Stops.Add(stop);
            }

            // Compute road distance/time + geometry for the whole route:
            // Driver start -> stops (in order) -> back to driver start.
            var orderedStops = route.Stops.OrderBy(s => s.Sequence).ToList();
            var points = new List<RoutePoint>
            {
                new RoutePoint(driver.StartLatitude ?? 0, driver.StartLongitude ?? 0)
            };
            points.AddRange(orderedStops.Select(s => new RoutePoint(s.Latitude, s.Longitude)));
            points.Add(new RoutePoint(driver.StartLatitude ?? 0, driver.StartLongitude ?? 0));

            var drivingRoute = await _routing.GetDrivingRouteAsync(points, cancellationToken);

            // Legs: start->stop1, stop1->stop2, ..., stopN->start
            for (var i = 0; i < orderedStops.Count; i++)
            {
                var leg = i < drivingRoute.Legs.Count ? drivingRoute.Legs[i] : new RouteLegResult(0, 0);
                orderedStops[i].TravelKmFromPrev = (float)leg.DistanceKm;
                orderedStops[i].TravelMinutesFromPrev = leg.DurationMinutes;
            }

            var totalServiceMinutes = orderedStops.Sum(s => s.ServiceMinutes);
            route.TotalKm = (float)drivingRoute.TotalDistanceKm;
            route.TotalMinutes = drivingRoute.TotalDurationMinutes + totalServiceMinutes;

            await _dbContext.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            // Re-load with all navigation properties needed for DTO mapping
            var loadedRoute = await _dbContext.Routes
                .Include(r => r.Driver)
                .Include(r => r.Stops)
                    .ThenInclude(s => s.ServiceLocation)
                .FirstAsync(r => r.Id == route.Id, cancellationToken);

            var dto = ToDto(loadedRoute, drivingRoute);

            return Ok(dto);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(cancellationToken);
            _logger.LogError(ex, "UpsertRoute failed: {Message}", ex.Message);

            return BadRequest(new ProblemDetails
            {
                Title = "Error saving route",
                Detail = $"Failed to save route: {ex.Message}. Inner exception: {ex.InnerException?.Message}"
            });
        }
    }

    /// <summary>
    /// Updates route status from Temp to Fixed (Save Day)
    /// </summary>
    [HttpPost("{routeId}/fix")]
    [ProducesResponseType(typeof(RouteDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RouteDto>> FixRoute(
        int routeId,
        CancellationToken cancellationToken = default)
    {
        var route = await _dbContext.Routes
            .Include(r => r.Driver)
            .Include(r => r.Stops)
            .FirstOrDefaultAsync(r => r.Id == routeId, cancellationToken);

        if (route == null)
        {
            return NotFound();
        }

        route.Status = RouteStatus.Fixed;
        await _dbContext.SaveChangesAsync(cancellationToken);

        var dto = new RouteDto
        {
            Id = route.Id,
            Date = route.Date,
            OwnerId = route.OwnerId,
            ServiceTypeId = route.ServiceTypeId,
            DriverId = route.DriverId,
            DriverName = route.Driver.Name,
            TotalMinutes = route.TotalMinutes,
            TotalKm = Convert.ToDouble(route.TotalKm), // Convert to ensure double
            Status = route.Status.ToString(),
            Stops = route.Stops
                .OrderBy(s => s.Sequence)
                .Select(s => new RouteStopDto
                {
                    Id = s.Id,
                    Sequence = s.Sequence,
                    ServiceLocationId = s.ServiceLocationId,
                    ServiceLocationToolId = s.ServiceLocation != null ? s.ServiceLocation.ToolId : (Guid?)null,
                    Name = s.ServiceLocation != null ? s.ServiceLocation.Name : $"Stop {s.Sequence}",
                    Latitude = Convert.ToDouble(s.Latitude), // Convert to ensure double
                    Longitude = Convert.ToDouble(s.Longitude), // Convert to ensure double
                    ServiceMinutes = s.ServiceMinutes,
                    TravelKmFromPrev = Convert.ToDouble(s.TravelKmFromPrev), // Convert to ensure double
                    TravelMinutesFromPrev = s.TravelMinutesFromPrev,
                    DriverInstruction = s.ServiceLocation != null ? s.ServiceLocation.DriverInstruction : null
                })
                .ToList()
        };

        return Ok(dto);
    }

    /// <summary>
    /// Fixes all Temp routes for a specific date and owner (Save Day for all drivers)
    /// </summary>
    [HttpPost("fix-day")]
    [ProducesResponseType(typeof(List<RouteDto>), StatusCodes.Status200OK)]
    [Authorize(Policy = "RequireStaff")]
    public async Task<ActionResult<List<RouteDto>>> FixDay(
        [FromQuery] DateTime date,
        [FromQuery] int ownerId,
        CancellationToken cancellationToken = default)
    {
        var routes = await _dbContext.Routes
            .Include(r => r.Driver)
            .Include(r => r.Stops)
            .Where(r => r.Date.Date == date.Date &&
                       r.OwnerId == ownerId &&
                       r.Status == RouteStatus.Temp)
            .ToListAsync(cancellationToken);

        foreach (var route in routes)
        {
            route.Status = RouteStatus.Fixed;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        var dtos = routes.Select(r => new RouteDto
        {
            Id = r.Id,
            Date = r.Date,
            OwnerId = r.OwnerId,
            ServiceTypeId = r.ServiceTypeId,
            DriverId = r.DriverId,
            DriverName = r.Driver.Name,
            TotalMinutes = r.TotalMinutes,
            TotalKm = Convert.ToDouble(r.TotalKm), // Convert to ensure double
            Status = r.Status.ToString(),
            Stops = r.Stops
                .OrderBy(s => s.Sequence)
                .Select(s => new RouteStopDto
                {
                    Id = s.Id,
                    Sequence = s.Sequence,
                    ServiceLocationId = s.ServiceLocationId,
                    ServiceLocationToolId = s.ServiceLocation != null ? s.ServiceLocation.ToolId : (Guid?)null,
                    Name = s.ServiceLocation != null ? s.ServiceLocation.Name : $"Stop {s.Sequence}",
                    Latitude = Convert.ToDouble(s.Latitude), // Convert to ensure double
                    Longitude = Convert.ToDouble(s.Longitude), // Convert to ensure double
                    ServiceMinutes = s.ServiceMinutes,
                    TravelKmFromPrev = Convert.ToDouble(s.TravelKmFromPrev), // Convert to ensure double
                    TravelMinutesFromPrev = s.TravelMinutesFromPrev,
                    DriverInstruction = s.ServiceLocation != null ? s.ServiceLocation.DriverInstruction : null
                })
                .ToList()
        }).ToList();

        return Ok(dtos);
    }

    [HttpPatch("stops/{routeStopId:int}")]
    [ProducesResponseType(typeof(RouteStopDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [Authorize(Roles = $"{AppRoles.SuperAdmin},{AppRoles.Admin},{AppRoles.Driver}")]
    public async Task<ActionResult<RouteStopDto>> UpdateRouteStop(
        [FromRoute] int routeStopId,
        [FromBody] UpdateRouteStopRequest request,
        CancellationToken cancellationToken = default)
    {
        var stop = await _dbContext.RouteStops
            .Include(s => s.Route)
                .ThenInclude(r => r.Driver)
            .Include(s => s.ServiceLocation)
            .FirstOrDefaultAsync(s => s.Id == routeStopId, cancellationToken);

        if (stop == null)
        {
            return NotFound();
        }

        if (!IsDriver && !CanAccessOwner(stop.Route.OwnerId))
        {
            return Forbid();
        }

        if (IsDriver)
        {
            var currentDriver = await GetCurrentDriverAsync(cancellationToken);
            if (currentDriver == null || stop.Route.DriverId != currentDriver.Id)
            {
                return Forbid();
            }

            var cutoff = stop.Route.Date.Date.AddDays(1).AddMinutes(-1); // 23:59 of route day (UTC)
            if (DateTime.UtcNow > cutoff)
            {
                return Forbid();
            }
        }

        var nowUtc = DateTime.UtcNow;

        if (request.Note != null)
        {
            stop.Note = request.Note;
        }

        if (request.ArrivedAtUtc.HasValue)
        {
            stop.ArrivedAt = request.ArrivedAtUtc.Value;
            if (stop.Status != RouteStopStatus.Completed)
            {
                stop.Status = RouteStopStatus.Arrived;
            }
        }

        if (request.CompletedAtUtc.HasValue)
        {
            stop.CompletedAt = request.CompletedAtUtc.Value;
            stop.Status = RouteStopStatus.Completed;
        }

        if (request.ActualServiceMinutes.HasValue)
        {
            stop.ActualServiceMinutes = request.ActualServiceMinutes.Value;

            // If the driver enters a duration without timestamps, treat it as completion.
            if (!stop.CompletedAt.HasValue)
            {
                stop.CompletedAt = nowUtc;
            }

            if (!stop.ArrivedAt.HasValue)
            {
                stop.ArrivedAt = stop.CompletedAt.Value.AddMinutes(-stop.ActualServiceMinutes.Value);
            }

            stop.Status = RouteStopStatus.Completed;
        }

        // If completed with timestamps but no explicit duration, compute it.
        if (request.Status != null &&
            Enum.TryParse<RouteStopStatus>(request.Status, ignoreCase: true, out var newStatus) &&
            newStatus == RouteStopStatus.NotVisited)
        {
            if (string.IsNullOrWhiteSpace(stop.Note) && string.IsNullOrWhiteSpace(request.Note))
            {
                return BadRequest(new { message = "Note is required when marking a stop as NotVisited" });
            }
            stop.Status = RouteStopStatus.NotVisited;
            stop.CompletedAt = null;
            stop.ActualServiceMinutes = null;
            stop.ArrivedAt = null;
            if (stop.ServiceLocation != null)
            {
                stop.ServiceLocation.Status = ServiceLocationStatus.NotVisited;
                stop.ServiceLocation.UpdatedAtUtc = nowUtc;
            }
        }
        else
        {
            if (stop.Status == RouteStopStatus.Completed
                && stop.ArrivedAt.HasValue
                && stop.CompletedAt.HasValue
                && !stop.ActualServiceMinutes.HasValue)
            {
                var minutes = (int)Math.Max(0, Math.Round((stop.CompletedAt.Value - stop.ArrivedAt.Value).TotalMinutes));
                stop.ActualServiceMinutes = minutes;
            }

            if (stop.Status == RouteStopStatus.Completed && stop.ServiceLocation != null)
            {
                stop.ServiceLocation.Status = ServiceLocationStatus.Done;
                stop.ServiceLocation.UpdatedAtUtc = nowUtc;
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        var dto = new RouteStopDto
        {
            Id = stop.Id,
            Sequence = stop.Sequence,
            ServiceLocationId = stop.ServiceLocationId,
            ServiceLocationToolId = stop.ServiceLocation?.ToolId,
            Name = stop.ServiceLocation?.Name,
            Latitude = stop.Latitude,
            Longitude = stop.Longitude,
            ServiceMinutes = stop.ServiceMinutes,
            ActualServiceMinutes = stop.ActualServiceMinutes,
            TravelKmFromPrev = stop.TravelKmFromPrev,
            TravelMinutesFromPrev = stop.TravelMinutesFromPrev,
            Status = stop.Status.ToString(),
            ArrivedAtUtc = stop.ArrivedAt,
            CompletedAtUtc = stop.CompletedAt,
            Note = stop.Note,
            DriverInstruction = stop.ServiceLocation != null ? stop.ServiceLocation.DriverInstruction : null
        };

        return Ok(dto);
    }

    private async Task<DrivingRouteResult?> TryBuildDrivingRouteAsync(RouteEntity route, CancellationToken cancellationToken)
    {
        try
        {
            var orderedStops = route.Stops.OrderBy(s => s.Sequence).ToList();
            var points = new List<RoutePoint>
            {
                new RoutePoint(route.Driver.StartLatitude ?? 0, route.Driver.StartLongitude ?? 0)
            };
            points.AddRange(orderedStops.Select(s => new RoutePoint(s.Latitude, s.Longitude)));
            points.Add(new RoutePoint(route.Driver.StartLatitude ?? 0, route.Driver.StartLongitude ?? 0));

            return await _routing.GetDrivingRouteAsync(points, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to build driving route geometry for RouteId={RouteId}", route.Id);
            return null;
        }
    }

    private static RouteDto ToDto(RouteEntity route, DrivingRouteResult? drivingRoute = null)
    {
        return new RouteDto
        {
            Id = route.Id,
            Date = route.Date,
            OwnerId = route.OwnerId,
            ServiceTypeId = route.ServiceTypeId,
            DriverId = route.DriverId,
            DriverName = route.Driver.Name,
            DriverStartLatitude = route.Driver.StartLatitude ?? 0,
            DriverStartLongitude = route.Driver.StartLongitude ?? 0,
            TotalMinutes = route.TotalMinutes,
            TotalKm = route.TotalKm,
            Status = route.Status.ToString(),
            Stops = route.Stops
                .OrderBy(s => s.Sequence)
                .Select(s => new RouteStopDto
                {
                    Id = s.Id,
                    Sequence = s.Sequence,
                    ServiceLocationId = s.ServiceLocationId,
                    ServiceLocationToolId = s.ServiceLocation != null ? s.ServiceLocation.ToolId : (Guid?)null,
                    Name = s.ServiceLocation != null ? s.ServiceLocation.Name : $"Stop {s.Sequence}",
                    Latitude = s.Latitude,
                    Longitude = s.Longitude,
                    ServiceMinutes = s.ServiceMinutes,
                    ActualServiceMinutes = s.ActualServiceMinutes,
                    TravelKmFromPrev = s.TravelKmFromPrev,
                    TravelMinutesFromPrev = s.TravelMinutesFromPrev,
                    Status = s.Status.ToString(),
                    ArrivedAtUtc = s.ArrivedAt,
                    CompletedAtUtc = s.CompletedAt,
                    Note = s.Note,
                    DriverInstruction = s.ServiceLocation != null ? s.ServiceLocation.DriverInstruction : null
                })
                .ToList(),
            Geometry = drivingRoute?.GeometryPoints
                .Select(p => new RouteGeometryPointDto { Lat = p.Lat, Lng = p.Lng })
                .ToList()
        };
    }
}

