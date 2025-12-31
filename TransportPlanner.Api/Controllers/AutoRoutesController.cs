using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using TransportPlanner.Api.Services.Routing;
using TransportPlanner.Application.DTOs;
using TransportPlanner.Application.Services;
using TransportPlanner.Domain.Entities;
using TransportPlanner.Infrastructure.Data;
using TransportPlanner.Infrastructure.Identity;
using TransportPlanner.Infrastructure.Services;
using RouteEntity = TransportPlanner.Domain.Entities.Route;

namespace TransportPlanner.Api.Controllers;

public class AutoGenerateRouteRequest
{
    public DateTime Date { get; set; }
    public Guid DriverToolId { get; set; }
    public int OwnerId { get; set; }
    public int? MaxStops { get; set; }
    public List<Guid>? ServiceLocationToolIds { get; set; } // explicit candidate list from map
    public double? WeightTime { get; set; }
    public double? WeightDistance { get; set; }
    public double? WeightDate { get; set; }
    public double? WeightCost { get; set; }
    public double? WeightOvertime { get; set; }
    public int? WeightTemplateId { get; set; }
    public bool? RequireServiceTypeMatch { get; set; }
}

public class AutoGenerateAllRequest
{
    public DateTime Date { get; set; }
    public int OwnerId { get; set; }
    public int? MaxStopsPerDriver { get; set; }
    public List<Guid>? ServiceLocationToolIds { get; set; } // optional candidate list from map
    public double? WeightTime { get; set; }
    public double? WeightDistance { get; set; }
    public double? WeightDate { get; set; }
    public double? WeightCost { get; set; }
    public double? WeightOvertime { get; set; }
    public int? WeightTemplateId { get; set; }
    public bool? RequireServiceTypeMatch { get; set; }
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
    private readonly ITravelTimeModelService _travelTimeModel;
    private readonly ILogger<AutoRoutesController> _logger;
    private bool IsSuperAdmin => User.IsInRole(AppRoles.SuperAdmin);
    private int? CurrentOwnerId => int.TryParse(User.FindFirstValue("ownerId"), out var id) ? id : null;
    private bool CanAccessOwner(int ownerId) => IsSuperAdmin || (CurrentOwnerId.HasValue && CurrentOwnerId.Value == ownerId);

    public AutoRoutesController(
        TransportPlannerDbContext dbContext,
        IRoutingService routing,
        ITravelTimeModelService travelTimeModel,
        ILogger<AutoRoutesController> logger)
    {
        _dbContext = dbContext;
        _routing = routing;
        _travelTimeModel = travelTimeModel;
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

        if (!driver.StartLatitude.HasValue || !driver.StartLongitude.HasValue
            || (driver.StartLatitude.Value == 0 && driver.StartLongitude.Value == 0))
        {
            return BadRequest(new { message = "Driver start coordinates are required to auto-generate a route." });
        }

        var availability = await _dbContext.DriverAvailabilities
            .AsNoTracking()
            .FirstOrDefaultAsync(av => av.DriverId == driver.Id && av.Date == date, cancellationToken);

        if (availability == null || availability.AvailableMinutes <= 0)
        {
            return BadRequest(new { message = "Driver is not available on this date." });
        }

        var costSettings = await GetCostSettingsAsync(cancellationToken);
        var weightTemplateResult = await ResolveWeightTemplateAsync(request.WeightTemplateId, request.OwnerId, cancellationToken);
        if (!string.IsNullOrEmpty(weightTemplateResult.Error))
        {
            return BadRequest(new { message = weightTemplateResult.Error });
        }

        // Candidates: all open/planned for owner, filtered by explicit ToolIds when provided (map selection)
        IQueryable<ServiceLocation> query = _dbContext.ServiceLocations
            .AsNoTracking()
            .Where(sl =>
                sl.OwnerId == request.OwnerId &&
                sl.Status == ServiceLocationStatus.Open &&
                sl.IsActive &&
                sl.Latitude.HasValue &&
                sl.Longitude.HasValue);

        if (request.ServiceLocationToolIds != null && request.ServiceLocationToolIds.Any())
        {
            query = query.Where(sl => request.ServiceLocationToolIds.Contains(sl.ToolId));
        }

        var candidates = await query.ToListAsync(cancellationToken);
        var locationWindows = await LoadLocationWindowsAsync(candidates.Select(c => c.Id).ToList(), date, cancellationToken);
        var locationConstraints = await LoadLocationConstraintsAsync(candidates.Select(c => c.Id).ToList(), cancellationToken);

        RouteDto? dto;
        List<int> usedIds;
        string? reason;
        try
        {
            (dto, usedIds, reason) = await GenerateRouteForDriverAsync(
                date,
                driver,
                request.OwnerId,
                candidates,
                request.MaxStops,
                availability.AvailableMinutes,
                availability.StartMinuteOfDay,
                availability.EndMinuteOfDay,
                ResolveWeights(weightTemplateResult.Template, request.WeightTime, request.WeightDistance, request.WeightDate, request.WeightCost, request.WeightOvertime),
                costSettings,
                locationWindows,
                locationConstraints,
                weightTemplateResult.Template?.Id,
                request.RequireServiceTypeMatch == true,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Auto-generate failed for DriverId={DriverId}", driver.Id);
            return BadRequest(new { message = "Auto-generate failed." });
        }

        if (dto == null)
        {
            return BadRequest(new { message = reason ?? "No route generated." });
        }

        await SyncPlannedStatusesAsync(request.OwnerId, cancellationToken);

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

        var costSettings = await GetCostSettingsAsync(cancellationToken);
        var weightTemplateResult = await ResolveWeightTemplateAsync(request.WeightTemplateId, request.OwnerId, cancellationToken);
        if (!string.IsNullOrEmpty(weightTemplateResult.Error))
        {
            return BadRequest(new { message = weightTemplateResult.Error });
        }

        await ClearTempRoutesForDayAsync(date, request.OwnerId, cancellationToken);

        // Candidates: all open/planned for owner, filtered by explicit ToolIds when provided (map selection)
        IQueryable<ServiceLocation> query = _dbContext.ServiceLocations
            .AsNoTracking()
            .Where(sl =>
                sl.OwnerId == request.OwnerId &&
                sl.Status == ServiceLocationStatus.Open &&
                sl.IsActive &&
                sl.Latitude.HasValue &&
                sl.Longitude.HasValue);

        if (request.ServiceLocationToolIds != null && request.ServiceLocationToolIds.Any())
        {
            query = query.Where(sl => request.ServiceLocationToolIds.Contains(sl.ToolId));
        }

        var candidatePool = await query.ToListAsync(cancellationToken);
        var locationWindows = await LoadLocationWindowsAsync(candidatePool.Select(c => c.Id).ToList(), date, cancellationToken);
        var locationConstraints = await LoadLocationConstraintsAsync(candidatePool.Select(c => c.Id).ToList(), cancellationToken);

        if (!candidatePool.Any())
        {
            return BadRequest(new { message = "No service locations for this date/owner." });
        }

        var response = new AutoGenerateAllResponse();

        foreach (var driverEntry in availableDrivers)
        {
            if (!driverEntry.Driver.StartLatitude.HasValue || !driverEntry.Driver.StartLongitude.HasValue
                || (driverEntry.Driver.StartLatitude.Value == 0 && driverEntry.Driver.StartLongitude.Value == 0))
            {
                response.SkippedDrivers.Add($"{driverEntry.Driver.Name}: Driver start coordinates are missing.");
                continue;
            }

            try
            {
                var capacity = Math.Min(driverEntry.Driver.MaxWorkMinutesPerDay, driverEntry.Availability!.AvailableMinutes);

                var (dto, usedIds, reason) = await GenerateRouteForDriverAsync(
                    date,
                    driverEntry.Driver,
                    request.OwnerId,
                    candidatePool,
                    request.MaxStopsPerDriver,
                    capacity,
                    driverEntry.Availability!.StartMinuteOfDay,
                    driverEntry.Availability.EndMinuteOfDay,
                    ResolveWeights(weightTemplateResult.Template, request.WeightTime, request.WeightDistance, request.WeightDate, request.WeightCost, request.WeightOvertime),
                    costSettings,
                    locationWindows,
                    locationConstraints,
                    weightTemplateResult.Template?.Id,
                    request.RequireServiceTypeMatch == true,
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
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Auto-generate failed for DriverId={DriverId}", driverEntry.Driver.Id);
                response.SkippedDrivers.Add($"{driverEntry.Driver.Name}: Auto-generate failed.");
            }
        }

        if (!response.Routes.Any())
        {
            return BadRequest(new { message = "No routes generated for any driver." });
        }

        await SyncPlannedStatusesAsync(request.OwnerId, cancellationToken);

        return Ok(response);
    }

    private async Task<(RouteDto? dto, List<int> usedLocationIds, string? reason)> GenerateRouteForDriverAsync(
        DateTime date,
        Driver driver,
        int ownerId,
        List<ServiceLocation> candidatePool,
        int? maxStops,
        int capacityMinutes,
        int startMinuteOfDay,
        int endMinuteOfDay,
        WeightSet weights,
        CostSettings costSettings,
        Dictionary<int, TimeWindow> locationWindows,
        Dictionary<int, ServiceLocationConstraint> locationConstraints,
        int? weightTemplateId,
        bool requireServiceTypeMatch,
        CancellationToken cancellationToken)
    {
        var existingRoute = await _dbContext.Routes
            .Include(r => r.Stops)
            .FirstOrDefaultAsync(r => r.DriverId == driver.Id && r.OwnerId == ownerId && r.Date.Date == date, cancellationToken);

        if (existingRoute != null && existingRoute.Status == RouteStatus.Fixed)
        {
            return (null, new List<int>(), "Existing route is fixed; auto-generate aborted.");
        }

        var previousServiceLocationIds = existingRoute?.Stops
            .Where(s => s.ServiceLocationId.HasValue)
            .Select(s => s.ServiceLocationId!.Value)
            .Distinct()
            .ToList() ?? new List<int>();

        // Don't double-plan locations already planned for any driver on this date
        var plannedLocationIdsForDate = await _dbContext.RouteStops
            .Where(rs => rs.Route.DriverId == driver.Id && rs.Route.OwnerId == ownerId && rs.Route.Date == date && rs.ServiceLocationId != null)
            .Select(rs => rs.ServiceLocationId!.Value)
            .ToListAsync(cancellationToken);

        var candidates = candidatePool
            .Where(sl => sl.OwnerId == ownerId && sl.Status == ServiceLocationStatus.Open && sl.IsActive && !plannedLocationIdsForDate.Contains(sl.Id))
            .ToList();

        if (requireServiceTypeMatch)
        {
            var driverServiceTypeIds = await _dbContext.DriverServiceTypes
                .AsNoTracking()
                .Where(dst => dst.DriverId == driver.Id)
                .Select(dst => dst.ServiceTypeId)
                .ToListAsync(cancellationToken);

            if (driverServiceTypeIds.Count == 0)
            {
                await ClearExistingRouteAsync(existingRoute, previousServiceLocationIds, cancellationToken);
                return (null, new List<int>(), "Driver has no service types assigned.");
            }

            candidates = candidates
                .Where(sl => driverServiceTypeIds.Contains(sl.ServiceTypeId))
                .ToList();
        }

        if (!candidates.Any())
        {
            await ClearExistingRouteAsync(existingRoute, previousServiceLocationIds, cancellationToken);
            return (null, new List<int>(), "No service locations for this date/owner.");
        }

        var stopLimit = maxStops ?? 30;

        // Greedy OSRM scoring
        var remaining = new List<ServiceLocation>(candidates);
        var selected = new List<ServiceLocation>();

        var startPoint = ResolveRouteStart(existingRoute, driver);
        double currentLat = startPoint.Lat;
        double currentLng = startPoint.Lng;
        var usedMinutes = 0;
        var currentMinute = startMinuteOfDay;
        var capacity = capacityMinutes <= 0 ? driver.MaxWorkMinutesPerDay : capacityMinutes;

        while (remaining.Any() && selected.Count < stopLimit)
        {
            ServiceLocation? best = null;
            double bestScore = double.MaxValue;
            int bestTravelMinutes = 0;

        foreach (var loc in remaining)
        {
            if (!locationWindows.TryGetValue(loc.Id, out var window))
            {
                window = TimeWindow.AlwaysOpen;
            }

                if (window.IsClosed)
                {
                    continue;
                }

                // Fast estimate (haversine) to keep selection loop O(n^2) but cheap
                var travelKm = HaversineKm(currentLat, currentLng, loc.Latitude ?? 0, loc.Longitude ?? 0);
                var travelMinutes = await _travelTimeModel.EstimateMinutesAsync(
                    date,
                    currentMinute,
                    travelKm,
                    currentLat,
                    currentLng,
                    loc.Latitude ?? 0,
                    loc.Longitude ?? 0,
                    cancellationToken);
                var travelMinutesRounded = (int)Math.Round(travelMinutes, MidpointRounding.AwayFromZero);

                var serviceMinutes = ResolveServiceMinutes(loc, locationConstraints, driver);
                var arrivalMinute = currentMinute + travelMinutesRounded;
                if (!TimeWindowHelper.TrySchedule(
                        window,
                        arrivalMinute,
                        serviceMinutes,
                        out var waitMinutes,
                        out var startServiceMinute,
                        out var endServiceMinute))
                {
                    continue;
                }

                var orderDate = (loc.PriorityDate ?? loc.DueDate).Date;
                var daysLate = (date - orderDate).TotalDays;
                var urgencyPenalty = daysLate > 0 ? daysLate * 90 : 0;
                var futureOffset = daysLate < 0 ? Math.Abs(daysLate) * 10 : 0;
                var nearbyFutureBonus = (daysLate < 0 && travelMinutes < 10) ? -20 : 0;

                var timeCost = travelMinutesRounded + waitMinutes + serviceMinutes;
                var projectedUsedMinutes = usedMinutes + travelMinutesRounded + waitMinutes + serviceMinutes;
                var projectedEndMinute = startMinuteOfDay + projectedUsedMinutes;
                var overtimeMinutes = Math.Max(0, projectedEndMinute - endMinuteOfDay);
                var costTravel = CostCalculator.CalculateTravelCost(
                    travelKm,
                    travelMinutes,
                    costSettings.FuelCostPerKm,
                    costSettings.PersonnelCostPerHour);
                var dateCost = urgencyPenalty + futureOffset + nearbyFutureBonus;
                var score = (weights.Time * timeCost)
                            + (weights.Distance * travelKm)
                            + (weights.Date * dateCost)
                            + (weights.Cost * costTravel)
                            + (weights.Overtime * overtimeMinutes);

                if (score < bestScore)
                {
                    best = loc;
                    bestScore = score;
                    bestTravelMinutes = travelMinutesRounded;
                }
            }

            if (best == null) break;

            var bestWindow = locationWindows.TryGetValue(best.Id, out var bw) ? bw : TimeWindow.AlwaysOpen;
            var bestServiceMinutes = ResolveServiceMinutes(best, locationConstraints, driver);
            var bestArrivalMinute = currentMinute + bestTravelMinutes;
            var bestWaitMinutes = bestArrivalMinute < bestWindow.OpenMinute ? bestWindow.OpenMinute - bestArrivalMinute : 0;
            var projected = usedMinutes + bestTravelMinutes + bestWaitMinutes + bestServiceMinutes;
            if (projected > capacity * 1.05) // allow small buffer
            {
                // skip this best; remove it to avoid loop
                remaining.Remove(best);
                continue;
            }

            selected.Add(best);
            remaining.Remove(best);
            usedMinutes += bestTravelMinutes + bestWaitMinutes + bestServiceMinutes;
            currentMinute = startMinuteOfDay + usedMinutes;
            currentLat = best.Latitude ?? 0;
            currentLng = best.Longitude ?? 0;
        }

        if (!selected.Any())
        {
            await ClearExistingRouteAsync(existingRoute, previousServiceLocationIds, cancellationToken);
            return (null, new List<int>(), "No feasible route within driver capacity.");
        }

        var stops = new List<RouteStop>();
        double lastLat = startPoint.Lat;
        double lastLng = startPoint.Lng;
        int sequence = 1;
        double totalKm = 0;
        int totalTravelMinutes = 0;
        int totalWaitMinutes = 0;
        int plannedMinute = startMinuteOfDay;

        foreach (var loc in selected)
        {
            // Now that order is fixed, call OSRM for accurate leg (still O(n))
            DrivingRouteResult? travel = null;
            try
            {
                travel = await _routing.GetDrivingRouteAsync(new List<RoutePoint>
                {
                    new RoutePoint(lastLat, lastLng),
                    new RoutePoint(loc.Latitude ?? 0, loc.Longitude ?? 0)
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "OSRM route leg failed for DriverId={DriverId}", driver.Id);
            }

            double travelMinutes = travel != null
                ? travel.TotalDurationMinutes
                : EstimateMinutes(lastLat, lastLng, loc.Latitude ?? 0, loc.Longitude ?? 0);
            var travelKm = travel?.TotalDistanceKm ?? HaversineKm(lastLat, lastLng, loc.Latitude ?? 0, loc.Longitude ?? 0);
            var travelMinutesRounded = (int)Math.Round(travelMinutes, MidpointRounding.AwayFromZero);
            var serviceMinutes = ResolveServiceMinutes(loc, locationConstraints, driver);
            var window = locationWindows.TryGetValue(loc.Id, out var windowValue) ? windowValue : TimeWindow.AlwaysOpen;
            var arrivalMinute = plannedMinute + travelMinutesRounded;
            if (!TimeWindowHelper.TrySchedule(
                    window,
                    arrivalMinute,
                    serviceMinutes,
                    out var waitMinutes,
                    out var startServiceMinute,
                    out var endServiceMinute))
            {
                waitMinutes = arrivalMinute < window.OpenMinute ? window.OpenMinute - arrivalMinute : 0;
                startServiceMinute = arrivalMinute + waitMinutes;
                endServiceMinute = startServiceMinute + serviceMinutes;
            }

            totalKm += travelKm;
            totalTravelMinutes += travelMinutesRounded;
            totalWaitMinutes += waitMinutes;

            stops.Add(new RouteStop
            {
                Sequence = sequence++,
                StopType = RouteStopType.Location,
                ServiceLocationId = loc.Id,
                Latitude = loc.Latitude ?? 0,
                Longitude = loc.Longitude ?? 0,
                ServiceMinutes = serviceMinutes,
                TravelKmFromPrev = (float)travelKm,
                TravelMinutesFromPrev = travelMinutesRounded,
                PlannedStart = date.Date.AddMinutes(startServiceMinute),
                PlannedEnd = date.Date.AddMinutes(endServiceMinute),
                Status = RouteStopStatus.Pending
            });

            plannedMinute = endServiceMinute;
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

        if (existingRoute != null)
        {
            await _dbContext.RouteStops
                .Where(rs => rs.RouteId == existingRoute.Id)
                .ExecuteDeleteAsync(cancellationToken);
            routeEntity.Stops.Clear();
        }

        routeEntity.Stops = stops;
        routeEntity.TotalKm = (float)totalKm;
        routeEntity.TotalMinutes = totalTravelMinutes + totalWaitMinutes + stops.Sum(s => s.ServiceMinutes);
        routeEntity.Status = RouteStatus.Temp;
        routeEntity.WeightTemplateId = weightTemplateId;

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

        if (previousServiceLocationIds.Count > 0)
        {
            var removedFromThisRoute = previousServiceLocationIds
                .Except(locationIds)
                .Distinct()
                .ToList();

            if (removedFromThisRoute.Count > 0)
            {
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
                        .Where(sl => openIds.Contains(sl.Id) && sl.Status == ServiceLocationStatus.Planned)
                        .ExecuteUpdateAsync(s => s.SetProperty(sl => sl.Status, ServiceLocationStatus.Open), cancellationToken);
                }
            }
        }

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
            WeightTemplateId = route.WeightTemplateId,
            StartAddress = route.StartAddress,
            StartLatitude = route.StartLatitude,
            StartLongitude = route.StartLongitude,
            EndAddress = route.EndAddress,
            EndLatitude = route.EndLatitude,
            EndLongitude = route.EndLongitude,
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
                    ActualArrivalUtc = s.ActualArrivalUtc,
                    ActualDepartureUtc = s.ActualDepartureUtc,
                    TravelKmFromPrev = s.TravelKmFromPrev,
                    TravelMinutesFromPrev = s.TravelMinutesFromPrev,
                    Status = s.Status.ToString(),
                    ArrivedAtUtc = s.ArrivedAt,
                    CompletedAtUtc = s.CompletedAt,
                    Note = s.Note,
                    DriverNote = s.DriverNote,
                    IssueCode = s.IssueCode,
                    FollowUpRequired = s.FollowUpRequired,
                    ProofStatus = s.ProofStatus.ToString(),
                    LastUpdatedByUserId = s.LastUpdatedByUserId,
                    LastUpdatedUtc = s.LastUpdatedUtc,
                    DriverInstruction = s.ServiceLocation?.DriverInstruction
                })
                .ToList()
        };

        return (dto, locationIds, null);
    }

    private sealed record WeightTemplateResult(WeightTemplate? Template, string? Error);

    private sealed record WeightSet(double Time, double Distance, double Date, double Cost, double Overtime);

    private sealed record CostSettings(decimal FuelCostPerKm, decimal PersonnelCostPerHour, string CurrencyCode);

    private static double NormalizeWeight(double? value)
    {
        var normalized = value ?? 1.0;
        if (normalized < 1.0) return 1.0;
        if (normalized > 100.0) return 100.0;
        return normalized;
    }

    private static WeightSet ResolveWeights(
        WeightTemplate? template,
        double? time,
        double? distance,
        double? date,
        double? cost,
        double? overtime)
    {
        if (template != null)
        {
            return new WeightSet(
                NormalizeWeight((double)template.WeightTravelTime),
                NormalizeWeight((double)template.WeightDistance),
                NormalizeWeight((double)template.WeightDate),
                NormalizeWeight((double)template.WeightCost),
                NormalizeWeight((double)template.WeightOvertime));
        }

        return new WeightSet(
            NormalizeWeight(time),
            NormalizeWeight(distance),
            NormalizeWeight(date),
            NormalizeWeight(cost),
            NormalizeWeight(overtime));
    }

    private async Task<CostSettings> GetCostSettingsAsync(CancellationToken cancellationToken)
    {
        var settings = await _dbContext.SystemCostSettings
            .AsNoTracking()
            .OrderByDescending(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (settings == null)
        {
            return new CostSettings(0m, 0m, "EUR");
        }

        return new CostSettings(
            settings.FuelCostPerKm,
            settings.PersonnelCostPerHour,
            string.IsNullOrWhiteSpace(settings.CurrencyCode) ? "EUR" : settings.CurrencyCode);
    }

    private async Task<WeightTemplateResult> ResolveWeightTemplateAsync(int? weightTemplateId, int ownerId, CancellationToken cancellationToken)
    {
        if (!weightTemplateId.HasValue)
        {
            return new WeightTemplateResult(null, null);
        }

        var template = await _dbContext.WeightTemplates
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == weightTemplateId.Value, cancellationToken);

        if (template == null || !template.IsActive)
        {
            return new WeightTemplateResult(null, "Weight template not found or inactive.");
        }

        if (!IsSuperAdmin && template.OwnerId.HasValue && template.OwnerId.Value != ownerId)
        {
            return new WeightTemplateResult(null, "Weight template is not available for this owner.");
        }

        return new WeightTemplateResult(template, null);
    }

    private async Task<Dictionary<int, TimeWindow>> LoadLocationWindowsAsync(
        List<int> locationIds,
        DateTime date,
        CancellationToken cancellationToken)
    {
        var distinctIds = locationIds.Distinct().ToList();
        if (distinctIds.Count == 0)
        {
            return new Dictionary<int, TimeWindow>();
        }

        var dateOnly = date.Date;
        var dayOfWeek = (int)dateOnly.DayOfWeek;

        var exceptions = await _dbContext.ServiceLocationExceptions
            .AsNoTracking()
            .Where(x => distinctIds.Contains(x.ServiceLocationId) && x.Date == dateOnly)
            .ToListAsync(cancellationToken);

        var exceptionLookup = exceptions
            .GroupBy(x => x.ServiceLocationId)
            .ToDictionary(g => g.Key, g => g.First());

        var hours = await _dbContext.ServiceLocationOpeningHours
            .AsNoTracking()
            .Where(x => distinctIds.Contains(x.ServiceLocationId) && x.DayOfWeek == dayOfWeek)
            .ToListAsync(cancellationToken);

        var hoursLookup = hours
            .GroupBy(x => x.ServiceLocationId)
            .ToDictionary(g => g.Key, g => g.First());

        var result = new Dictionary<int, TimeWindow>();
        foreach (var id in distinctIds)
        {
            if (exceptionLookup.TryGetValue(id, out var exception))
            {
                result[id] = TimeWindowHelper.BuildWindow(exception.IsClosed, exception.OpenTime, exception.CloseTime);
                continue;
            }

            if (hoursLookup.TryGetValue(id, out var open))
            {
                result[id] = TimeWindowHelper.BuildWindow(open.IsClosed, open.OpenTime, open.CloseTime);
            }
        }

        return result;
    }

    private async Task<Dictionary<int, ServiceLocationConstraint>> LoadLocationConstraintsAsync(
        List<int> locationIds,
        CancellationToken cancellationToken)
    {
        var distinctIds = locationIds.Distinct().ToList();
        if (distinctIds.Count == 0)
        {
            return new Dictionary<int, ServiceLocationConstraint>();
        }

        return await _dbContext.ServiceLocationConstraints
            .AsNoTracking()
            .Where(x => distinctIds.Contains(x.ServiceLocationId))
            .ToDictionaryAsync(x => x.ServiceLocationId, cancellationToken);
    }

    private int ResolveServiceMinutes(
        ServiceLocation location,
        Dictionary<int, ServiceLocationConstraint> constraints,
        Driver? driver = null)
    {
        var minutes = location.ServiceMinutes > 0 ? location.ServiceMinutes : (driver?.DefaultServiceMinutes ?? 20);

        if (constraints.TryGetValue(location.Id, out var constraint))
        {
            if (constraint.MinVisitDurationMinutes.HasValue)
            {
                minutes = Math.Max(minutes, constraint.MinVisitDurationMinutes.Value);
            }

            if (constraint.MaxVisitDurationMinutes.HasValue)
            {
                minutes = Math.Min(minutes, constraint.MaxVisitDurationMinutes.Value);
            }
        }

        return Math.Max(1, minutes);
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

    private static (double Lat, double Lng) ResolveRouteStart(RouteEntity? route, Driver driver)
    {
        if (route?.StartLatitude is double lat && route.StartLongitude is double lng)
        {
            return (lat, lng);
        }

        return (driver.StartLatitude ?? 0, driver.StartLongitude ?? 0);
    }

    private async Task ClearExistingRouteAsync(
        RouteEntity? existingRoute,
        List<int> previousServiceLocationIds,
        CancellationToken cancellationToken)
    {
        if (existingRoute == null)
        {
            return;
        }

        if (previousServiceLocationIds.Count > 0)
        {
            var today = DateTime.UtcNow.Date;
            var stillInAnyRoute = await _dbContext.RouteStops
                .Where(rs => rs.ServiceLocationId.HasValue
                    && previousServiceLocationIds.Contains(rs.ServiceLocationId.Value)
                    && rs.Route.Date >= today
                    && rs.RouteId != existingRoute.Id)
                .Select(rs => rs.ServiceLocationId!.Value)
                .Distinct()
                .ToListAsync(cancellationToken);

            var openIds = previousServiceLocationIds
                .Except(stillInAnyRoute)
                .Distinct()
                .ToList();

            if (openIds.Count > 0)
            {
                await _dbContext.ServiceLocations
                    .Where(sl => openIds.Contains(sl.Id) && sl.Status == ServiceLocationStatus.Planned)
                    .ExecuteUpdateAsync(s => s.SetProperty(sl => sl.Status, ServiceLocationStatus.Open), cancellationToken);
            }
        }

        await _dbContext.RouteStops
            .Where(rs => rs.RouteId == existingRoute.Id)
            .ExecuteDeleteAsync(cancellationToken);

        _dbContext.Routes.Remove(existingRoute);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task ClearTempRoutesForDayAsync(DateTime date, int ownerId, CancellationToken cancellationToken)
    {
        var routes = await _dbContext.Routes
            .Include(r => r.Stops)
            .Where(r => r.OwnerId == ownerId && r.Date.Date == date.Date && r.Status != RouteStatus.Fixed)
            .ToListAsync(cancellationToken);

        if (routes.Count == 0)
        {
            return;
        }

        var routeIds = routes.Select(r => r.Id).ToList();
        var removedLocationIds = routes
            .SelectMany(r => r.Stops)
            .Where(s => s.ServiceLocationId.HasValue)
            .Select(s => s.ServiceLocationId!.Value)
            .Distinct()
            .ToList();

        _dbContext.RouteStops.RemoveRange(routes.SelectMany(r => r.Stops));
        _dbContext.Routes.RemoveRange(routes);

        if (removedLocationIds.Count > 0)
        {
            var today = DateTime.UtcNow.Date;
            var stillInAnyRoute = await _dbContext.RouteStops
                .Where(rs => rs.ServiceLocationId.HasValue
                    && removedLocationIds.Contains(rs.ServiceLocationId.Value)
                    && rs.Route.OwnerId == ownerId
                    && rs.Route.Date >= today
                    && !routeIds.Contains(rs.RouteId))
                .Select(rs => rs.ServiceLocationId!.Value)
                .Distinct()
                .ToListAsync(cancellationToken);

            var openIds = removedLocationIds
                .Except(stillInAnyRoute)
                .Distinct()
                .ToList();

            if (openIds.Count > 0)
            {
                await _dbContext.ServiceLocations
                    .Where(sl => openIds.Contains(sl.Id) && sl.Status == ServiceLocationStatus.Planned)
                    .ExecuteUpdateAsync(s => s.SetProperty(sl => sl.Status, ServiceLocationStatus.Open), cancellationToken);
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task SyncPlannedStatusesAsync(int ownerId, CancellationToken cancellationToken)
    {
        var today = DateTime.UtcNow.Date;
        var plannedIds = await _dbContext.RouteStops
            .Where(rs => rs.ServiceLocationId.HasValue
                && rs.Route.OwnerId == ownerId
                && rs.Route.Date >= today)
            .Select(rs => rs.ServiceLocationId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (plannedIds.Count > 0)
        {
            await _dbContext.ServiceLocations
                .Where(sl => plannedIds.Contains(sl.Id)
                    && sl.Status != ServiceLocationStatus.Done
                    && sl.Status != ServiceLocationStatus.Cancelled)
                .ExecuteUpdateAsync(s => s.SetProperty(sl => sl.Status, ServiceLocationStatus.Planned), cancellationToken);
        }

        await _dbContext.ServiceLocations
            .Where(sl => sl.OwnerId == ownerId
                && sl.Status == ServiceLocationStatus.Planned
                && (plannedIds.Count == 0 || !plannedIds.Contains(sl.Id)))
            .ExecuteUpdateAsync(s => s.SetProperty(sl => sl.Status, ServiceLocationStatus.Open), cancellationToken);
    }
}
