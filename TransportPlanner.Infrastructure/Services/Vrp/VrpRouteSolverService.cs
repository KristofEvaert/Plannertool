using System.Security.Cryptography;
using System.Text;
using Google.OrTools.ConstraintSolver;
using Google.Protobuf.WellKnownTypes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TransportPlanner.Application.DTOs;
using TransportPlanner.Application.Services;
using TransportPlanner.Domain.Entities;
using TransportPlanner.Infrastructure.Data;
using TransportPlanner.Infrastructure.Options;
using RouteEntity = TransportPlanner.Domain.Entities.Route;

namespace TransportPlanner.Infrastructure.Services.Vrp;

public class VrpRouteSolverService : IVrpRouteSolverService
{
    private const string TimeDimensionName = "Time";
    private const int DefaultDistanceRefKm = 60;
    private const int DefaultTimeRefMinutes = 120;
    private const int DefaultCostRefEuro = 50;
    private const int DefaultOvertimeRefMinutes = 60;
    private const int MaxWaitMinutes = 24 * 60;
    private const long CostScale = 10000;
    private const long UnassignedPenalty = 1_000_000;

    private readonly TransportPlannerDbContext _dbContext;
    private readonly IVrpInputBuilder _inputBuilder;
    private readonly IMatrixProvider _matrixProvider;
    private readonly IVrpResultMapper _resultMapper;
    private readonly OrToolsOptions _options;
    private readonly ILogger<VrpRouteSolverService> _logger;

    public VrpRouteSolverService(
        TransportPlannerDbContext dbContext,
        IVrpInputBuilder inputBuilder,
        IMatrixProvider matrixProvider,
        IVrpResultMapper resultMapper,
        IOptions<PlanningOptions> planningOptions,
        ILogger<VrpRouteSolverService> logger)
    {
        _dbContext = dbContext;
        _inputBuilder = inputBuilder;
        _matrixProvider = matrixProvider;
        _resultMapper = resultMapper;
        _options = planningOptions.Value.OrTools ?? new OrToolsOptions();
        _logger = logger;
    }

    public async Task<VrpSolveResult> SolveDayAsync(VrpSolveRequest request, CancellationToken cancellationToken)
    {
        var build = await _inputBuilder.BuildAsync(request, cancellationToken);
        var input = build.Input;

        if (input.Drivers.Count == 0 || input.Jobs.Count == 0)
        {
            return new VrpSolveResult(Array.Empty<RouteDto>(), build.SkippedDrivers, build.ExcludedLocationIds);
        }

        var points = input.Nodes.Select(n => new MatrixPoint(n.Latitude, n.Longitude)).ToList();
        var cacheKey = BuildCacheKey(request.OwnerId, input.Date, input.Nodes);
        var matrix = await _matrixProvider.GetMatrixAsync(cacheKey, points, cancellationToken);

        var normalizationRefs = BuildNormalizationReferences(input, matrix, request.CostSettings);
        var weights = BuildNormalizedWeights(request.Weights, request.NormalizeWeights);

        var driverCount = input.Drivers.Count;
        var startIndices = Enumerable.Range(0, driverCount).ToArray();
        var endIndices = startIndices;
        var manager = new RoutingIndexManager(input.Nodes.Count, driverCount, startIndices, endIndices);
        var routing = new RoutingModel(manager);

        var timeCallbackIndex = routing.RegisterTransitCallback((long fromIndex, long toIndex) =>
        {
            var fromNode = manager.IndexToNode(fromIndex);
            var toNode = manager.IndexToNode(toIndex);
            var travelMinutes = matrix.TravelMinutes[fromNode, toNode];
            var serviceMinutes = input.Nodes[fromNode].ServiceMinutes;
            return travelMinutes + serviceMinutes;
        });

        var costCallbackIndex = routing.RegisterTransitCallback((long fromIndex, long toIndex) =>
        {
            var fromNode = manager.IndexToNode(fromIndex);
            var toNode = manager.IndexToNode(toIndex);
            var distanceKm = matrix.DistanceKm[fromNode, toNode];
            var travelMinutes = matrix.TravelMinutes[fromNode, toNode];
            var serviceMinutes = input.Nodes[fromNode].ServiceMinutes;
            var timeCostMinutes = travelMinutes + serviceMinutes;
            var duePenalty = 0.0;

            if (input.Nodes[toNode].Type == VrpNodeType.Job && input.Nodes[toNode].JobId.HasValue)
            {
                duePenalty = input.JobsById[input.Nodes[toNode].JobId!.Value].DuePenalty;
            }

            var normDistance = Clamp01(distanceKm / normalizationRefs.DistanceKm);
            var normTime = Clamp01(timeCostMinutes / normalizationRefs.TimeMinutes);
            var normCost = Clamp01(CostCalculator.CalculateTravelCost(
                distanceKm,
                travelMinutes,
                request.CostSettings.FuelCostPerKm,
                request.CostSettings.PersonnelCostPerHour) / normalizationRefs.CostEuro);

            var score = (weights.Distance * normDistance)
                        + (weights.Time * normTime)
                        + (weights.Date * duePenalty)
                        + (weights.Cost * normCost);

            return Math.Max(0, (long)Math.Round(score * CostScale));
        });

        routing.SetArcCostEvaluatorOfAllVehicles(costCallbackIndex);

        var maxEndMinute = input.Drivers.Max(d => d.AvailabilityEndMinute);
        var overtimeBuffer = weights.Overtime > 0 ? DefaultOvertimeRefMinutes : 0;
        var globalMax = Math.Min(24 * 60 + DefaultOvertimeRefMinutes, maxEndMinute + overtimeBuffer + MaxWaitMinutes);

        routing.AddDimension(
            timeCallbackIndex,
            MaxWaitMinutes,
            globalMax,
            false,
            TimeDimensionName);

        var timeDimension = routing.GetMutableDimension(TimeDimensionName);
        ApplyTimeCosts(routing, manager, timeDimension, normalizationRefs, weights, input.Drivers.Count);
        ApplyDriverTimeWindows(input, routing, timeDimension, weights);
        ApplyJobTimeWindows(input, manager, timeDimension);

        if (request.RequireServiceTypeMatch)
        {
            ApplyServiceTypeConstraints(input, routing, manager);
        }

        if (request.MaxStopsPerDriver.HasValue && request.MaxStopsPerDriver.Value > 0)
        {
            ApplyStopLimits(input, routing, manager, request.MaxStopsPerDriver.Value);
        }

        foreach (var jobEntry in input.JobNodeIndices)
        {
            var indices = jobEntry.Value.Select(manager.NodeToIndex).ToArray();
            routing.AddDisjunction(indices, UnassignedPenalty);
        }

        var searchParameters = BuildSearchParameters();
        var solution = routing.SolveWithParameters(searchParameters);

        if (solution == null)
        {
            var allJobIds = input.Jobs.Select(j => j.LocationId).ToList();
            var unassigned = build.ExcludedLocationIds.Concat(allJobIds).Distinct().ToList();
            return new VrpSolveResult(Array.Empty<RouteDto>(), build.SkippedDrivers, unassigned);
        }

        var mapped = _resultMapper.MapSolution(input, routing, manager, solution, matrix);
        foreach (var route in mapped.Routes)
        {
            var sumLegs = route.Stops.Sum(s => s.TravelKmFromPrev);
            if (route.TotalDistanceKm + 0.01 < sumLegs)
            {
                _logger.LogWarning(
                    "Route distance mismatch for DriverId={DriverId}. TotalKm={TotalKm} SumLegKm={SumLegKm} Stops={StopsCount}",
                    route.Driver.Id,
                    route.TotalDistanceKm,
                    sumLegs,
                    route.Stops.Count);
            }
        }

        var routes = await PersistRoutesAsync(input, mapped.Routes, request.WeightTemplateId, cancellationToken);
        var unassignedIds = build.ExcludedLocationIds
            .Concat(mapped.UnassignedLocationIds)
            .Distinct()
            .ToList();

        return new VrpSolveResult(routes, build.SkippedDrivers, unassignedIds);
    }

    private static string BuildCacheKey(int ownerId, DateTime date, IReadOnlyList<VrpNode> nodes)
    {
        var sb = new StringBuilder();
        sb.Append(ownerId).Append('|').Append(date.ToString("yyyyMMdd"));
        foreach (var node in nodes)
        {
            sb.Append('|').Append(node.Latitude.ToString("F5")).Append(',').Append(node.Longitude.ToString("F5"));
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return $"vrp:{Convert.ToHexString(hash)}";
    }

    private NormalizationReferences BuildNormalizationReferences(
        VrpInput input,
        MatrixResult matrix,
        VrpCostSettings costSettings)
    {
        var distanceSamples = new List<double>();
        var timeSamples = new List<double>();
        var costSamples = new List<double>();

        foreach (var job in input.Jobs)
        {
            var nodeIndex = input.JobNodeIndices[job.LocationId].First();
            var minDistance = double.MaxValue;
            var minTravelMinutes = double.MaxValue;

            for (var driverIndex = 0; driverIndex < input.Drivers.Count; driverIndex++)
            {
                var distance = matrix.DistanceKm[driverIndex, nodeIndex];
                var minutes = matrix.TravelMinutes[driverIndex, nodeIndex];
                if (distance < minDistance)
                {
                    minDistance = distance;
                }

                if (minutes < minTravelMinutes)
                {
                    minTravelMinutes = minutes;
                }
            }

            if (minDistance == double.MaxValue || minTravelMinutes == double.MaxValue)
            {
                continue;
            }

            distanceSamples.Add(minDistance);
            timeSamples.Add(minTravelMinutes + job.ServiceMinutes);
            costSamples.Add(CostCalculator.CalculateTravelCost(
                minDistance,
                minTravelMinutes,
                costSettings.FuelCostPerKm,
                costSettings.PersonnelCostPerHour));
        }

        var distanceRef = ComputePercentile(distanceSamples, 0.9);
        var timeRef = ComputePercentile(timeSamples, 0.9);
        var costRef = ComputePercentile(costSamples, 0.9);

        if (distanceRef <= 0) distanceRef = DefaultDistanceRefKm;
        if (timeRef <= 0) timeRef = DefaultTimeRefMinutes;
        if (costRef <= 0) costRef = DefaultCostRefEuro;

        return new NormalizationReferences(distanceRef, timeRef, costRef, DefaultOvertimeRefMinutes);
    }

    private static NormalizedWeightSet BuildNormalizedWeights(VrpWeightSet weights, bool normalizeWeights)
    {
        var time = Math.Max(0, weights.Time);
        var distance = Math.Max(0, weights.Distance);
        var date = Math.Max(0, weights.Date);
        var cost = Math.Max(0, weights.Cost);
        var overtime = Math.Max(0, weights.Overtime);

        if (cost > 0)
        {
            distance = 0;
            time = 0;
        }

        var raw = new[] { time, distance, date, cost, overtime };
        if (raw.All(value => value <= 0))
        {
            return new NormalizedWeightSet(1, 0, 0, 0, 0);
        }

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

    private static void ApplyDriverTimeWindows(
        VrpInput input,
        RoutingModel routing,
        RoutingDimension timeDimension,
        NormalizedWeightSet weights)
    {
        var overtimePenaltyPerMinute = weights.Overtime > 0
            ? (long)Math.Round((weights.Overtime * CostScale) / DefaultOvertimeRefMinutes)
            : 0;

        for (var vehicleId = 0; vehicleId < input.Drivers.Count; vehicleId++)
        {
            var driver = input.Drivers[vehicleId];
            var startIndex = routing.Start(vehicleId);
            var endIndex = routing.End(vehicleId);

            timeDimension.CumulVar(startIndex).SetRange(driver.AvailabilityStartMinute, driver.AvailabilityStartMinute);

            var hardMax = Math.Min(driver.AvailabilityEndMinute, driver.AvailabilityStartMinute + driver.MaxRouteMinutes);
            var softMax = overtimePenaltyPerMinute > 0 ? hardMax + DefaultOvertimeRefMinutes : hardMax;
            timeDimension.CumulVar(endIndex).SetMax(softMax);

            if (overtimePenaltyPerMinute > 0)
            {
                timeDimension.SetCumulVarSoftUpperBound(endIndex, hardMax, overtimePenaltyPerMinute);
            }
        }
    }

    private static void ApplyTimeCosts(
        RoutingModel routing,
        RoutingIndexManager manager,
        RoutingDimension timeDimension,
        NormalizationReferences normalizationRefs,
        NormalizedWeightSet weights,
        int vehicleCount)
    {
        if (weights.Time > 0 && normalizationRefs.TimeMinutes > 0)
        {
            var slackCostPerMinute = (long)Math.Round((weights.Time * CostScale) / normalizationRefs.TimeMinutes);
            if (slackCostPerMinute > 0)
            {
                timeDimension.SetSlackCostCoefficientForAllVehicles(slackCostPerMinute);
            }
        }

        for (var vehicleId = 0; vehicleId < vehicleCount; vehicleId++)
        {
            routing.AddVariableMinimizedByFinalizer(timeDimension.CumulVar(routing.End(vehicleId)));
        }

        for (var nodeIndex = 0; nodeIndex < manager.GetNumberOfNodes(); nodeIndex++)
        {
            var index = manager.NodeToIndex(nodeIndex);
            if (!routing.IsStart(index) && !routing.IsEnd(index))
            {
                routing.AddVariableMinimizedByFinalizer(timeDimension.CumulVar(index));
            }
        }
    }

    private static void ApplyJobTimeWindows(
        VrpInput input,
        RoutingIndexManager manager,
        RoutingDimension timeDimension)
    {
        foreach (var node in input.Nodes)
        {
            if (node.Type != VrpNodeType.Job || !input.NodeWindows.TryGetValue(node.NodeIndex, out var window))
            {
                continue;
            }

            var latestArrival = window.EndMinute - node.ServiceMinutes;
            if (latestArrival < window.StartMinute)
            {
                continue;
            }

            var index = manager.NodeToIndex(node.NodeIndex);
            timeDimension.CumulVar(index).SetRange(window.StartMinute, latestArrival);
        }
    }

    private static void ApplyServiceTypeConstraints(
        VrpInput input,
        RoutingModel routing,
        RoutingIndexManager manager)
    {
        foreach (var job in input.Jobs)
        {
            if (!input.JobNodeIndices.TryGetValue(job.LocationId, out var nodeIndices))
            {
                continue;
            }

            var allowedVehicles = new List<int>();
            for (var vehicleId = 0; vehicleId < input.Drivers.Count; vehicleId++)
            {
                if (input.Drivers[vehicleId].ServiceTypeIds.Contains(job.ServiceTypeId))
                {
                    allowedVehicles.Add(vehicleId);
                }
            }

            if (allowedVehicles.Count == 0)
            {
                continue;
            }

            foreach (var nodeIndex in nodeIndices)
            {
                var allowedVehicleIds = allowedVehicles.ToArray();
                routing.SetAllowedVehiclesForIndex(allowedVehicleIds, manager.NodeToIndex(nodeIndex));
            }
        }
    }

    private static void ApplyStopLimits(
        VrpInput input,
        RoutingModel routing,
        RoutingIndexManager manager,
        int maxStopsPerDriver)
    {
        var demandCallbackIndex = routing.RegisterUnaryTransitCallback(index =>
        {
            var node = manager.IndexToNode(index);
            return input.Nodes[node].Type == VrpNodeType.Job ? 1 : 0;
        });

        var capacities = Enumerable.Repeat((long)maxStopsPerDriver, input.Drivers.Count).ToArray();
        routing.AddDimensionWithVehicleCapacity(demandCallbackIndex, 0, capacities, true, "StopCount");
    }


    private async Task<IReadOnlyList<RouteDto>> PersistRoutesAsync(
        VrpInput input,
        IReadOnlyList<VrpRoutePlan> routes,
        int? weightTemplateId,
        CancellationToken cancellationToken)
    {
        if (routes.Count == 0)
        {
            return Array.Empty<RouteDto>();
        }

        var routeEntities = new List<RouteEntity>();

        foreach (var route in routes)
        {
            if (route.Stops.Count == 0)
            {
                continue;
            }

            var serviceTypeId = input.JobsById[route.Stops.First().ServiceLocationId].ServiceTypeId;
            var stops = route.Stops
                .Select(stop => new RouteStop
                {
                    Sequence = stop.Sequence,
                    StopType = RouteStopType.Location,
                    ServiceLocationId = stop.ServiceLocationId,
                    Latitude = stop.Latitude,
                    Longitude = stop.Longitude,
                    ServiceMinutes = stop.ServiceMinutes,
                    TravelKmFromPrev = (float)stop.TravelKmFromPrev,
                    TravelMinutesFromPrev = stop.TravelMinutesFromPrev,
                    PlannedStart = input.Date.Date.AddMinutes(stop.ArrivalMinute),
                    PlannedEnd = input.Date.Date.AddMinutes(stop.DepartureMinute),
                    Status = RouteStopStatus.Pending
                })
                .ToList();

            routeEntities.Add(new RouteEntity
            {
                Date = input.Date.Date,
                OwnerId = input.OwnerId,
                ServiceTypeId = serviceTypeId,
                DriverId = route.Driver.Id,
                Status = RouteStatus.Temp,
                TotalKm = (float)route.TotalDistanceKm,
                TotalMinutes = route.TotalMinutes,
                StartAddress = route.Driver.StartAddress,
                StartLatitude = route.Driver.StartLatitude,
                StartLongitude = route.Driver.StartLongitude,
                EndAddress = route.Driver.StartAddress,
                EndLatitude = route.Driver.StartLatitude,
                EndLongitude = route.Driver.StartLongitude,
                WeightTemplateId = weightTemplateId,
                Stops = stops
            });
        }

        if (routeEntities.Count == 0)
        {
            return Array.Empty<RouteDto>();
        }

        _dbContext.Routes.AddRange(routeEntities);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var plannedLocationIds = routeEntities
            .SelectMany(r => r.Stops)
            .Where(s => s.ServiceLocationId.HasValue)
            .Select(s => s.ServiceLocationId!.Value)
            .Distinct()
            .ToList();

        if (plannedLocationIds.Count > 0)
        {
            if (string.Equals(_dbContext.Database.ProviderName, "Microsoft.EntityFrameworkCore.InMemory", StringComparison.Ordinal))
            {
                var locations = await _dbContext.ServiceLocations
                    .Where(sl => plannedLocationIds.Contains(sl.Id))
                    .ToListAsync(cancellationToken);

                foreach (var location in locations)
                {
                    location.Status = ServiceLocationStatus.Planned;
                }

                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            else
            {
                await _dbContext.ServiceLocations
                    .Where(sl => plannedLocationIds.Contains(sl.Id))
                    .ExecuteUpdateAsync(s => s.SetProperty(sl => sl.Status, ServiceLocationStatus.Planned), cancellationToken);
            }
        }

        var routeIds = routeEntities.Select(r => r.Id).ToList();
        var savedRoutes = await _dbContext.Routes
            .Include(r => r.Driver)
            .Include(r => r.Stops)
                .ThenInclude(s => s.ServiceLocation)
            .Where(r => routeIds.Contains(r.Id))
            .ToListAsync(cancellationToken);

        return savedRoutes.Select(MapRouteDto).ToList();
    }

    private static RouteDto MapRouteDto(RouteEntity route)
    {
        return new RouteDto
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
    }

    private RoutingSearchParameters BuildSearchParameters()
    {
        var searchParameters = operations_research_constraint_solver.DefaultRoutingSearchParameters();

        searchParameters.FirstSolutionStrategy = ParseFirstSolutionStrategy(_options.FirstSolutionStrategy);
        searchParameters.LocalSearchMetaheuristic = ParseLocalSearchMetaheuristic(_options.LocalSearchMetaheuristic);
        searchParameters.TimeLimit = new Duration { Seconds = Math.Max(1, _options.TimeLimitSeconds) };
        searchParameters.SolutionLimit = Math.Max(1, _options.SolutionLimit);

        return searchParameters;
    }

    private static FirstSolutionStrategy.Types.Value ParseFirstSolutionStrategy(string? value)
    {
        if (System.Enum.TryParse<FirstSolutionStrategy.Types.Value>(value ?? string.Empty, true, out var parsed))
        {
            return parsed;
        }

        return FirstSolutionStrategy.Types.Value.PathCheapestArc;
    }

    private static LocalSearchMetaheuristic.Types.Value ParseLocalSearchMetaheuristic(string? value)
    {
        if (System.Enum.TryParse<LocalSearchMetaheuristic.Types.Value>(value ?? string.Empty, true, out var parsed))
        {
            return parsed;
        }

        return LocalSearchMetaheuristic.Types.Value.GuidedLocalSearch;
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

    private static double Clamp01(double value) => Math.Max(0, Math.Min(1, value));

    private sealed record NormalizedWeightSet(double Time, double Distance, double Date, double Cost, double Overtime);
    private sealed record NormalizationReferences(double DistanceKm, double TimeMinutes, double CostEuro, double OvertimeMinutes);
}
