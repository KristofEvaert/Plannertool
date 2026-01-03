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
using TransportPlanner.Infrastructure.Services.Vrp;
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
    public double? DueCostCapPercent { get; set; }
    public double? DetourCostCapPercent { get; set; }
    public double? DetourRefKmPercent { get; set; }
    public double? LateRefMinutesPercent { get; set; }
    public int? WeightTemplateId { get; set; }
    public bool? RequireServiceTypeMatch { get; set; }
    public bool? NormalizeWeights { get; set; }
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
    public double? DueCostCapPercent { get; set; }
    public double? DetourCostCapPercent { get; set; }
    public double? DetourRefKmPercent { get; set; }
    public double? LateRefMinutesPercent { get; set; }
    public int? WeightTemplateId { get; set; }
    public bool? RequireServiceTypeMatch { get; set; }
    public bool? NormalizeWeights { get; set; }
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
    private readonly IVrpRouteSolverService _vrpSolver;
    private readonly ILogger<AutoRoutesController> _logger;
    private bool IsSuperAdmin => User.IsInRole(AppRoles.SuperAdmin);
    private int? CurrentOwnerId => int.TryParse(User.FindFirstValue("ownerId"), out var id) ? id : null;
    private bool CanAccessOwner(int ownerId) => IsSuperAdmin || (CurrentOwnerId.HasValue && CurrentOwnerId.Value == ownerId);

    public AutoRoutesController(
        TransportPlannerDbContext dbContext,
        IRoutingService routing,
        ITravelTimeModelService travelTimeModel,
        IVrpRouteSolverService vrpSolver,
        ILogger<AutoRoutesController> logger)
    {
        _dbContext = dbContext;
        _routing = routing;
        _travelTimeModel = travelTimeModel;
        _vrpSolver = vrpSolver;
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

        var costSettings = await GetCostSettingsAsync(request.OwnerId, cancellationToken);
        var weightTemplateResult = await ResolveWeightTemplateAsync(request.WeightTemplateId, request.OwnerId, cancellationToken);
        if (!string.IsNullOrEmpty(weightTemplateResult.Error))
        {
            return BadRequest(new { message = weightTemplateResult.Error });
        }

        await ClearTempRoutesForDayAsync(date, request.OwnerId, cancellationToken);

        var weights = ResolveWeights(weightTemplateResult.Template, request.WeightTime, request.WeightDistance, request.WeightDate, request.WeightCost, request.WeightOvertime);
        var solverCaps = ResolveSolverCaps(
            weightTemplateResult.Template,
            request.DueCostCapPercent,
            request.DetourCostCapPercent,
            request.DetourRefKmPercent,
            request.LateRefMinutesPercent);
        var vrpRequest = new VrpSolveRequest(
            date,
            request.OwnerId,
            request.ServiceLocationToolIds,
            request.MaxStops,
            new VrpWeightSet(weights.Time, weights.Distance, weights.Date, weights.Cost, weights.Overtime),
            new VrpCostSettings(costSettings.FuelCostPerKm, costSettings.PersonnelCostPerHour, costSettings.CurrencyCode),
            request.RequireServiceTypeMatch == true,
            request.NormalizeWeights ?? true,
            weightTemplateResult.Template?.Id,
            solverCaps.DueCostCapPercent,
            solverCaps.DetourCostCapPercent,
            solverCaps.DetourRefKmPercent,
            solverCaps.LateRefMinutesPercent);

        VrpSolveResult result;
        try
        {
            result = await _vrpSolver.SolveDayAsync(vrpRequest, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Auto-generate failed for DriverId={DriverId}", driver.Id);
            return BadRequest(new { message = "Auto-generate failed." });
        }

        await SyncPlannedStatusesAsync(request.OwnerId, cancellationToken);

        var dto = result.Routes.FirstOrDefault(r => r.DriverId == driver.Id);
        if (dto == null)
        {
            return BadRequest(new { message = "No route generated for this driver." });
        }

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
        var costSettings = await GetCostSettingsAsync(request.OwnerId, cancellationToken);
        var weightTemplateResult = await ResolveWeightTemplateAsync(request.WeightTemplateId, request.OwnerId, cancellationToken);
        if (!string.IsNullOrEmpty(weightTemplateResult.Error))
        {
            return BadRequest(new { message = weightTemplateResult.Error });
        }

        await ClearTempRoutesForDayAsync(date, request.OwnerId, cancellationToken);
        var weights = ResolveWeights(weightTemplateResult.Template, request.WeightTime, request.WeightDistance, request.WeightDate, request.WeightCost, request.WeightOvertime);
        var solverCaps = ResolveSolverCaps(
            weightTemplateResult.Template,
            request.DueCostCapPercent,
            request.DetourCostCapPercent,
            request.DetourRefKmPercent,
            request.LateRefMinutesPercent);
        var vrpRequest = new VrpSolveRequest(
            date,
            request.OwnerId,
            request.ServiceLocationToolIds,
            request.MaxStopsPerDriver,
            new VrpWeightSet(weights.Time, weights.Distance, weights.Date, weights.Cost, weights.Overtime),
            new VrpCostSettings(costSettings.FuelCostPerKm, costSettings.PersonnelCostPerHour, costSettings.CurrencyCode),
            request.RequireServiceTypeMatch == true,
            request.NormalizeWeights ?? true,
            weightTemplateResult.Template?.Id,
            solverCaps.DueCostCapPercent,
            solverCaps.DetourCostCapPercent,
            solverCaps.DetourRefKmPercent,
            solverCaps.LateRefMinutesPercent);

        VrpSolveResult result;
        try
        {
            result = await _vrpSolver.SolveDayAsync(vrpRequest, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Auto-generate failed for owner {OwnerId}", request.OwnerId);
            return BadRequest(new { message = "Auto-generate failed." });
        }

        if (!result.Routes.Any())
        {
            return BadRequest(new { message = "No routes generated for any driver." });
        }

        await SyncPlannedStatusesAsync(request.OwnerId, cancellationToken);

        return Ok(new AutoGenerateAllResponse
        {
            Routes = result.Routes.ToList(),
            SkippedDrivers = result.SkippedDrivers.ToList()
        });
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
        bool normalizeWeights,
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
        var normalizedWeights = BuildNormalizedWeights(weights, normalizeWeights);
        var normalizationRefs = await BuildNormalizationReferencesAsync(
            date,
            startMinuteOfDay,
            startPoint.Lat,
            startPoint.Lng,
            remaining,
            locationWindows,
            locationConstraints,
            driver,
            costSettings,
            cancellationToken);
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

                var timeCost = travelMinutesRounded + waitMinutes + serviceMinutes;
                var projectedUsedMinutes = usedMinutes + travelMinutesRounded + waitMinutes + serviceMinutes;
                var projectedEndMinute = startMinuteOfDay + projectedUsedMinutes;
                var overtimeMinutes = Math.Max(0, projectedEndMinute - endMinuteOfDay);
                var costTravel = CostCalculator.CalculateTravelCost(
                    travelKm,
                    travelMinutes,
                    costSettings.FuelCostPerKm,
                    costSettings.PersonnelCostPerHour);
                var dueUrgency = ComputeDueUrgencyNormalized(date, loc, travelMinutes);
                var duePenalty = 1 - dueUrgency;

                var normDistance = Clamp01(travelKm / normalizationRefs.DistanceKm);
                var normTime = Clamp01(timeCost / normalizationRefs.TimeMinutes);
                var normOvertime = Clamp01(overtimeMinutes / normalizationRefs.OvertimeMinutes);
                var normCost = Clamp01(costTravel / normalizationRefs.CostEuro);

                var score = (normalizedWeights.Time * normTime)
                            + (normalizedWeights.Distance * normDistance)
                            + (normalizedWeights.Date * duePenalty)
                            + (normalizedWeights.Cost * normCost)
                            + (normalizedWeights.Overtime * normOvertime);

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

        // Add final leg back to the route end (driver default or override).
        var endPoint = ResolveRouteEnd(existingRoute, driver);
        if (selected.Any())
        {
            DrivingRouteResult? endTravel = null;
            try
            {
                endTravel = await _routing.GetDrivingRouteAsync(new List<RoutePoint>
                {
                    new RoutePoint(lastLat, lastLng),
                    new RoutePoint(endPoint.Lat, endPoint.Lng)
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "OSRM return leg failed for DriverId={DriverId}", driver.Id);
            }

            var endMinutes = endTravel != null
                ? endTravel.TotalDurationMinutes
                : EstimateMinutes(lastLat, lastLng, endPoint.Lat, endPoint.Lng);
            var endKm = endTravel?.TotalDistanceKm ?? HaversineKm(lastLat, lastLng, endPoint.Lat, endPoint.Lng);
            totalKm += endKm;
            totalTravelMinutes += endMinutes;
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

    private sealed record NormalizedWeightSet(double Time, double Distance, double Date, double Cost, double Overtime);

    private sealed record NormalizationReferences(double DistanceKm, double TimeMinutes, double CostEuro, double OvertimeMinutes);

    private sealed record CostSettings(decimal FuelCostPerKm, decimal PersonnelCostPerHour, string CurrencyCode);

    private sealed record SolverCapPercents(
        double DueCostCapPercent,
        double DetourCostCapPercent,
        double DetourRefKmPercent,
        double LateRefMinutesPercent);

    private static double NormalizeWeight(double? value)
    {
        var normalized = value ?? 1.0;
        if (normalized < 0.0) return 0.0;
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

    private static SolverCapPercents ResolveSolverCaps(
        WeightTemplate? template,
        double? dueCostCapPercent,
        double? detourCostCapPercent,
        double? detourRefKmPercent,
        double? lateRefMinutesPercent)
    {
        if (template != null)
        {
            return new SolverCapPercents(
                NormalizePercent((double)template.DueCostCapPercent),
                NormalizePercent((double)template.DetourCostCapPercent),
                NormalizePercent((double)template.DetourRefKmPercent),
                NormalizePercent((double)template.LateRefMinutesPercent));
        }

        return new SolverCapPercents(
            NormalizePercent(dueCostCapPercent ?? 50),
            NormalizePercent(detourCostCapPercent ?? 50),
            NormalizePercent(detourRefKmPercent ?? 50),
            NormalizePercent(lateRefMinutesPercent ?? 50));
    }

    private static NormalizedWeightSet BuildNormalizedWeights(WeightSet weights, bool normalizeWeights)
    {
        var time = Math.Max(0, weights.Time);
        var distance = Math.Max(0, weights.Distance);
        var date = Math.Max(0, weights.Date);
        var cost = Math.Max(0, weights.Cost);
        var overtime = Math.Max(0, weights.Overtime);

        if (cost > 0)
        {
            distance = 0;
        }

        var raw = new[] { time, distance, date, cost, overtime };
        var gamma = DetermineGamma(raw);
        var scored = raw.Select(value => value <= 0 ? 0 : Math.Pow(value / 100.0, gamma)).ToArray();

        if (normalizeWeights)
        {
            var sum = scored.Sum();
            if (sum > 0)
            {
                for (var i = 0; i < scored.Length; i++)
                {
                    scored[i] /= sum;
                }
            }
        }

        return new NormalizedWeightSet(scored[0], scored[1], scored[2], scored[3], scored[4]);
    }

    private static double DetermineGamma(IReadOnlyList<double> rawPercents)
    {
        var max = rawPercents.Max();
        if (max <= 0)
        {
            return 2.0;
        }

        var dominant = max >= 90 && rawPercents.Where(p => p != max).All(p => p <= 10);
        return dominant ? 3.0 : 2.0;
    }

    private static double NormalizePercent(double value)
    {
        if (value < 0) return 0;
        if (value > 100) return 100;
        return value;
    }

    private async Task<NormalizationReferences> BuildNormalizationReferencesAsync(
        DateTime date,
        int startMinuteOfDay,
        double startLat,
        double startLng,
        List<ServiceLocation> candidates,
        Dictionary<int, TimeWindow> locationWindows,
        Dictionary<int, ServiceLocationConstraint> locationConstraints,
        Driver driver,
        CostSettings costSettings,
        CancellationToken cancellationToken)
    {
        const double defaultDistance = 60;
        const double defaultTime = 120;
        const double defaultCost = 50;
        const double defaultOvertime = 60;

        var distanceSamples = new List<double>();
        var timeSamples = new List<double>();
        var costSamples = new List<double>();

        foreach (var loc in candidates)
        {
            if (!locationWindows.TryGetValue(loc.Id, out var window))
            {
                window = TimeWindow.AlwaysOpen;
            }

            if (window.IsClosed)
            {
                continue;
            }

            var travelKm = HaversineKm(startLat, startLng, loc.Latitude ?? 0, loc.Longitude ?? 0);
            var travelMinutes = await _travelTimeModel.EstimateMinutesAsync(
                date,
                startMinuteOfDay,
                travelKm,
                startLat,
                startLng,
                loc.Latitude ?? 0,
                loc.Longitude ?? 0,
                cancellationToken);
            var travelMinutesRounded = (int)Math.Round(travelMinutes, MidpointRounding.AwayFromZero);
            var serviceMinutes = ResolveServiceMinutes(loc, locationConstraints, driver);
            var arrivalMinute = startMinuteOfDay + travelMinutesRounded;

            if (!TimeWindowHelper.TrySchedule(
                    window,
                    arrivalMinute,
                    serviceMinutes,
                    out var waitMinutes,
                    out _,
                    out _))
            {
                continue;
            }

            var timeCost = travelMinutesRounded + waitMinutes + serviceMinutes;
            distanceSamples.Add(travelKm);
            timeSamples.Add(timeCost);
            costSamples.Add(CostCalculator.CalculateTravelCost(
                travelKm,
                travelMinutes,
                costSettings.FuelCostPerKm,
                costSettings.PersonnelCostPerHour));
        }

        var distanceRef = ComputePercentile(distanceSamples, 0.9);
        var timeRef = ComputePercentile(timeSamples, 0.9);
        var costRef = ComputePercentile(costSamples, 0.9);

        if (distanceRef <= 0) distanceRef = defaultDistance;
        if (timeRef <= 0) timeRef = defaultTime;
        if (costRef <= 0) costRef = defaultCost;

        return new NormalizationReferences(distanceRef, timeRef, costRef, defaultOvertime);
    }

    private static double ComputePercentile(IReadOnlyList<double> values, double percentile)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        var ordered = values.Where(v => v > 0).OrderBy(v => v).ToArray();
        if (ordered.Length == 0)
        {
            return 0;
        }

        var rank = (ordered.Length - 1) * percentile;
        var low = (int)Math.Floor(rank);
        var high = (int)Math.Ceiling(rank);
        if (low == high)
        {
            return ordered[low];
        }

        var weight = rank - low;
        return ordered[low] + (ordered[high] - ordered[low]) * weight;
    }

    private static double ComputeDueUrgencyNormalized(DateTime scheduleDate, ServiceLocation location, double travelMinutes)
    {
        var orderDate = (location.PriorityDate ?? location.DueDate).Date;
        if (orderDate == default)
        {
            return 0;
        }

        var daysRemaining = (orderDate - scheduleDate.Date).TotalDays;
        double urgency;

        if (daysRemaining < 0)
        {
            urgency = 1.0;
        }
        else if (daysRemaining <= 7)
        {
            urgency = 1.0 - (daysRemaining / 7.0) * 0.2;
        }
        else if (daysRemaining <= 14)
        {
            urgency = 0.8 - ((daysRemaining - 7.0) / 7.0) * 0.3;
        }
        else if (daysRemaining <= 28)
        {
            urgency = 0.5 - ((daysRemaining - 14.0) / 14.0) * 0.3;
        }
        else
        {
            urgency = 0.2 - Math.Min((daysRemaining - 28.0) / 28.0, 1.0) * 0.2;
        }

        if (daysRemaining > 0 && travelMinutes < 10)
        {
            urgency -= 0.05;
        }

        return Clamp01(urgency);
    }

    private static double Clamp01(double value) => Math.Max(0, Math.Min(1, value));

    private async Task<CostSettings> GetCostSettingsAsync(int ownerId, CancellationToken cancellationToken)
    {
        var settings = await _dbContext.SystemCostSettings
            .AsNoTracking()
            .Where(x => x.OwnerId == ownerId)
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
                result[id] = TimeWindowHelper.BuildWindow(open.IsClosed, open.OpenTime, open.CloseTime, open.OpenTime2, open.CloseTime2);
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

    private static (double Lat, double Lng) ResolveRouteEnd(RouteEntity? route, Driver driver)
    {
        if (route?.EndLatitude is double lat && route.EndLongitude is double lng)
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
