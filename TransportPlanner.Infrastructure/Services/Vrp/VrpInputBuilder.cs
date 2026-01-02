using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TransportPlanner.Application.Services;
using TransportPlanner.Domain.Entities;
using TransportPlanner.Infrastructure.Data;

namespace TransportPlanner.Infrastructure.Services.Vrp;

public class VrpInputBuilder : IVrpInputBuilder
{
    private readonly TransportPlannerDbContext _dbContext;
    private readonly ILogger<VrpInputBuilder> _logger;

    public VrpInputBuilder(TransportPlannerDbContext dbContext, ILogger<VrpInputBuilder> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<VrpInputBuildResult> BuildAsync(VrpSolveRequest request, CancellationToken cancellationToken)
    {
        var date = request.Date.Date;
        var skippedDrivers = new List<string>();
        var excludedLocationIds = new List<int>();

        var drivers = await _dbContext.Drivers
            .AsNoTracking()
            .Include(d => d.DriverServiceTypes)
            .Where(d => d.OwnerId == request.OwnerId && d.IsActive)
            .ToListAsync(cancellationToken);

        var driverIds = drivers.Select(d => d.Id).ToList();
        var availabilities = await _dbContext.DriverAvailabilities
            .AsNoTracking()
            .Where(a => a.Date == date && driverIds.Contains(a.DriverId))
            .ToDictionaryAsync(a => a.DriverId, cancellationToken);

        var fixedRoutes = await _dbContext.Routes
            .AsNoTracking()
            .Include(r => r.Stops)
            .Where(r => r.OwnerId == request.OwnerId && r.Date.Date == date && r.Status == RouteStatus.Fixed)
            .ToListAsync(cancellationToken);

        var driversWithFixedRoutes = fixedRoutes.Select(r => r.DriverId).ToHashSet();
        var fixedLocationIds = fixedRoutes
            .SelectMany(r => r.Stops)
            .Where(s => s.ServiceLocationId.HasValue)
            .Select(s => s.ServiceLocationId!.Value)
            .Distinct()
            .ToHashSet();

        var driverInputs = new List<VrpDriver>();
        foreach (var driver in drivers)
        {
            if (driversWithFixedRoutes.Contains(driver.Id))
            {
                skippedDrivers.Add($"{driver.Name}: Existing route is fixed.");
                continue;
            }

            if (!availabilities.TryGetValue(driver.Id, out var availability))
            {
                skippedDrivers.Add($"{driver.Name}: Driver is not available on this date.");
                continue;
            }

            if (!driver.StartLatitude.HasValue || !driver.StartLongitude.HasValue
                || (driver.StartLatitude.Value == 0 && driver.StartLongitude.Value == 0))
            {
                skippedDrivers.Add($"{driver.Name}: Driver start coordinates are missing.");
                continue;
            }

            var maxRouteMinutes = Math.Min(availability.AvailableMinutes, driver.MaxWorkMinutesPerDay);
            if (maxRouteMinutes <= 0)
            {
                skippedDrivers.Add($"{driver.Name}: Driver has no available minutes.");
                continue;
            }

            var serviceTypeIds = driver.DriverServiceTypes.Select(dst => dst.ServiceTypeId).Distinct().ToList();
            if (request.RequireServiceTypeMatch && serviceTypeIds.Count == 0)
            {
                skippedDrivers.Add($"{driver.Name}: Driver has no service types assigned.");
                continue;
            }

            driverInputs.Add(new VrpDriver(
                driver,
                availability.StartMinuteOfDay,
                availability.EndMinuteOfDay,
                maxRouteMinutes,
                serviceTypeIds));
        }

        IQueryable<ServiceLocation> query = _dbContext.ServiceLocations
            .AsNoTracking()
            .Where(sl =>
                sl.OwnerId == request.OwnerId &&
                sl.Status == ServiceLocationStatus.Open &&
                sl.IsActive &&
                sl.Latitude.HasValue &&
                sl.Longitude.HasValue);

        if (request.ServiceLocationToolIds != null && request.ServiceLocationToolIds.Count > 0)
        {
            query = query.Where(sl => request.ServiceLocationToolIds.Contains(sl.ToolId));
        }

        if (fixedLocationIds.Count > 0)
        {
            query = query.Where(sl => !fixedLocationIds.Contains(sl.Id));
        }

        var candidates = await query.ToListAsync(cancellationToken);
        var locationIds = candidates.Select(c => c.Id).ToList();
        var locationWindows = await LoadLocationWindowsAsync(locationIds, date, cancellationToken);
        var locationConstraints = await LoadLocationConstraintsAsync(locationIds, cancellationToken);

        var driverServiceTypeIds = driverInputs
            .SelectMany(d => d.ServiceTypeIds)
            .Distinct()
            .ToHashSet();

        var jobs = new List<VrpJob>();

        if (request.RequireServiceTypeMatch && driverServiceTypeIds.Count == 0)
        {
            excludedLocationIds.AddRange(candidates.Select(c => c.Id));
            _logger.LogInformation("All service locations excluded because no drivers have service types assigned.");
        }

        foreach (var location in candidates)
        {
            if (request.RequireServiceTypeMatch && driverServiceTypeIds.Count == 0)
            {
                continue;
            }

            var serviceMinutes = ResolveServiceMinutes(location, locationConstraints);

            var window = locationWindows.TryGetValue(location.Id, out var windowValue)
                ? windowValue
                : TimeWindow.AlwaysOpen;

            if (window.IsClosed)
            {
                excludedLocationIds.Add(location.Id);
                continue;
            }

            var windows = BuildWindows(window, serviceMinutes);
            if (windows.Count == 0)
            {
                excludedLocationIds.Add(location.Id);
                continue;
            }

            if (request.RequireServiceTypeMatch && driverServiceTypeIds.Count > 0 &&
                !driverServiceTypeIds.Contains(location.ServiceTypeId))
            {
                excludedLocationIds.Add(location.Id);
                continue;
            }

            var duePenalty = 1.0 - ComputeDueUrgencyNormalized(date, location);

            jobs.Add(new VrpJob(
                location.Id,
                location.ToolId,
                location.Name,
                location.ServiceTypeId,
                location.Latitude ?? 0,
                location.Longitude ?? 0,
                serviceMinutes,
                duePenalty,
                windows));
        }

        var nodes = new List<VrpNode>();
        var nodeWindows = new Dictionary<int, VrpTimeWindow>();
        var jobNodeIndices = new Dictionary<int, List<int>>();

        for (var i = 0; i < driverInputs.Count; i++)
        {
            var driver = driverInputs[i].Driver;
            nodes.Add(new VrpNode(
                i,
                VrpNodeType.Start,
                null,
                driver.StartLatitude ?? 0,
                driver.StartLongitude ?? 0,
                0,
                0,
                null));
        }

        var nodeIndex = nodes.Count;
        foreach (var job in jobs)
        {
            foreach (var window in job.Windows)
            {
                nodes.Add(new VrpNode(
                    nodeIndex,
                    VrpNodeType.Job,
                    job.LocationId,
                    job.Latitude,
                    job.Longitude,
                    job.ServiceMinutes,
                    job.DuePenalty,
                    job.ServiceTypeId));

                nodeWindows[nodeIndex] = window;

                if (!jobNodeIndices.TryGetValue(job.LocationId, out var list))
                {
                    list = new List<int>();
                    jobNodeIndices[job.LocationId] = list;
                }

                list.Add(nodeIndex);
                nodeIndex++;
            }
        }

        var jobsById = jobs.ToDictionary(j => j.LocationId, j => j);

        var input = new VrpInput(
            date,
            request.OwnerId,
            driverInputs,
            jobs,
            nodes,
            nodeWindows,
            jobNodeIndices,
            jobsById);

        return new VrpInputBuildResult(input, skippedDrivers, excludedLocationIds);
    }

    private static List<VrpTimeWindow> BuildWindows(TimeWindow window, int serviceMinutes)
    {
        var windows = new List<VrpTimeWindow>();

        if (!window.IsClosed)
        {
            AddWindow(windows, window.OpenMinute, window.CloseMinute, serviceMinutes);
            if (window.HasSecondWindow)
            {
                AddWindow(windows, window.OpenMinute2!.Value, window.CloseMinute2!.Value, serviceMinutes);
            }
        }

        return windows;
    }

    private static void AddWindow(List<VrpTimeWindow> windows, int openMinute, int closeMinute, int serviceMinutes)
    {
        var latestStart = closeMinute - serviceMinutes;
        if (latestStart >= openMinute)
        {
            windows.Add(new VrpTimeWindow(openMinute, closeMinute));
        }
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

    private static int ResolveServiceMinutes(
        ServiceLocation location,
        Dictionary<int, ServiceLocationConstraint> constraints)
    {
        var minutes = location.ServiceMinutes > 0 ? location.ServiceMinutes : 20;

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

    private static double ComputeDueUrgencyNormalized(DateTime scheduleDate, ServiceLocation location)
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

        return Clamp01(urgency);
    }

    private static double Clamp01(double value) => Math.Max(0, Math.Min(1, value));
}
