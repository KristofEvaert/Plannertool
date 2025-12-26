using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using TransportPlanner.Api.Services.Routing;
using TransportPlanner.Application.DTOs;
using TransportPlanner.Domain.Entities;
using TransportPlanner.Infrastructure.Data;
using TransportPlanner.Infrastructure.Identity;
using RouteEntity = TransportPlanner.Domain.Entities.Route;

namespace TransportPlanner.Api.Controllers;

public class AutoGenerateRouteRequest
{
    public DateTime Date { get; set; }
    public Guid DriverToolId { get; set; }
    public int OwnerId { get; set; }
    public int? MaxStops { get; set; }
    public List<Guid>? ServiceLocationToolIds { get; set; } // explicit candidate list from map
}

public class AutoGenerateAllRequest
{
    public DateTime Date { get; set; }
    public int OwnerId { get; set; }
    public int? MaxStopsPerDriver { get; set; }
    public List<Guid>? ServiceLocationToolIds { get; set; } // optional candidate list from map
}

public class AutoGenerateAllResponse
{
    public List<RouteDto> Routes { get; set; } = new();
    public List<string> SkippedDrivers { get; set; } = new();
}

[ApiController]
[Route("api/routes/auto-generate")]
[Authorize(Policy = "RequireStaff")]
public class AutoRoutesController : ControllerBase
{
    private readonly TransportPlannerDbContext _dbContext;
    private readonly IRoutingService _routing;
    private readonly ILogger<AutoRoutesController> _logger;
    private bool IsSuperAdmin => User.IsInRole(AppRoles.SuperAdmin);
    private int? CurrentOwnerId => int.TryParse(User.FindFirstValue("ownerId"), out var id) ? id : null;
    private bool CanAccessOwner(int ownerId) => IsSuperAdmin || (CurrentOwnerId.HasValue && CurrentOwnerId.Value == ownerId);

    public AutoRoutesController(TransportPlannerDbContext dbContext, IRoutingService routing, ILogger<AutoRoutesController> logger)
    {
        _dbContext = dbContext;
        _routing = routing;
        _logger = logger;
    }

    [HttpPost]
    [ProducesResponseType(typeof(RouteDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<RouteDto>> AutoGenerate([FromBody] AutoGenerateRouteRequest request, CancellationToken cancellationToken)
    {
        var date = request.Date.Date;

        if (!CanAccessOwner(request.OwnerId))
        {
            return Forbid();
        }

        var driver = await _dbContext.Drivers.AsNoTracking().FirstOrDefaultAsync(d => d.ToolId == request.DriverToolId, cancellationToken);
        if (driver == null)
        {
            return BadRequest(new { message = "Driver not found" });
        }

        if (driver.OwnerId != request.OwnerId)
        {
            return BadRequest(new { message = "Driver does not belong to the selected owner" });
        }

        // Candidates: all open/planned for owner, filtered by explicit ToolIds when provided (map selection)
        IQueryable<ServiceLocation> query = _dbContext.ServiceLocations
            .AsNoTracking()
            .Where(sl =>
                sl.OwnerId == request.OwnerId &&
                sl.Status == ServiceLocationStatus.Open &&
                sl.IsActive);

        if (request.ServiceLocationToolIds != null && request.ServiceLocationToolIds.Any())
        {
            query = query.Where(sl => request.ServiceLocationToolIds.Contains(sl.ToolId));
        }

        var candidates = await query.ToListAsync(cancellationToken);

        var (dto, usedIds, reason) = await GenerateRouteForDriverAsync(
            date,
            driver,
            request.OwnerId,
            candidates,
            request.MaxStops,
            driver.MaxWorkMinutesPerDay,
            cancellationToken);

        if (dto == null)
        {
            return BadRequest(new { message = reason ?? "No route generated." });
        }

        // Ensure selected locations are not reused by subsequent calls (single-driver call only uses local list)
        return Ok(dto);
    }

    [HttpPost("all")]
    [ProducesResponseType(typeof(AutoGenerateAllResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<AutoGenerateAllResponse>> AutoGenerateForAll([FromBody] AutoGenerateAllRequest request, CancellationToken cancellationToken)
    {
        var date = request.Date.Date;

        if (request.OwnerId <= 0)
        {
            return BadRequest(new { message = "Owner is required." });
        }

        if (!CanAccessOwner(request.OwnerId))
        {
            return Forbid();
        }

        // Load all active drivers for owner with availability on this date
        var drivers = await _dbContext.Drivers
            .AsNoTracking()
            .Where(d => d.OwnerId == request.OwnerId && d.IsActive)
            .Select(d => new
            {
                Driver = d,
                Availability = d.Availabilities.FirstOrDefault(av => av.Date == date)
            })
            .ToListAsync(cancellationToken);

        var availableDrivers = drivers
            .Where(d => d.Availability != null && d.Availability.AvailableMinutes > 0)
            .ToList();

        if (!availableDrivers.Any())
        {
            return BadRequest(new { message = "No available drivers for this date/owner." });
        }

        // Candidates: all open/planned for owner, filtered by explicit ToolIds when provided (map selection)
        IQueryable<ServiceLocation> query = _dbContext.ServiceLocations
            .AsNoTracking()
            .Where(sl =>
                sl.OwnerId == request.OwnerId &&
                sl.Status == ServiceLocationStatus.Open &&
                sl.IsActive);

        if (request.ServiceLocationToolIds != null && request.ServiceLocationToolIds.Any())
        {
            query = query.Where(sl => request.ServiceLocationToolIds.Contains(sl.ToolId));
        }

        var candidatePool = await query.ToListAsync(cancellationToken);

        if (!candidatePool.Any())
        {
            return BadRequest(new { message = "No service locations for this date/owner." });
        }

        var response = new AutoGenerateAllResponse();

        foreach (var driverEntry in availableDrivers)
        {
            var capacity = Math.Min(driverEntry.Driver.MaxWorkMinutesPerDay, driverEntry.Availability!.AvailableMinutes);

            var (dto, usedIds, reason) = await GenerateRouteForDriverAsync(
                date,
                driverEntry.Driver,
                request.OwnerId,
                candidatePool,
                request.MaxStopsPerDriver,
                capacity,
                cancellationToken);

            if (dto != null)
            {
                response.Routes.Add(dto);
                // remove used locations from pool so they are not assigned to another driver
                candidatePool.RemoveAll(sl => usedIds.Contains(sl.Id));
            }
            else if (!string.IsNullOrEmpty(reason))
            {
                response.SkippedDrivers.Add($"{driverEntry.Driver.Name}: {reason}");
            }
        }

        if (!response.Routes.Any())
        {
            return BadRequest(new { message = "No routes generated for any driver." });
        }

        return Ok(response);
    }

    private async Task<(RouteDto? dto, List<int> usedLocationIds, string? reason)> GenerateRouteForDriverAsync(
        DateTime date,
        Driver driver,
        int ownerId,
        List<ServiceLocation> candidatePool,
        int? maxStops,
        int capacityMinutes,
        CancellationToken cancellationToken)
    {
        var existingRoute = await _dbContext.Routes
            .Include(r => r.Stops)
            .FirstOrDefaultAsync(r => r.DriverId == driver.Id && r.OwnerId == ownerId && r.Date.Date == date, cancellationToken);

        if (existingRoute != null && existingRoute.Status == RouteStatus.Fixed)
        {
            return (null, new List<int>(), "Existing route is fixed; auto-generate aborted.");
        }

        // Don't double-plan locations already planned for any driver on this date
        var plannedLocationIdsForDate = await _dbContext.RouteStops
            .Where(rs => rs.Route.DriverId == driver.Id && rs.Route.OwnerId == ownerId && rs.Route.Date == date && rs.ServiceLocationId != null)
            .Select(rs => rs.ServiceLocationId!.Value)
            .ToListAsync(cancellationToken);

        var candidates = candidatePool
            .Where(sl => sl.OwnerId == ownerId && sl.Status == ServiceLocationStatus.Open && sl.IsActive && !plannedLocationIdsForDate.Contains(sl.Id))
            .ToList();

        if (!candidates.Any())
        {
            return (null, new List<int>(), "No service locations for this date/owner.");
        }

        var stopLimit = maxStops ?? 30;

        // Greedy OSRM scoring
        var remaining = new List<ServiceLocation>(candidates);
        var selected = new List<ServiceLocation>();

        double currentLat = driver.StartLatitude ?? 0;
        double currentLng = driver.StartLongitude ?? 0;
        var usedMinutes = 0;
        var capacity = capacityMinutes <= 0 ? driver.MaxWorkMinutesPerDay : capacityMinutes;

        while (remaining.Any() && selected.Count < stopLimit)
        {
            ServiceLocation? best = null;
            double bestScore = double.MaxValue;
            int bestTravelMinutes = 0;

            foreach (var loc in remaining)
            {
                // Fast estimate (haversine) to keep selection loop O(n^2) but cheap
                var travelMinutes = EstimateMinutes(currentLat, currentLng, loc.Latitude ?? 0, loc.Longitude ?? 0);

                var orderDate = (loc.PriorityDate ?? loc.DueDate).Date;
                var daysLate = (date - orderDate).TotalDays;
                var urgencyPenalty = daysLate > 0 ? daysLate * 90 : 0;
                var futureOffset = daysLate < 0 ? Math.Abs(daysLate) * 10 : 0;
                var nearbyFutureBonus = (daysLate < 0 && travelMinutes < 10) ? -20 : 0;

                var score = travelMinutes + urgencyPenalty + futureOffset + nearbyFutureBonus;

                if (score < bestScore)
                {
                    best = loc;
                    bestScore = score;
                    bestTravelMinutes = (int)Math.Round((double)travelMinutes, MidpointRounding.AwayFromZero);
                }
            }

            if (best == null) break;

            var projected = usedMinutes + bestTravelMinutes + best.ServiceMinutes;
            if (projected > capacity * 1.05) // allow small buffer
            {
                // skip this best; remove it to avoid loop
                remaining.Remove(best);
                continue;
            }

            selected.Add(best);
            remaining.Remove(best);
            usedMinutes += bestTravelMinutes + best.ServiceMinutes;
            currentLat = best.Latitude ?? 0;
            currentLng = best.Longitude ?? 0;
        }

        if (!selected.Any())
        {
            return (null, new List<int>(), "No feasible route within driver capacity.");
        }

        var stops = new List<RouteStop>();
        double lastLat = driver.StartLatitude ?? 0;
        double lastLng = driver.StartLongitude ?? 0;
        int sequence = 1;
        double totalKm = 0;
        int totalTravelMinutes = 0;

        foreach (var loc in selected)
        {
            // Now that order is fixed, call OSRM for accurate leg (still O(n))
            var travel = await _routing.GetDrivingRouteAsync(new List<RoutePoint>
            {
                new RoutePoint(lastLat, lastLng),
                new RoutePoint(loc.Latitude ?? 0, loc.Longitude ?? 0)
            }, cancellationToken);

            double travelMinutes = travel != null
                ? travel.TotalDurationMinutes
                : EstimateMinutes(lastLat, lastLng, loc.Latitude ?? 0, loc.Longitude ?? 0);
            var travelKm = travel?.TotalDistanceKm ?? HaversineKm(lastLat, lastLng, loc.Latitude ?? 0, loc.Longitude ?? 0);

            totalKm += travelKm;
            totalTravelMinutes += (int)Math.Round(travelMinutes, MidpointRounding.AwayFromZero);

            stops.Add(new RouteStop
            {
                Sequence = sequence++,
                StopType = RouteStopType.Location,
                ServiceLocationId = loc.Id,
                Latitude = loc.Latitude ?? 0,
                Longitude = loc.Longitude ?? 0,
                ServiceMinutes = loc.ServiceMinutes,
                TravelKmFromPrev = (float)travelKm,
                TravelMinutesFromPrev = (int)Math.Round(travelMinutes, MidpointRounding.AwayFromZero),
                Status = RouteStopStatus.Pending
            });

            lastLat = loc.Latitude ?? 0;
            lastLng = loc.Longitude ?? 0;
        }

        var routeEntity = existingRoute ?? new RouteEntity
        {
            Date = date,
            OwnerId = ownerId,
            ServiceTypeId = selected.First().ServiceTypeId,
            DriverId = driver.Id,
            Status = RouteStatus.Temp,
            TotalKm = 0,
            TotalMinutes = 0,
            Stops = new List<RouteStop>()
        };

        routeEntity.Stops = stops;
        routeEntity.TotalKm = (float)totalKm;
        routeEntity.TotalMinutes = totalTravelMinutes + selected.Sum(s => s.ServiceMinutes);
        routeEntity.Status = RouteStatus.Temp;

        if (existingRoute == null)
        {
            _dbContext.Routes.Add(routeEntity);
        }
        else
        {
            _dbContext.Routes.Update(routeEntity);
        }

        // mark locations planned
        var locationIds = selected.Select(s => s.Id).ToList();
        await _dbContext.ServiceLocations
            .Where(sl => locationIds.Contains(sl.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(sl => sl.Status, ServiceLocationStatus.Planned), cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);

        // re-read with stops for DTO
        var route = await _dbContext.Routes
            .Include(r => r.Driver)
            .Include(r => r.Stops)
                .ThenInclude(s => s.ServiceLocation)
            .FirstAsync(r => r.Id == routeEntity.Id, cancellationToken);

        var dto = new RouteDto
        {
            Id = route.Id,
            Date = route.Date,
            OwnerId = route.OwnerId,
            ServiceTypeId = route.ServiceTypeId,
            DriverId = route.DriverId,
            DriverName = route.Driver.Name,
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
                    ServiceLocationToolId = s.ServiceLocation?.ToolId,
                    Name = s.ServiceLocation?.Name,
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
                    DriverInstruction = s.ServiceLocation?.DriverInstruction
                })
                .ToList()
        };

        return (dto, locationIds, null);
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

    private static int EstimateMinutes(double lat1, double lon1, double lat2, double lon2)
    {
        var km = HaversineKm(lat1, lon1, lat2, lon2);
        return (int)Math.Round((km / 50.0) * 60.0);
    }
}
