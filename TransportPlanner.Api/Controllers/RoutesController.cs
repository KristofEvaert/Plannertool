using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Security.Claims;
using System.Text.Json;
using TransportPlanner.Api.Services.Routing;
using TransportPlanner.Application.DTOs;
using TransportPlanner.Domain.Entities;
using TransportPlanner.Infrastructure.Data;
using TransportPlanner.Infrastructure.Identity;
using TransportPlanner.Infrastructure.Services;
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
    private readonly IGeocodingService _geocodingService;
    private readonly ITravelTimeModelService _travelTimeModel;

    public RoutesController(
        TransportPlannerDbContext dbContext,
        ILogger<RoutesController> logger,
        IRoutingService routing,
        IGeocodingService geocodingService,
        ITravelTimeModelService travelTimeModel)
    {
        _dbContext = dbContext;
        _logger = logger;
        _routing = routing;
        _geocodingService = geocodingService;
        _travelTimeModel = travelTimeModel;
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
            StartAddress = r.StartAddress,
            StartLatitude = r.StartLatitude,
            StartLongitude = r.StartLongitude,
            EndAddress = r.EndAddress,
            EndLatitude = r.EndLatitude,
            EndLongitude = r.EndLongitude,
            WeightTemplateId = r.WeightTemplateId,
            TotalMinutes = r.TotalMinutes,
            TotalKm = r.TotalKm,
            Status = r.Status.ToString(),
            Stops = r.Stops
                .OrderBy(s => s.Sequence)
                .Select(ToStopDto)
                .ToList()
        }).ToList();

        return Ok(dtos);
    }

    /// <summary>
    /// Deletes a driver's route for a specific date and owner
    /// </summary>
    [HttpDelete("driver-day")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [Authorize(Policy = "RequireStaff")]
    public async Task<IActionResult> DeleteDriverDayRoute(
        [FromQuery] DateTime date,
        [FromQuery] Guid driverToolId,
        [FromQuery] int ownerId,
        CancellationToken cancellationToken = default)
    {
        if (!CanAccessOwner(ownerId))
        {
            return Forbid();
        }

        var driver = await _dbContext.Drivers
            .FirstOrDefaultAsync(d => d.ToolId == driverToolId, cancellationToken);

        if (driver == null)
        {
            return NoContent();
        }

        if (driver.OwnerId != ownerId)
        {
            return Forbid();
        }

        var route = await _dbContext.Routes
            .Include(r => r.Stops)
            .FirstOrDefaultAsync(r =>
                r.Date.Date == date.Date &&
                r.DriverId == driver.Id &&
                r.OwnerId == ownerId, cancellationToken);

        if (route == null)
        {
            return NoContent();
        }

        if (route.Status == RouteStatus.Fixed)
        {
            return Problem(
                detail: "Cannot delete a fixed route.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Validation Error");
        }

        var removedLocationIds = route.Stops
            .Where(s => s.ServiceLocationId.HasValue)
            .Select(s => s.ServiceLocationId!.Value)
            .Distinct()
            .ToList();

        _dbContext.RouteStops.RemoveRange(route.Stops);
        _dbContext.Routes.Remove(route);

        if (removedLocationIds.Count > 0)
        {
            var today = DateTime.UtcNow.Date;
            var stillInAnyRoute = await _dbContext.RouteStops
                .Where(rs => rs.ServiceLocationId.HasValue
                    && removedLocationIds.Contains(rs.ServiceLocationId.Value)
                    && rs.RouteId != route.Id
                    && rs.Route.Date >= today)
                .Select(rs => rs.ServiceLocationId!.Value)
                .Distinct()
                .ToListAsync(cancellationToken);

            var openIds = removedLocationIds.Except(stillInAnyRoute).ToList();
            if (openIds.Count > 0)
            {
                await _dbContext.ServiceLocations
                    .Where(sl => openIds.Contains(sl.Id))
                    .ExecuteUpdateAsync(s => s.SetProperty(sl => sl.Status, ServiceLocationStatus.Open), cancellationToken);
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Deletes all routes for a specific date and owner
    /// </summary>
    [HttpDelete("day")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [Authorize(Policy = "RequireStaff")]
    public async Task<IActionResult> DeleteDayRoutes(
        [FromQuery] DateTime date,
        [FromQuery] int ownerId,
        CancellationToken cancellationToken = default)
    {
        if (!CanAccessOwner(ownerId))
        {
            return Forbid();
        }

        var routes = await _dbContext.Routes
            .Include(r => r.Stops)
            .Where(r => r.Date.Date == date.Date && r.OwnerId == ownerId)
            .ToListAsync(cancellationToken);

        if (routes.Count == 0)
        {
            return Ok(new { deletedRoutes = 0, skippedFixed = 0 });
        }

        var fixedRoutes = routes.Where(r => r.Status == RouteStatus.Fixed).ToList();
        var deletableRoutes = routes.Where(r => r.Status != RouteStatus.Fixed).ToList();

        var removedLocationIds = deletableRoutes
            .SelectMany(r => r.Stops)
            .Where(s => s.ServiceLocationId.HasValue)
            .Select(s => s.ServiceLocationId!.Value)
            .Distinct()
            .ToList();

        foreach (var route in deletableRoutes)
        {
            _dbContext.RouteStops.RemoveRange(route.Stops);
            _dbContext.Routes.Remove(route);
        }

        if (removedLocationIds.Count > 0)
        {
            var today = DateTime.UtcNow.Date;
            var routeIds = routes.Select(r => r.Id).ToList();
            var stillInAnyRoute = await _dbContext.RouteStops
                .Where(rs => rs.ServiceLocationId.HasValue
                    && removedLocationIds.Contains(rs.ServiceLocationId.Value)
                    && !routeIds.Contains(rs.RouteId)
                    && rs.Route.Date >= today)
                .Select(rs => rs.ServiceLocationId!.Value)
                .Distinct()
                .ToListAsync(cancellationToken);

            var openIds = removedLocationIds.Except(stillInAnyRoute).ToList();
            if (openIds.Count > 0)
            {
                await _dbContext.ServiceLocations
                    .Where(sl => openIds.Contains(sl.Id))
                    .ExecuteUpdateAsync(s => s.SetProperty(sl => sl.Status, ServiceLocationStatus.Open), cancellationToken);
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new { deletedRoutes = deletableRoutes.Count, skippedFixed = fixedRoutes.Count });
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
            var previousStopToolIds = new List<Guid>();
            var wasInProgress = false;
            if (existingRoute != null)
            {
                route = existingRoute;
                wasInProgress = route.Status == RouteStatus.Started;
                route.Status = RouteStatus.Temp;

                previousServiceLocationIds = await _dbContext.RouteStops
                    .Where(rs => rs.RouteId == route.Id && rs.ServiceLocationId.HasValue)
                    .Select(rs => rs.ServiceLocationId!.Value)
                    .Distinct()
                    .ToListAsync(cancellationToken);

                previousStopToolIds = await _dbContext.RouteStops
                    .Where(rs => rs.RouteId == route.Id && rs.ServiceLocationId.HasValue)
                    .Join(_dbContext.ServiceLocations,
                        rs => rs.ServiceLocationId!.Value,
                        sl => sl.Id,
                        (rs, sl) => new { rs.Sequence, sl.ToolId })
                    .OrderBy(x => x.Sequence)
                    .Select(x => x.ToolId)
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

            route.WeightTemplateId = request.WeightTemplateId;

            var overrideError = await ApplyRouteOverridesAsync(route, request, cancellationToken);
            if (!string.IsNullOrEmpty(overrideError))
            {
                await tx.RollbackAsync(cancellationToken);
                return BadRequest(new ProblemDetails
                {
                    Title = "Validation Error",
                    Detail = overrideError
                });
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

            if (wasInProgress)
            {
                var newStopToolIds = resolvedStops
                    .OrderBy(x => x.Sequence)
                    .Select(x => x.ServiceLocation.ToolId)
                    .ToList();

                if (!previousStopToolIds.SequenceEqual(newStopToolIds))
                {
                    await CreateRouteChangeNotificationAsync(route, previousStopToolIds, newStopToolIds, nowUtc, cancellationToken);
                }
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

                    var affectedStart = ResolveRouteStart(affectedRoute, affectedRoute.Driver);
                    var affectedEnd = ResolveRouteEnd(affectedRoute, affectedRoute.Driver);
                    var affectedPoints = new List<RoutePoint>
                    {
                        new RoutePoint(affectedStart.Lat, affectedStart.Lng)
                    };
                    affectedPoints.AddRange(remainingStops.Select(s => new RoutePoint(s.Latitude, s.Longitude)));
                    affectedPoints.Add(new RoutePoint(affectedEnd.Lat, affectedEnd.Lng));

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
                    var today = DateTime.UtcNow.Date;
                    var stillInAnyRoute = await _dbContext.RouteStops
                        .Where(rs => rs.ServiceLocationId.HasValue
                            && removedFromThisRoute.Contains(rs.ServiceLocationId.Value)
                            && rs.Route.Date >= today)
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
            var startPoint = ResolveRouteStart(route, driver);
            var endPoint = ResolveRouteEnd(route, driver);
            var points = new List<RoutePoint>
            {
                new RoutePoint(startPoint.Lat, startPoint.Lng)
            };
            points.AddRange(orderedStops.Select(s => new RoutePoint(s.Latitude, s.Longitude)));
            points.Add(new RoutePoint(endPoint.Lat, endPoint.Lng));

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
                StartAddress = route.StartAddress,
                StartLatitude = route.StartLatitude,
                StartLongitude = route.StartLongitude,
                EndAddress = route.EndAddress,
                EndLatitude = route.EndLatitude,
                EndLongitude = route.EndLongitude,
                WeightTemplateId = route.WeightTemplateId,
                TotalMinutes = route.TotalMinutes,
                TotalKm = route.TotalKm,
                Status = route.Status.ToString(),
                Stops = route.Stops
                .OrderBy(s => s.Sequence)
                .Select(ToStopDto)
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
                StartAddress = r.StartAddress,
                StartLatitude = r.StartLatitude,
                StartLongitude = r.StartLongitude,
                EndAddress = r.EndAddress,
                EndLatitude = r.EndLatitude,
                EndLongitude = r.EndLongitude,
                WeightTemplateId = r.WeightTemplateId,
                TotalMinutes = r.TotalMinutes,
                TotalKm = r.TotalKm,
                Status = r.Status.ToString(),
                Stops = r.Stops
                .OrderBy(s => s.Sequence)
                .Select(ToStopDto)
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

            if (stop.Route.Date.Date != DateTime.UtcNow.Date)
            {
                return Forbid();
            }
        }

        var nowUtc = DateTime.UtcNow;
        var shouldUpdateLearnedStats =
            request.ArrivedAtUtc.HasValue || request.CompletedAtUtc.HasValue || request.ActualServiceMinutes.HasValue;
        var currentUserId = CurrentUserId;
        var events = new List<RouteStopEvent>();
        var notePayload = new Dictionary<string, object?>();
        var updated = false;

        if (request.Note != null)
        {
            stop.Note = request.Note;
            notePayload["note"] = request.Note;
            updated = true;
        }

        if (request.DriverNote != null)
        {
            stop.DriverNote = string.IsNullOrWhiteSpace(request.DriverNote) ? null : request.DriverNote.Trim();
            notePayload["driverNote"] = stop.DriverNote;
            updated = true;
        }

        if (request.IssueCode != null)
        {
            stop.IssueCode = string.IsNullOrWhiteSpace(request.IssueCode) ? null : request.IssueCode.Trim();
            notePayload["issueCode"] = stop.IssueCode;
            updated = true;
        }

        if (request.FollowUpRequired.HasValue)
        {
            stop.FollowUpRequired = request.FollowUpRequired.Value;
            notePayload["followUpRequired"] = stop.FollowUpRequired;
            updated = true;
        }

        if (!string.IsNullOrWhiteSpace(request.ProofStatus)
            && Enum.TryParse<RouteStopProofStatus>(request.ProofStatus, ignoreCase: true, out var proofStatus))
        {
            stop.ProofStatus = proofStatus;
            notePayload["proofStatus"] = stop.ProofStatus.ToString();
            updated = true;
        }

        if (request.ArrivedAtUtc.HasValue)
        {
            stop.ArrivedAt = request.ArrivedAtUtc.Value;
            stop.ActualArrivalUtc = request.ArrivedAtUtc.Value;
            if (stop.Status != RouteStopStatus.Completed)
            {
                stop.Status = RouteStopStatus.Arrived;
            }
            if (stop.ProofStatus == RouteStopProofStatus.None)
            {
                stop.ProofStatus = RouteStopProofStatus.InProgress;
            }

            events.Add(new RouteStopEvent
            {
                RouteStopId = stop.Id,
                EventType = RouteStopEventType.Arrive,
                EventUtc = nowUtc,
                PayloadJson = JsonSerializer.Serialize(new { arrivedAtUtc = request.ArrivedAtUtc.Value })
            });
            updated = true;
        }

        if (request.CompletedAtUtc.HasValue)
        {
            stop.CompletedAt = request.CompletedAtUtc.Value;
            stop.ActualDepartureUtc = request.CompletedAtUtc.Value;
            stop.Status = RouteStopStatus.Completed;
            stop.ProofStatus = RouteStopProofStatus.Completed;

            events.Add(new RouteStopEvent
            {
                RouteStopId = stop.Id,
                EventType = RouteStopEventType.Depart,
                EventUtc = nowUtc,
                PayloadJson = JsonSerializer.Serialize(new { departedAtUtc = request.CompletedAtUtc.Value })
            });
            updated = true;
        }

        if (request.ActualServiceMinutes.HasValue)
        {
            stop.ActualServiceMinutes = request.ActualServiceMinutes.Value;
            notePayload["actualServiceMinutes"] = stop.ActualServiceMinutes.Value;

            // If the driver enters a duration without timestamps, treat it as completion.
            if (!stop.CompletedAt.HasValue)
            {
                stop.CompletedAt = nowUtc;
                stop.ActualDepartureUtc ??= stop.CompletedAt;
            }

            if (!stop.ArrivedAt.HasValue)
            {
                stop.ArrivedAt = stop.CompletedAt.Value.AddMinutes(-stop.ActualServiceMinutes.Value);
                stop.ActualArrivalUtc ??= stop.ArrivedAt;
            }

            stop.Status = RouteStopStatus.Completed;
            stop.ProofStatus = RouteStopProofStatus.Completed;
            updated = true;
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
            stop.ActualArrivalUtc = null;
            stop.ActualDepartureUtc = null;
            stop.ProofStatus = RouteStopProofStatus.None;
            if (stop.ServiceLocation != null)
            {
                stop.ServiceLocation.Status = ServiceLocationStatus.NotVisited;
                stop.ServiceLocation.UpdatedAtUtc = nowUtc;
            }
            updated = true;
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

        if (stop.Status == RouteStopStatus.Arrived || stop.Status == RouteStopStatus.Completed)
        {
            if (stop.Route.Status != RouteStatus.Started && stop.Route.Status != RouteStatus.Completed)
            {
                stop.Route.Status = RouteStatus.Started;
                stop.Route.StartedAt ??= nowUtc;
                updated = true;
            }
        }

        if (stop.Status == RouteStopStatus.Completed)
        {
            var hasPending = await _dbContext.RouteStops
                .AsNoTracking()
                .AnyAsync(rs => rs.RouteId == stop.RouteId
                    && rs.Id != stop.Id
                    && rs.Status != RouteStopStatus.Completed
                    && rs.Status != RouteStopStatus.NotVisited,
                    cancellationToken);
            if (!hasPending)
            {
                stop.Route.Status = RouteStatus.Completed;
                stop.Route.CompletedAt ??= nowUtc;
                updated = true;
            }
        }

        if (notePayload.Count > 0)
        {
            events.Add(new RouteStopEvent
            {
                RouteStopId = stop.Id,
                EventType = RouteStopEventType.Note,
                EventUtc = nowUtc,
                PayloadJson = JsonSerializer.Serialize(notePayload)
            });
        }

        if (events.Count > 0)
        {
            _dbContext.RouteStopEvents.AddRange(events);
        }

        if (updated)
        {
            stop.LastUpdatedByUserId = currentUserId;
            stop.LastUpdatedUtc = nowUtc;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        if (shouldUpdateLearnedStats)
        {
            try
            {
                await TryUpdateLearnedStatsAsync(stop, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to update learned travel stats for RouteStopId={RouteStopId}", stop.Id);
            }
        }

        return Ok(ToStopDto(stop));
    }

    private async Task<DrivingRouteResult?> TryBuildDrivingRouteAsync(RouteEntity route, CancellationToken cancellationToken)
    {
        try
        {
            var orderedStops = route.Stops.OrderBy(s => s.Sequence).ToList();
            var startPoint = ResolveRouteStart(route, route.Driver);
            var endPoint = ResolveRouteEnd(route, route.Driver);
            var points = new List<RoutePoint>
            {
                new RoutePoint(startPoint.Lat, startPoint.Lng)
            };
            points.AddRange(orderedStops.Select(s => new RoutePoint(s.Latitude, s.Longitude)));
            points.Add(new RoutePoint(endPoint.Lat, endPoint.Lng));

            return await _routing.GetDrivingRouteAsync(points, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to build driving route geometry for RouteId={RouteId}", route.Id);
            return null;
        }
    }

    private static RouteStopDto ToStopDto(RouteStop stop)
    {
        return new RouteStopDto
        {
            Id = stop.Id,
            Sequence = stop.Sequence,
            ServiceLocationId = stop.ServiceLocationId,
            ServiceLocationToolId = stop.ServiceLocation?.ToolId,
            Name = stop.ServiceLocation?.Name ?? $"Stop {stop.Sequence}",
            Latitude = stop.Latitude,
            Longitude = stop.Longitude,
            ServiceMinutes = stop.ServiceMinutes,
            ActualServiceMinutes = stop.ActualServiceMinutes,
            ActualArrivalUtc = stop.ActualArrivalUtc,
            ActualDepartureUtc = stop.ActualDepartureUtc,
            TravelKmFromPrev = stop.TravelKmFromPrev,
            TravelMinutesFromPrev = stop.TravelMinutesFromPrev,
            Status = stop.Status.ToString(),
            ArrivedAtUtc = stop.ArrivedAt,
            CompletedAtUtc = stop.CompletedAt,
            Note = stop.Note,
            DriverNote = stop.DriverNote,
            IssueCode = stop.IssueCode,
            FollowUpRequired = stop.FollowUpRequired,
            ProofStatus = stop.ProofStatus.ToString(),
            LastUpdatedByUserId = stop.LastUpdatedByUserId,
            LastUpdatedUtc = stop.LastUpdatedUtc,
            DriverInstruction = stop.ServiceLocation?.DriverInstruction
        };
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
            StartAddress = route.StartAddress,
            StartLatitude = route.StartLatitude,
            StartLongitude = route.StartLongitude,
            EndAddress = route.EndAddress,
            EndLatitude = route.EndLatitude,
            EndLongitude = route.EndLongitude,
            WeightTemplateId = route.WeightTemplateId,
            TotalMinutes = route.TotalMinutes,
            TotalKm = route.TotalKm,
            Status = route.Status.ToString(),
            Stops = route.Stops
                .OrderBy(s => s.Sequence)
                .Select(ToStopDto)
                .ToList(),
            Geometry = drivingRoute?.GeometryPoints
                .Select(p => new RouteGeometryPointDto { Lat = p.Lat, Lng = p.Lng })
                .ToList()
        };
    }

    private async Task TryUpdateLearnedStatsAsync(RouteStop stop, CancellationToken cancellationToken)
    {
        var arrivalUtc = stop.ActualArrivalUtc ?? stop.ArrivedAt;
        if (!arrivalUtc.HasValue)
        {
            return;
        }

        var previousStop = await _dbContext.RouteStops
            .AsNoTracking()
            .Where(s => s.RouteId == stop.RouteId && s.Sequence < stop.Sequence)
            .OrderByDescending(s => s.Sequence)
            .FirstOrDefaultAsync(cancellationToken);

        if (previousStop == null)
        {
            return;
        }

        var departureUtc = previousStop.ActualDepartureUtc ?? previousStop.CompletedAt;
        if (!departureUtc.HasValue)
        {
            return;
        }

        var travelMinutes = (arrivalUtc.Value - departureUtc.Value).TotalMinutes;
        if (travelMinutes <= 0)
        {
            return;
        }

        var distanceKm = stop.TravelKmFromPrev > 0
            ? (double)stop.TravelKmFromPrev
            : HaversineKm(previousStop.Latitude, previousStop.Longitude, stop.Latitude, stop.Longitude);

        var departureMinute = (int)Math.Round((departureUtc.Value - stop.Route.Date.Date).TotalMinutes);
        departureMinute = Math.Clamp(departureMinute, 0, 24 * 60 - 1);

        await _travelTimeModel.UpdateLearnedStatsAsync(
            stop.Route.Date.Date,
            departureMinute,
            distanceKm,
            travelMinutes,
            stop.ActualServiceMinutes,
            stop.Route.DriverId,
            previousStop.Latitude,
            previousStop.Longitude,
            stop.Latitude,
            stop.Longitude,
            cancellationToken);
    }

    private async Task CreateRouteChangeNotificationAsync(
        RouteEntity route,
        List<Guid> previousStopToolIds,
        List<Guid> newStopToolIds,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var added = newStopToolIds.Except(previousStopToolIds).ToList();
        var removed = previousStopToolIds.Except(newStopToolIds).ToList();

        var versionNumber = await _dbContext.RouteVersions
            .Where(rv => rv.RouteId == route.Id)
            .Select(rv => (int?)rv.VersionNumber)
            .MaxAsync(cancellationToken) ?? 0;

        var summary = JsonSerializer.Serialize(new
        {
            added,
            removed,
            totalBefore = previousStopToolIds.Count,
            totalAfter = newStopToolIds.Count
        });

        var version = new RouteVersion
        {
            RouteId = route.Id,
            VersionNumber = versionNumber + 1,
            CreatedUtc = nowUtc,
            CreatedBy = CurrentUserId ?? Guid.Empty,
            ChangeSummary = summary
        };

        _dbContext.RouteVersions.Add(version);
        _dbContext.RouteChangeNotifications.Add(new RouteChangeNotification
        {
            RouteId = route.Id,
            RouteVersion = version,
            DriverId = route.DriverId,
            CreatedUtc = nowUtc,
            Severity = RouteChangeSeverity.Important
        });
    }

    private async Task<string?> ApplyRouteOverridesAsync(
        RouteEntity route,
        CreateRouteRequest request,
        CancellationToken cancellationToken)
    {
        var startResult = await ResolveOverrideAsync(
            request.StartAddress,
            request.StartLatitude,
            request.StartLongitude,
            "Start",
            cancellationToken);
        if (startResult.Error != null)
        {
            return startResult.Error;
        }

        var endResult = await ResolveOverrideAsync(
            request.EndAddress,
            request.EndLatitude,
            request.EndLongitude,
            "End",
            cancellationToken);
        if (endResult.Error != null)
        {
            return endResult.Error;
        }

        route.StartAddress = startResult.Address;
        route.StartLatitude = startResult.Latitude;
        route.StartLongitude = startResult.Longitude;

        route.EndAddress = endResult.Address;
        route.EndLatitude = endResult.Latitude;
        route.EndLongitude = endResult.Longitude;

        return null;
    }

    private async Task<(string? Address, double? Latitude, double? Longitude, string? Error)> ResolveOverrideAsync(
        string? address,
        double? latitude,
        double? longitude,
        string label,
        CancellationToken cancellationToken)
    {
        address = TrimOrNull(address);
        var hasAddress = !string.IsNullOrWhiteSpace(address);
        var hasLatitude = latitude.HasValue;
        var hasLongitude = longitude.HasValue;

        if (hasLatitude != hasLongitude)
        {
            return (null, null, null, $"Provide both {label}Latitude and {label}Longitude, or leave both empty.");
        }

        if (!hasAddress && !hasLatitude)
        {
            return (null, null, null, null);
        }

        if (!hasLatitude && hasAddress)
        {
            var geocode = await _geocodingService.GeocodeAddressAsync(address!, cancellationToken);
            if (geocode == null)
            {
                return (null, null, null, $"Unable to resolve {label}Latitude/{label}Longitude from {label}Address.");
            }

            latitude = geocode.Latitude;
            longitude = geocode.Longitude;
            hasLatitude = true;
            hasLongitude = true;
        }
        else if (!hasAddress && hasLatitude)
        {
            var reverseAddress = await _geocodingService.ReverseGeocodeAsync(latitude!.Value, longitude!.Value, cancellationToken);
            if (string.IsNullOrWhiteSpace(reverseAddress))
            {
                return (null, null, null, $"Unable to resolve {label}Address from {label}Latitude/{label}Longitude.");
            }

            address = reverseAddress;
            hasAddress = true;
        }

        if (!hasLatitude || !hasLongitude)
        {
            return (null, null, null, $"{label}Latitude and {label}Longitude are required after geocoding.");
        }

        if (latitude < -90 || latitude > 90)
        {
            return (null, null, null, $"{label}Latitude must be between -90 and 90.");
        }

        if (longitude < -180 || longitude > 180)
        {
            return (null, null, null, $"{label}Longitude must be between -180 and 180.");
        }

        return (address, latitude, longitude, null);
    }

    private static (double Lat, double Lng) ResolveRouteStart(RouteEntity route, Driver driver)
    {
        if (route.StartLatitude.HasValue && route.StartLongitude.HasValue)
        {
            return (route.StartLatitude.Value, route.StartLongitude.Value);
        }

        return (driver.StartLatitude ?? 0, driver.StartLongitude ?? 0);
    }

    private static (double Lat, double Lng) ResolveRouteEnd(RouteEntity route, Driver driver)
    {
        if (route.EndLatitude.HasValue && route.EndLongitude.HasValue)
        {
            return (route.EndLatitude.Value, route.EndLongitude.Value);
        }

        return (driver.StartLatitude ?? 0, driver.StartLongitude ?? 0);
    }

    private static string? TrimOrNull(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static double HaversineKm(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371;
        var dLat = ToRad(lat2 - lat1);
        var dLon = ToRad(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(ToRad(lat1)) * Math.Cos(ToRad(lat2)) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return R * c;
    }

    private static double ToRad(double value) => (Math.PI / 180) * value;
}


