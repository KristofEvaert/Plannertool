using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TransportPlanner.Application.DTOs;
using TransportPlanner.Application.Interfaces;
using TransportPlanner.Domain;
using TransportPlanner.Domain.Entities;
using TransportPlanner.Infrastructure.Data;
using TransportPlanner.Infrastructure.Options;

namespace TransportPlanner.Infrastructure.Services;

public class PlanGenerationService : IPlanGenerationService
{
    private readonly TransportPlannerDbContext _dbContext;
    private readonly ITravelTimeService _travelTimeService;
    private readonly IRoutePlanner _routePlanner;
    private readonly MultiDayClusteringService _clusteringService;
    private readonly ILogger<PlanGenerationService> _logger;
    private readonly PlanningOptions _planningOptions;

    public PlanGenerationService(
        TransportPlannerDbContext dbContext, 
        ITravelTimeService travelTimeService,
        IRoutePlanner routePlanner,
        ILogger<PlanGenerationService> logger,
        IOptions<PlanningOptions> planningOptions)
    {
        _dbContext = dbContext;
        _travelTimeService = travelTimeService;
        _routePlanner = routePlanner;
        _logger = logger;
        _planningOptions = planningOptions.Value;
        _clusteringService = new MultiDayClusteringService();
    }

    public async Task<GeneratePlanResultDto> GenerateAsync(DateTime fromDate, int days, CancellationToken cancellationToken = default)
    {
        var result = new GeneratePlanResultDto
        {
            FromDate = fromDate.Date,
            Days = days
        };

        // Back to basics: day-by-day planning until backlog empty
        var startDate = fromDate.Date;
        var daysCap = days > 0 ? days : 30; // Safety limit, default 30
        var backlog = await GetBacklogPolesOrderedAsync(cancellationToken);
        
        _logger.LogInformation(
            "Starting generation from {StartDate} with {BacklogCount} poles in backlog (daysCap={DaysCap})",
            startDate, backlog.Count, daysCap);

        var totalPlannedStops = 0;
        var totalCreatedRoutes = 0;
        var actualDaysUsed = 0;

        // Day-by-day loop until backlog empty or daysCap reached
        for (int i = 0; i < daysCap; i++)
        {
            if (backlog.Count == 0)
            {
                _logger.LogInformation("Backlog empty, stopping generation after {DaysUsed} days", actualDaysUsed);
                break;
            }

            var currentDay = startDate.AddDays(i);
            
            // Check if day is locked
            var planDay = await _dbContext.PlanDays
                .AsNoTracking()
                .FirstOrDefaultAsync(pd => pd.Date.Date == currentDay.Date, cancellationToken);
            
            if (planDay != null && planDay.IsLocked)
            {
                result.SkippedLockedDays++;
                _logger.LogInformation("Day {Date} is locked, skipping", currentDay);
                continue;
            }

            // Generate day and update backlog
            var dayResult = await GenerateDayFromBacklogAsync(
                currentDay, 
                backlog, 
                cancellationToken);
            
            actualDaysUsed++;
            result.GeneratedDays++;
            result.PlannedPolesCount += dayResult.AssignedPolesCount;
            totalPlannedStops += dayResult.StopsCreated;
            totalCreatedRoutes += dayResult.RoutesCreated;
            
            // Remove assigned poles from backlog
            backlog = backlog
                .Where(p => !dayResult.AssignedPoleIds.Contains(p.Id))
                .ToList();
            
            _logger.LogInformation(
                "DAY {Day}: candidates={Candidates}, drivers={Drivers}, assigned={Assigned}, remainingBacklog={Backlog}, routesSaved={Routes}, stopsSaved={Stops}",
                currentDay.ToString("yyyy-MM-dd"),
                dayResult.CandidatesCount,
                dayResult.DriversCount,
                dayResult.AssignedPolesCount,
                backlog.Count,
                dayResult.RoutesCreated,
                dayResult.StopsCreated);
        }

        result.PlannedStops = totalPlannedStops;
        result.CreatedRoutes = totalCreatedRoutes;
        result.RemainingBacklogCount = backlog.Count;
        result.UnplannedPolesCount = backlog.Count;

        _logger.LogInformation(
            "Generation complete: {DaysUsed} days generated, {PlannedStops} stops planned, {RoutesCreated} routes created, {RemainingBacklog} poles remaining",
            actualDaysUsed, totalPlannedStops, totalCreatedRoutes, backlog.Count);

        return result;
    }

    private async Task<List<Pole>> GetBacklogPolesOrderedAsync(CancellationToken cancellationToken)
    {
        // Get all open poles (Status NOT IN (Done, Cancelled))
        var allOpenPoles = await _dbContext.Poles
            .AsNoTracking()
            .Where(p => p.Status != PoleStatus.Done && p.Status != PoleStatus.Cancelled)
            .ToListAsync(cancellationToken);

        // Exclude poles already planned (exist in RouteStops join Routes where Route.Status != Cancelled)
        // For simplicity, exclude any pole that has a RouteStop (assumes routes are not cancelled)
        var plannedPoleIds = await _dbContext.RouteStops
            .AsNoTracking()
            .Join(
                _dbContext.Routes.AsNoTracking(),
                rs => rs.RouteId,
                r => r.Id,
                (rs, r) => rs.PoleId)
            .Distinct()
            .ToListAsync(cancellationToken);

        // Backlog: open poles NOT already planned
        var backlog = allOpenPoles
            .Where(p => !plannedPoleIds.Contains(p.Id))
            .OrderBy(p => p.DueDate) // Order by DueDate ascending (earliest first) - for priority only
            .ThenBy(p => p.Id) // Then by Id for stability
            .ToList();

        return backlog;
    }

    private async Task<(int CandidatesCount, int DriversCount, int AssignedPolesCount, int RoutesCreated, int StopsCreated, HashSet<int> AssignedPoleIds)> GenerateDayFromBacklogAsync(
        DateTime currentDay,
        List<Pole> backlog,
        CancellationToken cancellationToken)
    {
        var assignedPoleIds = new HashSet<int>();
        var routesCreated = 0;
        var stopsCreated = 0;

        // Load available drivers for that day
        var drivers = await _dbContext.Drivers
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var availabilities = await _dbContext.DriverAvailabilities
            .AsNoTracking()
            .Where(da => da.Date.Date == currentDay.Date)
            .ToDictionaryAsync(da => da.DriverId, da => da, cancellationToken);

        // Get available drivers with valid availability
        var availableDrivers = drivers
            .Where(d => availabilities.ContainsKey(d.Id))
            .Select(driver =>
            {
                var availability = availabilities[driver.Id];
                var availabilityMinutes = (int)(availability.EndTime - availability.StartTime).TotalMinutes;
                
                // Get extra work minutes for this day
                var settings = _dbContext.PlanDaySettings
                    .AsNoTracking()
                    .FirstOrDefault(s => s.Date.Date == currentDay.Date);
                var extraWorkMinutes = settings?.ExtraWorkMinutes ?? 0;
                
                var effectiveMax = Math.Min(availabilityMinutes, driver.MaxWorkMinutesPerDay + extraWorkMinutes);
                
                return new
                {
                    Driver = driver,
                    Availability = availability,
                    EffectiveMaxMinutes = effectiveMax,
                    AvailabilityMinutes = availabilityMinutes
                };
            })
            .Where(x => x.EffectiveMaxMinutes > 0) // Skip drivers with no available time
            .ToList();

        if (!availableDrivers.Any() || backlog.Count == 0)
        {
            return (backlog.Count, 0, 0, 0, 0, assignedPoleIds);
        }

        // Prepare OR-Tools input: take first N poles from backlog (ordered by DueDate)
        var candidateLimit = _planningOptions.OrTools?.MaxDailyCandidates ?? 300;
        var candidatePoles = backlog.Take(candidateLimit).ToList();

        // Build location list for travel time matrix
        var locations = new List<GeoLocation>();
        
        // Add driver start locations first
        foreach (var driverInfo in availableDrivers)
        {
            locations.Add(new GeoLocation((double)(driverInfo.Driver.StartLatitude ?? 0), (double)(driverInfo.Driver.StartLongitude ?? 0)));
        }

        // Add pole locations
        foreach (var pole in candidatePoles)
        {
            locations.Add(new GeoLocation((double)pole.Latitude, (double)pole.Longitude));
        }

        // Compute travel time matrix
        var travelTimeMatrix = await _travelTimeService.GetTravelTimeMatrixAsync(locations, cancellationToken);

        // Build input for route planner
        var planInput = new PlanDayInput
        {
            Date = currentDay,
            Drivers = availableDrivers.Select(driverInfo =>
            {
                var availability = driverInfo.Availability;
                return new DriverInput
                {
                    Id = driverInfo.Driver.Id,
                    StartLatitude = driverInfo.Driver.StartLatitude ?? 0,
                    StartLongitude = driverInfo.Driver.StartLongitude ?? 0,
                    AvailabilityMinutes = driverInfo.AvailabilityMinutes,
                    MaxWorkMinutesPerDay = driverInfo.EffectiveMaxMinutes,
                    DefaultServiceMinutes = driverInfo.Driver.DefaultServiceMinutes,
                    StartTime = availability.StartTime,
                    EndTime = availability.EndTime
                };
            }).ToList(),
            Poles = candidatePoles.Select(pole =>
            {
                var serviceMinutes = availableDrivers.First().Driver.DefaultServiceMinutes;
                return new PoleInput
                {
                    Id = pole.Id,
                    Latitude = pole.Latitude,
                    Longitude = pole.Longitude,
                    ServiceMinutes = serviceMinutes,
                    DueDate = pole.DueDate,
                    FixedDate = pole.FixedDate,
                    IsFixedForDate = false // No fixed-date logic for now
                };
            }).ToList()
        };

        // Solve with OR-Tools
        var plannedResult = await _routePlanner.PlanDayAsync(planInput, travelTimeMatrix, cancellationToken);

        // Extract assigned pole IDs
        foreach (var driverRoute in plannedResult.DriverRoutes)
        {
            foreach (var assignment in driverRoute.PoleAssignments)
            {
                assignedPoleIds.Add(assignment.PoleId);
            }
        }

        // Persist routes and stops for this day
        using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // Delete existing routes for this day (if not locked)
            var existingRoutes = await _dbContext.Routes
                .Where(r => r.Date.Date == currentDay.Date && !r.IsLocked)
                .ToListAsync(cancellationToken);

            if (existingRoutes.Any())
            {
                var routeIds = existingRoutes.Select(r => r.Id).ToList();
                var existingStops = await _dbContext.RouteStops
                    .Where(rs => routeIds.Contains(rs.RouteId))
                    .ToListAsync(cancellationToken);

                _dbContext.RouteStops.RemoveRange(existingStops);
                _dbContext.Routes.RemoveRange(existingRoutes);
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            // Create routes and stops
            var poleLookup = candidatePoles.ToDictionary(p => p.Id);
            var driverLookup = availableDrivers.ToDictionary(d => d.Driver.Id, d => d);

            foreach (var driverRoute in plannedResult.DriverRoutes)
            {
                if (!driverRoute.PoleAssignments.Any())
                    continue;

                var driverInfo = driverLookup[driverRoute.DriverId];
                var driver = driverInfo.Driver;
                var availability = driverInfo.Availability;
                var routeStartTime = currentDay.Date.Add(availability.StartTime);

                // Calculate total minutes and km
                var totalMinutes = 0;
                decimal totalKm = 0;
                var startLocation = new GeoLocation((double)(driver.StartLatitude ?? 0), (double)(driver.StartLongitude ?? 0));
                GeoLocation? prevLocation = startLocation;

                foreach (var assignment in driverRoute.PoleAssignments.OrderBy(a => a.Sequence))
                {
                    var pole = poleLookup[assignment.PoleId];
                    var currentLocation = new GeoLocation((double)pole.Latitude, (double)pole.Longitude);
                    var serviceMinutes = availableDrivers.First().Driver.DefaultServiceMinutes;
                    totalMinutes += assignment.TravelMinutesFromPrev + serviceMinutes;
                    
                    assignment.TravelKmFromPrev = (decimal)CalculateKmBetween(prevLocation, currentLocation);
                    totalKm += assignment.TravelKmFromPrev;
                    
                    prevLocation = currentLocation;
                }

                // Add return trip
                if (prevLocation != null)
                {
                    var returnKm = CalculateKmBetween(prevLocation, startLocation);
                    var returnMinutes = await _travelTimeService.GetTravelTimeAsync(
                        prevLocation, 
                        startLocation, 
                        cancellationToken);
                    totalKm += (decimal)returnKm;
                    totalMinutes += returnMinutes;
                }

                var route = new Route
                {
                    Date = currentDay.Date,
                    DriverId = driver.Id,
                    IsLocked = false,
                    TotalMinutes = totalMinutes,
                    TotalKm = totalKm
                };

                _dbContext.Routes.Add(route);
                await _dbContext.SaveChangesAsync(cancellationToken);
                routesCreated++;

                // Create route stops
                var currentTime = routeStartTime;
                GeoLocation? prevStopLocation = new GeoLocation((double)(driver.StartLatitude ?? 0), (double)(driver.StartLongitude ?? 0));
                
                foreach (var assignment in driverRoute.PoleAssignments.OrderBy(a => a.Sequence))
                {
                    var pole = poleLookup[assignment.PoleId];
                    currentTime = currentTime.AddMinutes(assignment.TravelMinutesFromPrev);
                    
                    var serviceMinutes = availableDrivers.First().Driver.DefaultServiceMinutes;
                    var plannedStart = currentTime;
                    var plannedEnd = currentTime.AddMinutes(serviceMinutes);

                    // Calculate km from previous stop
                    var currentStopLocation = new GeoLocation((double)pole.Latitude, (double)pole.Longitude);
                    var kmFromPrev = CalculateKmBetween(prevStopLocation, currentStopLocation);

                    var routeStop = new RouteStop
                    {
                        RouteId = route.Id,
                        Sequence = assignment.Sequence,
                        PoleId = pole.Id,
                        PlannedStart = plannedStart,
                        PlannedEnd = plannedEnd,
                        TravelMinutesFromPrev = assignment.TravelMinutesFromPrev,
                        TravelKmFromPrev = (decimal)kmFromPrev,
                        Status = RouteStopStatus.Pending
                    };

                    _dbContext.RouteStops.Add(routeStop);
                    currentTime = plannedEnd;
                    prevStopLocation = currentStopLocation;
                    stopsCreated++;
                }

                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        // Verify persistence
        var savedRoutesCount = await _dbContext.Routes
            .AsNoTracking()
            .Where(r => r.Date.Date == currentDay.Date)
            .CountAsync(cancellationToken);

        var savedStopsCount = await _dbContext.RouteStops
            .AsNoTracking()
            .Join(
                _dbContext.Routes.AsNoTracking().Where(r => r.Date.Date == currentDay.Date),
                rs => rs.RouteId,
                r => r.Id,
                (rs, r) => rs)
            .CountAsync(cancellationToken);

        if (assignedPoleIds.Count > 0 && savedStopsCount == 0)
        {
            var errorMessage = $"Solver assigned stops but persistence wrote 0 stops: Day {currentDay:yyyy-MM-dd}";
            _logger.LogError(errorMessage);
            var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
            if (environment == "Development")
            {
                throw new InvalidOperationException(errorMessage);
            }
        }

        return (candidatePoles.Count, availableDrivers.Count, assignedPoleIds.Count, routesCreated, stopsCreated, assignedPoleIds);
    }

    private async Task<(int DueTodayUnplannedCount, int OverdueCount)> ComputeEndOfWindowShortageAsync(
        DateTime fromDate,
        DateTime windowEnd,
        HashSet<int> plannedPoleIds,
        CancellationToken cancellationToken)
    {
        // Get all poles that are not Done/Cancelled
        var allPoles = await _dbContext.Poles
            .AsNoTracking()
            .Where(p => p.Status != PoleStatus.Done && p.Status != PoleStatus.Cancelled)
            .ToListAsync(cancellationToken);

        // Get poles planned within the window
        var plannedInWindow = await _dbContext.Routes
            .AsNoTracking()
            .Where(r => r.Date >= fromDate && r.Date <= windowEnd)
            .SelectMany(r => _dbContext.RouteStops
                .AsNoTracking()
                .Where(rs => rs.RouteId == r.Id)
                .Select(rs => rs.PoleId))
            .Distinct()
            .ToListAsync(cancellationToken);

        // Combine in-memory and DB planned poles
        var allPlannedPoleIds = plannedPoleIds.Union(plannedInWindow).ToHashSet();

        // DueTodayUnplannedCountAtEnd: poles due on windowEnd that are not planned
        var dueTodayUnplannedCount = allPoles
            .Where(p => p.DueDate.Date == windowEnd)
            .Where(p => !allPlannedPoleIds.Contains(p.Id))
            .Count();

        // OverdueCountAtEnd: poles with DueDate < windowEnd that are not planned
        var overdueCount = allPoles
            .Where(p => p.DueDate.Date < windowEnd)
            .Where(p => !allPlannedPoleIds.Contains(p.Id))
            .Count();

        return (dueTodayUnplannedCount, overdueCount);
    }

    private async Task<GeneratePlanResultDto> GenerateWithClusteringAsync(
        DateTime fromDate, 
        int days, 
        CancellationToken cancellationToken)
    {
        var result = new GeneratePlanResultDto
        {
            FromDate = fromDate.Date,
            Days = days
        };

        // Build list of available days (excluding locked days)
        var availableDays = new List<DateTime>();
        for (int i = 0; i < days; i++)
        {
            var date = fromDate.Date.AddDays(i);
            var planDay = await _dbContext.PlanDays
                .AsNoTracking()
                .FirstOrDefaultAsync(pd => pd.Date == date, cancellationToken);
            
            if (planDay == null || !planDay.IsLocked)
            {
                availableDays.Add(date);
            }
            else
            {
                result.SkippedLockedDays++;
            }
        }

        if (!availableDays.Any())
        {
            return result;
        }

        // Load all candidate poles for the entire period
        var allCandidatePoles = await GetAllCandidatePolesAsync(
            fromDate.Date, 
            fromDate.Date.AddDays(days - 1), 
            cancellationToken);

        if (!allCandidatePoles.Any())
        {
            return result;
        }

        // Separate late poles (DueDate < fromDate) - these should not be scheduled
        var latePoles = allCandidatePoles
            .Where(p => !p.FixedDate.HasValue && p.DueDate.Date < fromDate.Date)
            .ToList();

        // Filter out late poles from candidates
        var eligiblePoles = allCandidatePoles
            .Where(p => !latePoles.Contains(p))
            .ToList();

        // Convert to PoleWithDate for clustering
        // EligibleFromDate is now fromDate (can schedule earlier than due date)
        var polesWithDate = eligiblePoles.Select(pole => new PoleWithDate
        {
            Pole = pole,
            EligibleFromDate = fromDate.Date, // Can schedule from start of window
            MustBeOnDate = pole.FixedDate
        }).ToList();

        // Cluster poles by geographic proximity
        var clusters = _clusteringService.ClusterPoles(polesWithDate);

        // Assign poles from clusters to days (avoiding consecutive day visits)
        var poleAssignments = _clusteringService.AssignPolesToDays(clusters, availableDays);

        // Generate routes for each day with assigned poles
        foreach (var day in availableDays.OrderBy(d => d))
        {
            var assignedPoles = poleAssignments[day];
            if (!assignedPoles.Any())
            {
                continue;
            }

            var dayResult = await GenerateDayWithPolesAsync(day, assignedPoles.Select(p => p.Pole).ToList(), cancellationToken);
            
            result.GeneratedDays += dayResult.GeneratedDays;
            result.PlannedPolesCount += dayResult.PlannedPolesCount;
            result.UnplannedPolesCount += dayResult.UnplannedPolesCount;
        }

        // Count unique late poles
        result.LatePolesCount = latePoles.Select(p => p.Id).Distinct().Count();

        // Compute end-of-window shortage indicators
        var windowEnd = fromDate.Date.AddDays(days - 1);
        var plannedPoleIdsInWindow = await _dbContext.Routes
            .AsNoTracking()
            .Where(r => r.Date >= fromDate.Date && r.Date <= windowEnd)
            .SelectMany(r => _dbContext.RouteStops
                .AsNoTracking()
                .Where(rs => rs.RouteId == r.Id)
                .Select(rs => rs.PoleId))
            .Distinct()
            .ToListAsync(cancellationToken);

        var shortageInfo = await ComputeEndOfWindowShortageAsync(fromDate.Date, windowEnd, plannedPoleIdsInWindow.ToHashSet(), cancellationToken);
        result.DueTodayUnplannedCountAtEnd = shortageInfo.DueTodayUnplannedCount;
        result.OverdueCountAtEnd = shortageInfo.OverdueCount;

        // Compute UnplannedPolesCount: poles with Status not Done/Cancelled, FixedDate null,
        // DueDate <= windowEnd, EXCLUDING poles planned within [fromDate..windowEnd]
        var allEligiblePoles = await _dbContext.Poles
            .AsNoTracking()
            .Where(p => p.Status != PoleStatus.Done && p.Status != PoleStatus.Cancelled)
            .Where(p => p.FixedDate == null && p.DueDate.Date <= windowEnd)
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);

        result.UnplannedPolesCount = allEligiblePoles
            .Where(poleId => !plannedPoleIdsInWindow.Contains(poleId))
            .Count();

        return result;
    }

    private async Task<List<Pole>> GetAllCandidatePolesAsync(
        DateTime fromDate, 
        DateTime toDate, 
        CancellationToken cancellationToken)
    {
        // Get all poles that could be planned in this period using new eligibility rules
        // A pole is "in window" if:
        // - Status is not Done/Cancelled
        // - FixedDate is within the window OR
        // - FixedDate is null AND DueDate is between fromDate and windowEnd (inclusive)
        var allPoles = await _dbContext.Poles
            .AsNoTracking()
            .Where(p => p.Status != PoleStatus.Done && p.Status != PoleStatus.Cancelled)
            .ToListAsync(cancellationToken);

        // Filter by eligibility window
        var eligiblePoles = allPoles
            .Where(p =>
            {
                if (p.FixedDate.HasValue)
                {
                    // Fixed date poles must be within the window
                    return p.FixedDate.Value.Date >= fromDate && p.FixedDate.Value.Date <= toDate;
                }
                else
                {
                    // Non-fixed poles: DueDate must be between fromDate and toDate (inclusive)
                    return p.DueDate.Date >= fromDate && p.DueDate.Date <= toDate;
                }
            })
            .ToList();

        // Exclude already planned poles in this period
        var routesInPeriod = await _dbContext.Routes
            .AsNoTracking()
            .Where(r => r.Date >= fromDate && r.Date <= toDate)
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);

        var plannedPoleIds = routesInPeriod.Any()
            ? await _dbContext.RouteStops
                .AsNoTracking()
                .Where(rs => routesInPeriod.Contains(rs.RouteId))
                .Select(rs => rs.PoleId)
                .Distinct()
                .ToListAsync(cancellationToken)
            : new List<int>();

        return eligiblePoles
            .Where(p => !plannedPoleIds.Contains(p.Id))
            .ToList();
    }

    private async Task<(int GeneratedDays, int PlannedPolesCount, int UnplannedPolesCount)> GenerateDayWithPolesAsync(
        DateTime dateOnly,
        List<Pole> candidatePoles,
        CancellationToken cancellationToken)
    {
        // Use transaction for per-day generation
        using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // Ensure PlanDay exists
            var planDay = await _dbContext.PlanDays
                .FirstOrDefaultAsync(pd => pd.Date == dateOnly, cancellationToken);

            if (planDay == null)
            {
                planDay = new PlanDay
                {
                    Date = dateOnly,
                    IsLocked = false
                };
                _dbContext.PlanDays.Add(planDay);
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            // If day is locked, skip
            if (planDay.IsLocked)
            {
                await transaction.CommitAsync(cancellationToken);
                return (0, 0, candidatePoles.Count);
            }

            // Delete existing routes for this day (only if not locked)
            var existingRoutes = await _dbContext.Routes
                .Where(r => r.Date.Date == dateOnly.Date && !r.IsLocked)
                .ToListAsync(cancellationToken);

            if (existingRoutes.Any())
            {
                var routeIds = existingRoutes.Select(r => r.Id).ToList();
                var existingStops = await _dbContext.RouteStops
                    .Where(rs => routeIds.Contains(rs.RouteId))
                    .ToListAsync(cancellationToken);

                _dbContext.RouteStops.RemoveRange(existingStops);
                _dbContext.Routes.RemoveRange(existingRoutes);
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            var dayResult = await GenerateDayInternalWithPolesAsync(dateOnly, candidatePoles, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            
            return (1, dayResult.PlannedPolesCount, dayResult.UnplannedPolesCount);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private async Task<(int PlannedPolesCount, int UnplannedPolesCount)> GenerateDayInternalWithPolesAsync(
        DateTime dateOnly,
        List<Pole> candidatePoles,
        CancellationToken cancellationToken)
    {
        // Load drivers
        var drivers = await _dbContext.Drivers
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        if (!drivers.Any())
        {
            return (0, candidatePoles.Count);
        }

        // Load driver availability for this day
        var availabilities = await _dbContext.DriverAvailabilities
            .AsNoTracking()
            .Where(da => da.Date == dateOnly)
            .ToDictionaryAsync(da => da.DriverId, da => da, cancellationToken);

        // Get available drivers
        var availableDrivers = drivers
            .Where(d => availabilities.ContainsKey(d.Id))
            .ToList();

        if (!availableDrivers.Any())
        {
            return (0, candidatePoles.Count);
        }

        // Build location list for travel time matrix
        var locations = new List<GeoLocation>();
        
        // Add driver start locations first
        foreach (var driver in availableDrivers)
        {
            locations.Add(new GeoLocation((double)(driver.StartLatitude ?? 0), (double)(driver.StartLongitude ?? 0)));
        }

        // Add pole locations
        foreach (var pole in candidatePoles)
        {
            locations.Add(new GeoLocation((double)pole.Latitude, (double)pole.Longitude));
        }

        // Compute travel time matrix
        var travelTimeMatrix = await _travelTimeService.GetTravelTimeMatrixAsync(locations, cancellationToken);

        // Get extra work minutes from settings
        var settings = await _dbContext.PlanDaySettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Date == dateOnly, cancellationToken);
        var extraWorkMinutes = settings?.ExtraWorkMinutes ?? 0;

        // Build input for route planner
        var planInput = new PlanDayInput
        {
            Date = dateOnly,
            Drivers = availableDrivers.Select(driver =>
            {
                var availability = availabilities[driver.Id];
                var availabilityMinutes = (int)(availability.EndTime - availability.StartTime).TotalMinutes;
                
                var effectiveMaxWorkMinutes = Math.Min(
                    availabilityMinutes,
                    driver.MaxWorkMinutesPerDay + extraWorkMinutes);
                
                return new DriverInput
                {
                    Id = driver.Id,
                    StartLatitude = driver.StartLatitude ?? 0,
                    StartLongitude = driver.StartLongitude ?? 0,
                    AvailabilityMinutes = availabilityMinutes,
                    MaxWorkMinutesPerDay = effectiveMaxWorkMinutes,
                    DefaultServiceMinutes = driver.DefaultServiceMinutes,
                    StartTime = availability.StartTime,
                    EndTime = availability.EndTime
                };
            }).ToList(),
            Poles = candidatePoles.Select(pole =>
            {
                var serviceMinutes = availableDrivers.First().DefaultServiceMinutes;
                var isFixedForDate = pole.FixedDate.HasValue && pole.FixedDate.Value == dateOnly;
                
                return new PoleInput
                {
                    Id = pole.Id,
                    Latitude = pole.Latitude,
                    Longitude = pole.Longitude,
                    ServiceMinutes = serviceMinutes,
                    DueDate = pole.DueDate,
                    FixedDate = pole.FixedDate,
                    IsFixedForDate = isFixedForDate
                };
            }).ToList()
        };

        // Use route planner to generate routes
        var plannedResult = await _routePlanner.PlanDayAsync(planInput, travelTimeMatrix, cancellationToken);

        // Persist routes and stops
        var plannedCount = await PersistRoutesAsync(dateOnly, availableDrivers, availabilities, plannedResult, candidatePoles, planInput, cancellationToken);

        return (plannedCount, plannedResult.UnassignedPoleIds.Count);
    }

    private async Task<GeneratePlanResultDto> GenerateDayWithTrackingAsync(
        DateTime date,
        DateTime fromDate,
        DateTime windowEnd,
        HashSet<int> plannedPoleIds,
        List<Pole> latePoles,
        CancellationToken cancellationToken = default)
    {
        var result = new GeneratePlanResultDto
        {
            FromDate = date.Date,
            Days = 1
        };

        var dateOnly = date.Date;

        // Use transaction for per-day generation
        using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // Ensure PlanDay exists (need tracking for lock check)
            var planDay = await _dbContext.PlanDays
                .FirstOrDefaultAsync(pd => pd.Date == dateOnly, cancellationToken);

            if (planDay == null)
            {
                planDay = new PlanDay
                {
                    Date = dateOnly,
                    IsLocked = false
                };
                _dbContext.PlanDays.Add(planDay);
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            // If day is locked, skip entirely
            if (planDay.IsLocked)
            {
                await transaction.CommitAsync(cancellationToken);
                result.SkippedLockedDays = 1;
                return result;
            }

            var dayResult = await GenerateDayInternalWithTrackingAsync(
                dateOnly, 
                fromDate, 
                windowEnd, 
                plannedPoleIds, 
                latePoles, 
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            
            result.GeneratedDays = 1;
            result.PlannedPolesCount = dayResult.PlannedPolesCount;
            result.UnplannedPolesCount = dayResult.UnplannedPolesCount;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        return result;
    }

    public async Task<GeneratePlanResultDto> GenerateDayAsync(DateTime date, CancellationToken cancellationToken = default)
    {
        var result = new GeneratePlanResultDto
        {
            FromDate = date.Date,
            Days = 1
        };

        var dateOnly = date.Date;
        var plannedPoleIds = new HashSet<int>();
        var latePoles = new List<Pole>();

        // Use transaction for per-day generation
        using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // Ensure PlanDay exists (need tracking for lock check)
            var planDay = await _dbContext.PlanDays
                .FirstOrDefaultAsync(pd => pd.Date == dateOnly, cancellationToken);

            if (planDay == null)
            {
                planDay = new PlanDay
                {
                    Date = dateOnly,
                    IsLocked = false
                };
                _dbContext.PlanDays.Add(planDay);
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            // If day is locked, skip entirely
            if (planDay.IsLocked)
            {
                await transaction.CommitAsync(cancellationToken);
                result.SkippedLockedDays = 1;
                return result;
            }

            // For single day, use the same date as both fromDate and windowEnd
            var dayResult = await GenerateDayInternalWithTrackingAsync(
                dateOnly, 
                dateOnly, 
                dateOnly, 
                plannedPoleIds, 
                latePoles, 
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            
            result.GeneratedDays = 1;
            result.PlannedPolesCount = dayResult.PlannedPolesCount;
            result.UnplannedPolesCount = dayResult.UnplannedPolesCount;
            result.LatePolesCount = latePoles.Select(p => p.Id).Distinct().Count();
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        return result;
    }

    private async Task<(int PlannedPolesCount, int UnplannedPolesCount)> GenerateDayInternalAsync(DateTime dateOnly, CancellationToken cancellationToken)
    {
        // Load drivers
        var drivers = await _dbContext.Drivers
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        if (!drivers.Any())
        {
            return (0, 0);
        }

        // Load driver availability for this day
        var availabilities = await _dbContext.DriverAvailabilities
            .AsNoTracking()
            .Where(da => da.Date == dateOnly)
            .ToDictionaryAsync(da => da.DriverId, da => da, cancellationToken);

        // Get available drivers (those with availability for this day)
        var availableDrivers = drivers
            .Where(d => availabilities.ContainsKey(d.Id))
            .ToList();

        if (!availableDrivers.Any())
        {
            return (0, 0);
        }

        // Get candidate poles for this day using new eligibility rules
        // Eligibility: fromDate <= day <= min(DueDate, windowEnd) for non-fixed poles
        // For fixed poles: must be on the specific day
        var allPoles = await _dbContext.Poles
            .AsNoTracking()
            .Where(p => p.Status != PoleStatus.Done && p.Status != PoleStatus.Cancelled)
            .ToListAsync(cancellationToken);

        // Exclude already planned poles (from database and in-memory tracking) - ensure Date normalization
        var routesForDate = await _dbContext.Routes
            .AsNoTracking()
            .Where(r => r.Date.Date == dateOnly.Date)
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);

        var dbPlannedPoleIds = routesForDate.Any()
            ? await _dbContext.RouteStops
                .AsNoTracking()
                .Where(rs => routesForDate.Contains(rs.RouteId))
                .Select(rs => rs.PoleId)
                .Distinct()
                .ToListAsync(cancellationToken)
            : new List<int>();

        // Filter by eligibility: fromDate <= day <= min(DueDate, windowEnd)
        // For this single-day call, we use dateOnly as both fromDate and windowEnd
        var fromDate = dateOnly;
        var windowEnd = dateOnly;
        
        var eligiblePoles = allPoles
            .Where(p => 
            {
                if (p.FixedDate.HasValue)
                {
                    // Fixed date poles must be on the specific day
                    return p.FixedDate.Value.Date == dateOnly;
                }
                else
                {
                    // Non-fixed poles: eligible if fromDate <= day <= DueDate
                    return fromDate <= dateOnly && dateOnly <= p.DueDate.Date;
                }
            })
            .Where(p => !dbPlannedPoleIds.Contains(p.Id))
            .ToList();

        // Separate late poles (DueDate < current day) - these should not be scheduled
        var latePolesForDay = eligiblePoles
            .Where(p => !p.FixedDate.HasValue && p.DueDate.Date < dateOnly)
            .ToList();

        var candidatePoles = eligiblePoles
            .Where(p => !latePolesForDay.Contains(p))
            .ToList();

        if (!candidatePoles.Any())
        {
            return (0, 0);
        }

        // Delete existing routes for this day (only if not locked) - ensure Date normalization
        var existingRoutes = await _dbContext.Routes
            .Where(r => r.Date.Date == dateOnly.Date && !r.IsLocked)
            .ToListAsync(cancellationToken);

        if (existingRoutes.Any())
        {
            var routeIds = existingRoutes.Select(r => r.Id).ToList();
            var existingStops = await _dbContext.RouteStops
                .Where(rs => routeIds.Contains(rs.RouteId))
                .ToListAsync(cancellationToken);

            _dbContext.RouteStops.RemoveRange(existingStops);
            _dbContext.Routes.RemoveRange(existingRoutes);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        // Build location list for travel time matrix
        var locations = new List<GeoLocation>();
        
        // Add driver start locations first
        foreach (var driver in availableDrivers)
        {
            locations.Add(new GeoLocation((double)(driver.StartLatitude ?? 0), (double)(driver.StartLongitude ?? 0)));
        }

        // Add pole locations
        foreach (var pole in candidatePoles)
        {
            locations.Add(new GeoLocation((double)pole.Latitude, (double)pole.Longitude));
        }

        // Compute travel time matrix
        var travelTimeMatrix = await _travelTimeService.GetTravelTimeMatrixAsync(locations, cancellationToken);

        // Get extra work minutes from settings (default 0)
        var settings = await _dbContext.PlanDaySettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Date == dateOnly, cancellationToken);
        var extraWorkMinutes = settings?.ExtraWorkMinutes ?? 0;

        // Build input for route planner
        var planInput = new PlanDayInput
        {
            Date = dateOnly,
            Drivers = availableDrivers.Select(driver =>
            {
                var availability = availabilities[driver.Id];
                var availabilityMinutes = (int)(availability.EndTime - availability.StartTime).TotalMinutes;
                
                // Calculate effective max: driver max + extra minutes, bounded by availability
                var effectiveMaxWorkMinutes = Math.Min(
                    availabilityMinutes,
                    driver.MaxWorkMinutesPerDay + extraWorkMinutes);
                
                return new DriverInput
                {
                    Id = driver.Id,
                    StartLatitude = driver.StartLatitude ?? 0,
                    StartLongitude = driver.StartLongitude ?? 0,
                    AvailabilityMinutes = availabilityMinutes,
                    MaxWorkMinutesPerDay = effectiveMaxWorkMinutes,
                    DefaultServiceMinutes = driver.DefaultServiceMinutes,
                    StartTime = availability.StartTime,
                    EndTime = availability.EndTime
                };
            }).ToList(),
            Poles = candidatePoles.Select(pole =>
            {
                var serviceMinutes = availableDrivers.First().DefaultServiceMinutes; // Use default from first driver
                var isFixedForDate = pole.FixedDate.HasValue && pole.FixedDate.Value == dateOnly;
                
                return new PoleInput
                {
                    Id = pole.Id,
                    Latitude = pole.Latitude,
                    Longitude = pole.Longitude,
                    ServiceMinutes = serviceMinutes,
                    DueDate = pole.DueDate,
                    FixedDate = pole.FixedDate,
                    IsFixedForDate = isFixedForDate
                };
            }).ToList()
        };

        // Use route planner to generate routes
        var plannedResult = await _routePlanner.PlanDayAsync(planInput, travelTimeMatrix, cancellationToken);

        // Persist routes and stops
        var plannedCount = await PersistRoutesAsync(dateOnly, availableDrivers, availabilities, plannedResult, candidatePoles, planInput, cancellationToken);

        return (plannedCount, plannedResult.UnassignedPoleIds.Count);
    }

    private async Task<(int PlannedPolesCount, int UnplannedPolesCount)> GenerateDayInternalWithTrackingAsync(
        DateTime dateOnly,
        DateTime fromDate,
        DateTime windowEnd,
        HashSet<int> plannedPoleIds,
        List<Pole> latePoles,
        CancellationToken cancellationToken)
    {
        // Load drivers
        var drivers = await _dbContext.Drivers
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        if (!drivers.Any())
        {
            return (0, 0);
        }

        // Load driver availability for this day
        var availabilities = await _dbContext.DriverAvailabilities
            .AsNoTracking()
            .Where(da => da.Date == dateOnly)
            .ToDictionaryAsync(da => da.DriverId, da => da, cancellationToken);

        // Get available drivers (those with availability for this day)
        var availableDrivers = drivers
            .Where(d => availabilities.ContainsKey(d.Id))
            .ToList();

        if (!availableDrivers.Any())
        {
            return (0, 0);
        }

        // Get all poles that could be eligible (status check)
        var allPoles = await _dbContext.Poles
            .AsNoTracking()
            .Where(p => p.Status != PoleStatus.Done && p.Status != PoleStatus.Cancelled)
            .ToListAsync(cancellationToken);

        // Exclude already planned poles (from database and in-memory tracking) - ensure Date normalization
        var routesForDate = await _dbContext.Routes
            .AsNoTracking()
            .Where(r => r.Date.Date == dateOnly.Date)
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);

        var dbPlannedPoleIds = routesForDate.Any()
            ? await _dbContext.RouteStops
                .AsNoTracking()
                .Where(rs => routesForDate.Contains(rs.RouteId))
                .Select(rs => rs.PoleId)
                .Distinct()
                .ToListAsync(cancellationToken)
            : new List<int>();

        // HORIZON-BASED CANDIDATE SELECTION
        // Build candidates for day D:
        // mandatory = poles with FixedDate == D (open)
        // backlog = poles with FixedDate null AND DueDate between [D..end] (open, still due in horizon)
        
        // Mandatory: FixedDate == D
        var mandatoryPoles = allPoles
            .Where(p => p.FixedDate.HasValue && p.FixedDate.Value.Date == dateOnly.Date)
            .Where(p => !plannedPoleIds.Contains(p.Id))
            .Where(p => !dbPlannedPoleIds.Contains(p.Id))
            .ToList();

        // Backlog: FixedDate null AND DueDate between [D..end] (still due in horizon)
        var backlogPoles = allPoles
            .Where(p => p.FixedDate == null && p.DueDate.Date >= dateOnly.Date && p.DueDate.Date <= windowEnd)
            .Where(p => !plannedPoleIds.Contains(p.Id))
            .Where(p => !dbPlannedPoleIds.Contains(p.Id))
            .OrderBy(p => p.DueDate) // Order backlog by DueDate ASC (earliest due first)
            .ToList();

        // Build candidate list: mandatory + backlog (limit MaxDailyCandidates but NEVER exclude mandatory)
        var maxCandidates = _planningOptions.OrTools?.MaxDailyCandidates ?? 200;
        var candidatePoles = new List<Pole>();
        
        // Always include all mandatory poles
        candidatePoles.AddRange(mandatoryPoles);
        
        // Add backlog poles up to limit
        var remainingSlots = maxCandidates - mandatoryPoles.Count;
        if (remainingSlots > 0)
        {
            candidatePoles.AddRange(backlogPoles.Take(remainingSlots));
        }
        
        _logger.LogInformation(
            "Day {Date}: Planning with {MandatoryCount} mandatory poles and {BacklogCount} backlog poles (from {TotalBacklog} available), total candidates: {CandidateCount}",
            dateOnly, mandatoryPoles.Count, Math.Min(backlogPoles.Count, remainingSlots), backlogPoles.Count, candidatePoles.Count);

        // Candidate list is already prioritized by DueDate ASC (earliest due first)

        if (!candidatePoles.Any())
        {
            return (0, 0);
        }

        // Delete existing routes for this day (only if not locked) - ensure Date normalization
        var existingRoutes = await _dbContext.Routes
            .Where(r => r.Date.Date == dateOnly.Date && !r.IsLocked)
            .ToListAsync(cancellationToken);

        if (existingRoutes.Any())
        {
            var routeIds = existingRoutes.Select(r => r.Id).ToList();
            var existingStops = await _dbContext.RouteStops
                .Where(rs => routeIds.Contains(rs.RouteId))
                .ToListAsync(cancellationToken);

            _dbContext.RouteStops.RemoveRange(existingStops);
            _dbContext.Routes.RemoveRange(existingRoutes);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        // Build location list for travel time matrix
        var locations = new List<GeoLocation>();
        
        // Add driver start locations first
        foreach (var driver in availableDrivers)
        {
            locations.Add(new GeoLocation((double)(driver.StartLatitude ?? 0), (double)(driver.StartLongitude ?? 0)));
        }

        // Add pole locations
        foreach (var pole in candidatePoles)
        {
            locations.Add(new GeoLocation((double)pole.Latitude, (double)pole.Longitude));
        }

        // Compute travel time matrix
        var travelTimeMatrix = await _travelTimeService.GetTravelTimeMatrixAsync(locations, cancellationToken);

        // Get extra work minutes from settings (default 0)
        var settings = await _dbContext.PlanDaySettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Date == dateOnly, cancellationToken);
        var extraWorkMinutes = settings?.ExtraWorkMinutes ?? 0;

        // Build input for route planner with slackDays for prioritization
        var planInput = new PlanDayInput
        {
            Date = dateOnly,
            Drivers = availableDrivers.Select(driver =>
            {
                var availability = availabilities[driver.Id];
                var availabilityMinutes = (int)(availability.EndTime - availability.StartTime).TotalMinutes;
                
                // Calculate effective max: driver max + extra minutes, bounded by availability
                var effectiveMaxWorkMinutes = Math.Min(
                    availabilityMinutes,
                    driver.MaxWorkMinutesPerDay + extraWorkMinutes);
                
                return new DriverInput
                {
                    Id = driver.Id,
                    StartLatitude = driver.StartLatitude ?? 0,
                    StartLongitude = driver.StartLongitude ?? 0,
                    AvailabilityMinutes = availabilityMinutes,
                    MaxWorkMinutesPerDay = effectiveMaxWorkMinutes,
                    DefaultServiceMinutes = driver.DefaultServiceMinutes,
                    StartTime = availability.StartTime,
                    EndTime = availability.EndTime
                };
            }).ToList(),
            Poles = candidatePoles.Select(pole =>
            {
                var serviceMinutes = availableDrivers.First().DefaultServiceMinutes;
                var isFixedForDate = pole.FixedDate.HasValue && pole.FixedDate.Value == dateOnly;
                var slackDays = isFixedForDate ? 0 : (pole.DueDate.Date - dateOnly).Days;
                
                return new PoleInput
                {
                    Id = pole.Id,
                    Latitude = pole.Latitude,
                    Longitude = pole.Longitude,
                    ServiceMinutes = serviceMinutes,
                    DueDate = pole.DueDate,
                    FixedDate = pole.FixedDate,
                    IsFixedForDate = isFixedForDate
                };
            }).ToList()
        };

        // Use route planner to generate routes
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var plannedResult = await _routePlanner.PlanDayAsync(planInput, travelTimeMatrix, cancellationToken);
        stopwatch.Stop();

        _logger.LogInformation(
            "Day {Date}: Solver completed in {ElapsedMs}ms, assigned {AssignedCount} poles, {UnassignedCount} unassigned",
            dateOnly, stopwatch.ElapsedMilliseconds, 
            candidatePoles.Count - plannedResult.UnassignedPoleIds.Count,
            plannedResult.UnassignedPoleIds.Count);

        // Track planned poles in memory
        var assignedPoleIds = candidatePoles
            .Where(p => !plannedResult.UnassignedPoleIds.Contains(p.Id))
            .Select(p => p.Id)
            .ToList();
        
        foreach (var poleId in assignedPoleIds)
        {
            plannedPoleIds.Add(poleId);
        }

        // Persist routes and stops
        var plannedCount = await PersistRoutesAsync(dateOnly, availableDrivers, availabilities, plannedResult, candidatePoles, planInput, cancellationToken);

        return (plannedCount, plannedResult.UnassignedPoleIds.Count);
    }

    private async Task<int> PersistRoutesAsync(
        DateTime dateOnly,
        List<Driver> drivers,
        Dictionary<int, DriverAvailability> availabilities,
        PlannedDayResult plannedResult,
        List<Pole> candidatePoles,
        PlanDayInput planInput,
        CancellationToken cancellationToken)
    {
        var poleLookup = candidatePoles.ToDictionary(p => p.Id);
        var driverLookup = drivers.ToDictionary(d => d.Id);
        var poleInputLookup = planInput.Poles.ToDictionary(p => p.Id);
        var plannedCount = 0;

        foreach (var driverRoute in plannedResult.DriverRoutes)
        {
            var driver = driverLookup[driverRoute.DriverId];
            var availability = availabilities[driver.Id];
            var routeStartTime = dateOnly.Date.Add(availability.StartTime);

            // Calculate total minutes and km
            var totalMinutes = 0;
            decimal totalKm = 0;
            var startLocation = new GeoLocation((double)(driver.StartLatitude ?? 0), (double)(driver.StartLongitude ?? 0));
            GeoLocation? prevLocation = startLocation;

            foreach (var assignment in driverRoute.PoleAssignments.OrderBy(a => a.Sequence))
            {
                var pole = poleLookup[assignment.PoleId];
                var poleInput = poleInputLookup[assignment.PoleId];
                var currentLocation = new GeoLocation((double)pole.Latitude, (double)pole.Longitude);
                var serviceMinutes = poleInput.ServiceMinutes;
                totalMinutes += assignment.TravelMinutesFromPrev + serviceMinutes;
                
                // Calculate km for this assignment
                assignment.TravelKmFromPrev = (decimal)CalculateKmBetween(prevLocation, currentLocation);
                totalKm += assignment.TravelKmFromPrev;
                
                prevLocation = currentLocation;
            }

            // Add return trip to start location if there are any assignments
            if (driverRoute.PoleAssignments.Any() && prevLocation != null)
            {
                var returnKm = CalculateKmBetween(prevLocation, startLocation);
                var returnMinutes = await _travelTimeService.GetTravelTimeAsync(
                    prevLocation, 
                    startLocation, 
                    cancellationToken);
                totalKm += (decimal)returnKm;
                totalMinutes += returnMinutes;
            }

            var route = new Route
            {
                Date = dateOnly.Date, // Ensure only date part, no time component
                DriverId = driver.Id,
                IsLocked = false,
                TotalMinutes = totalMinutes,
                TotalKm = totalKm
            };

            _dbContext.Routes.Add(route);
            await _dbContext.SaveChangesAsync(cancellationToken);

            // Create route stops with planned times
            var currentTime = routeStartTime;
            foreach (var assignment in driverRoute.PoleAssignments.OrderBy(a => a.Sequence))
            {
                var pole = poleLookup[assignment.PoleId];
                var poleInput = poleInputLookup[assignment.PoleId];
                currentTime = currentTime.AddMinutes(assignment.TravelMinutesFromPrev);
                
                var plannedStart = currentTime;
                var serviceMinutes = poleInput.ServiceMinutes;
                var plannedEnd = currentTime.AddMinutes(serviceMinutes);

                var routeStop = new RouteStop
                {
                    RouteId = route.Id,
                    Sequence = assignment.Sequence,
                    PoleId = pole.Id,
                    PlannedStart = plannedStart,
                    PlannedEnd = plannedEnd,
                    TravelMinutesFromPrev = assignment.TravelMinutesFromPrev,
                    TravelKmFromPrev = assignment.TravelKmFromPrev,
                    Status = RouteStopStatus.Pending
                };

                _dbContext.RouteStops.Add(routeStop);
                currentTime = plannedEnd;
                plannedCount++;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        // HARD ASSERT: Verify persistence - re-query count of Routes for day D
        var savedRoutesCount = await _dbContext.Routes
            .AsNoTracking()
            .Where(r => r.Date.Date == dateOnly.Date)
            .CountAsync(cancellationToken);

        var savedStopsCount = await _dbContext.RouteStops
            .AsNoTracking()
            .Join(
                _dbContext.Routes.AsNoTracking().Where(r => r.Date.Date == dateOnly.Date),
                rs => rs.RouteId,
                r => r.Id,
                (rs, r) => rs)
            .CountAsync(cancellationToken);

        _logger.LogInformation(
            "Day {Date}: Saved {RoutesCount} routes and {StopsCount} stops. Expected {ExpectedCount} stops.",
            dateOnly, savedRoutesCount, savedStopsCount, plannedCount);

        // HARD ASSERT (Development only): If solver assigned > 0 stops AND Routes.Count(date D) == 0, throw exception
        if (plannedCount > 0 && savedRoutesCount == 0)
        {
            var errorMessage = $"Routes not persisted: Day {dateOnly:yyyy-MM-dd} - Solver assigned {plannedCount} stops but 0 routes found in database after SaveChanges.";
            _logger.LogError(errorMessage);
            
            // Only throw in Development to catch persistence issues immediately
            var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
            if (environment == "Development")
            {
                throw new InvalidOperationException(errorMessage);
            }
        }

        if (savedStopsCount != plannedCount)
        {
            _logger.LogWarning(
                "Day {Date}: Mismatch between expected stops ({ExpectedCount}) and saved stops ({SavedCount})",
                dateOnly, plannedCount, savedStopsCount);
        }

        return plannedCount;
    }

    private static double CalculateKmBetween(GeoLocation from, GeoLocation to)
    {
        const double EarthRadiusKm = 6371.0;
        var dLat = DegreesToRadians(to.Latitude - from.Latitude);
        var dLon = DegreesToRadians(to.Longitude - from.Longitude);

        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(DegreesToRadians(from.Latitude)) * Math.Cos(DegreesToRadians(to.Latitude)) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return EarthRadiusKm * c;
    }

    private static double DegreesToRadians(double degrees)
    {
        return degrees * (Math.PI / 180.0);
    }
}
