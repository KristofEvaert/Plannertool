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
        var jobPrimaryNodeIndices = BuildJobPrimaryNodeIndices(input);
        var penaltyOptions = ResolvePenaltyOptions(request);
        var penaltyData = BuildPenaltyData(
            input,
            matrix,
            jobPrimaryNodeIndices,
            request.RequireServiceTypeMatch,
            normalizationRefs,
            weights,
            penaltyOptions);

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

        for (var vehicleId = 0; vehicleId < driverCount; vehicleId++)
        {
            var driverIndex = vehicleId;
            var costCallbackIndex = routing.RegisterTransitCallback((long fromIndex, long toIndex) =>
            {
                var fromNode = manager.IndexToNode(fromIndex);
                var toNode = manager.IndexToNode(toIndex);
                var distanceKm = matrix.DistanceKm[fromNode, toNode];
                var travelMinutes = matrix.TravelMinutes[fromNode, toNode];
                var serviceMinutes = input.Nodes[fromNode].ServiceMinutes;
                var timeCostMinutes = travelMinutes + serviceMinutes;
                var extraDuePenalty = 0.0;
                var extraDetourPenalty = 0.0;

                if (input.Nodes[toNode].Type == VrpNodeType.Job && input.Nodes[toNode].JobId.HasValue)
                {
                    var jobId = input.Nodes[toNode].JobId!.Value;
                    if (penaltyData.TryGetValue(jobId, out var penalties))
                    {
                        extraDuePenalty = weights.Date * penalties.DuePenaltyByDriver[driverIndex];
                        extraDetourPenalty = penalties.DetourPenaltyByDriver[driverIndex];
                    }
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
                            + (weights.Cost * normCost)
                            + extraDuePenalty
                            + extraDetourPenalty;

                return Math.Max(0, (long)Math.Round(score * CostScale));
            });

            routing.SetArcCostEvaluatorOfVehicle(costCallbackIndex, vehicleId);
        }

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
        LogPenaltyDiagnostics(input, mapped.Routes, penaltyData);
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
                distanceSamples.Add(distance);

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

        distanceRef = Math.Max(distanceRef, DefaultDistanceRefKm);

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

        var locationIds = routes
            .SelectMany(route => route.Stops)
            .Select(stop => stop.ServiceLocationId)
            .Distinct()
            .ToList();

        var instructionLookup = new Dictionary<int, List<string>>();
        if (locationIds.Count > 0)
        {
            instructionLookup = await _dbContext.ServiceLocations
                .AsNoTracking()
                .Where(sl => locationIds.Contains(sl.Id))
                .Select(sl => new { sl.Id, sl.ExtraInstructions })
                .ToDictionaryAsync(x => x.Id, x => x.ExtraInstructions, cancellationToken);
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
                    ChecklistItems = BuildChecklistItems(instructionLookup, stop.ServiceLocationId),
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
                    ChecklistItems = MapChecklistItems(s),
                    ProofStatus = s.ProofStatus.ToString(),
                    HasProofPhoto = s.ProofPhoto != null && s.ProofPhoto.Length > 0,
                    HasProofSignature = s.ProofSignature != null && s.ProofSignature.Length > 0,
                    LastUpdatedByUserId = s.LastUpdatedByUserId,
                    LastUpdatedUtc = s.LastUpdatedUtc,
                    DriverInstruction = s.ServiceLocation?.DriverInstruction
                })
                .ToList()
        };
    }

    private static List<RouteStopChecklistItem> BuildChecklistItems(
        IReadOnlyDictionary<int, List<string>> instructionLookup,
        int serviceLocationId)
    {
        if (!instructionLookup.TryGetValue(serviceLocationId, out var items) || items.Count == 0)
        {
            return new List<RouteStopChecklistItem>();
        }

        return items
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => new RouteStopChecklistItem
            {
                Text = item.Trim(),
                IsChecked = false
            })
            .ToList();
    }

    private static List<RouteStopChecklistItemDto> MapChecklistItems(RouteStop stop)
    {
        var items = stop.ChecklistItems ?? new List<RouteStopChecklistItem>();
        if (items.Count > 0)
        {
            return items
                .Where(item => !string.IsNullOrWhiteSpace(item.Text))
                .Select(item => new RouteStopChecklistItemDto
                {
                    Text = item.Text.Trim(),
                    IsChecked = item.IsChecked
                })
                .ToList();
        }

        var fallback = stop.ServiceLocation?.ExtraInstructions ?? new List<string>();
        return fallback
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => new RouteStopChecklistItemDto
            {
                Text = item.Trim(),
                IsChecked = false
            })
            .ToList();
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

    private static Dictionary<int, int> BuildJobPrimaryNodeIndices(VrpInput input)
    {
        var result = new Dictionary<int, int>();
        foreach (var entry in input.JobNodeIndices)
        {
            if (entry.Value.Count > 0)
            {
                result[entry.Key] = entry.Value[0];
            }
        }

        return result;
    }

    private static Dictionary<int, JobPenaltyInfo> BuildPenaltyData(
        VrpInput input,
        MatrixResult matrix,
        Dictionary<int, int> jobPrimaryNodeIndices,
        bool requireServiceTypeMatch,
        NormalizationReferences normalizationRefs,
        NormalizedWeightSet weights,
        SolverPenaltyOptions options)
    {
        var driverCount = input.Drivers.Count;
        var dueCapNormalized = NormalizeCap(options.DueCostCapKm, normalizationRefs.DistanceKm);
        var detourCapNormalized = NormalizeCap(options.DetourCostCapKm, normalizationRefs.DistanceKm);
        var detourRefKm = Math.Max(1, options.DetourRefKm);
        var lateRefMinutes = Math.Max(1, options.LateRefMinutes);
        var detourWeight = Math.Max(0.2, Math.Max(weights.Distance, weights.Cost));

        var penalties = new Dictionary<int, JobPenaltyInfo>();

        foreach (var job in input.Jobs)
        {
            if (!jobPrimaryNodeIndices.TryGetValue(job.LocationId, out var jobNodeIndex))
            {
                continue;
            }

            var startToJobKm = new double[driverCount];
            var startToJobMinutes = new int[driverCount];
            for (var driverIndex = 0; driverIndex < driverCount; driverIndex++)
            {
                startToJobKm[driverIndex] = matrix.DistanceKm[driverIndex, jobNodeIndex];
                startToJobMinutes[driverIndex] = matrix.TravelMinutes[driverIndex, jobNodeIndex];
            }

            var feasibleDrivers = Enumerable.Range(0, driverCount)
                .Where(idx => !requireServiceTypeMatch || input.Drivers[idx].ServiceTypeIds.Contains(job.ServiceTypeId))
                .ToList();

            var nearestKm = feasibleDrivers.Count > 0
                ? feasibleDrivers.Min(idx => startToJobKm[idx])
                : startToJobKm.Min();

            var dueEndMinute = ComputeDueEndMinute(input.Date, job.DueDate);
            var hasOnTimeDriver = false;

            if (dueEndMinute.HasValue && feasibleDrivers.Count > 0)
            {
                foreach (var driverIndex in feasibleDrivers)
                {
                    var earliest = ComputeEarliestStartMinute(
                        input.Drivers[driverIndex].AvailabilityStartMinute,
                        startToJobMinutes[driverIndex],
                        job.ServiceMinutes,
                        job.Windows);
                    if (earliest <= dueEndMinute.Value)
                    {
                        hasOnTimeDriver = true;
                        break;
                    }
                }
            }

            var duePenaltyByDriver = new double[driverCount];
            var detourPenaltyByDriver = new double[driverCount];

            for (var driverIndex = 0; driverIndex < driverCount; driverIndex++)
            {
                if (requireServiceTypeMatch && !input.Drivers[driverIndex].ServiceTypeIds.Contains(job.ServiceTypeId))
                {
                    continue;
                }

                var detourKm = Math.Max(0, startToJobKm[driverIndex] - nearestKm);
                var detourFactor = Math.Max(0, detourKm / detourRefKm);
                detourPenaltyByDriver[driverIndex] = detourFactor * detourCapNormalized * detourWeight;

                if (dueEndMinute.HasValue && job.DuePenalty > 0 && dueCapNormalized > 0)
                {
                    var earliest = ComputeEarliestStartMinute(
                        input.Drivers[driverIndex].AvailabilityStartMinute,
                        startToJobMinutes[driverIndex],
                        job.ServiceMinutes,
                        job.Windows);
                    var latenessMinutes = Math.Max(0, earliest - dueEndMinute.Value);
                    if (latenessMinutes <= 0 && hasOnTimeDriver)
                    {
                        duePenaltyByDriver[driverIndex] = job.DuePenalty * 0.2 * dueCapNormalized;
                    }
                    else
                    {
                        var latenessFactor = Clamp01(latenessMinutes / lateRefMinutes);
                        duePenaltyByDriver[driverIndex] = latenessFactor * dueCapNormalized;
                    }
                }
            }

            penalties[job.LocationId] = new JobPenaltyInfo(
                startToJobKm,
                duePenaltyByDriver,
                detourPenaltyByDriver,
                nearestKm);
        }

        return penalties;
    }

    private static int? ComputeDueEndMinute(DateTime scheduleDate, DateTime dueDate)
    {
        if (dueDate == default)
        {
            return null;
        }

        var daysOffset = (dueDate.Date - scheduleDate.Date).TotalDays;
        return (int)Math.Round(daysOffset * 1440 + 1440);
    }

    private static int ComputeEarliestStartMinute(
        int driverStartMinute,
        int travelMinutes,
        int serviceMinutes,
        IReadOnlyList<VrpTimeWindow> windows)
    {
        var candidate = driverStartMinute + travelMinutes;
        if (windows.Count == 0)
        {
            return candidate;
        }

        var ordered = windows.OrderBy(w => w.StartMinute);
        foreach (var window in ordered)
        {
            var start = Math.Max(candidate, window.StartMinute);
            if (start + serviceMinutes <= window.EndMinute)
            {
                return start;
            }
        }

        return candidate;
    }

    private static double NormalizeCap(double capKm, double referenceKm)
    {
        if (capKm <= 0 || referenceKm <= 0)
        {
            return 0;
        }

        return Clamp01(capKm / referenceKm);
    }

    private void LogPenaltyDiagnostics(
        VrpInput input,
        IReadOnlyList<VrpRoutePlan> routes,
        Dictionary<int, JobPenaltyInfo> penalties)
    {
        if (!_logger.IsEnabled(LogLevel.Debug))
        {
            return;
        }

        var driverIndexById = input.Drivers
            .Select((driver, index) => new { driver.Driver.Id, index })
            .ToDictionary(x => x.Id, x => x.index);

        foreach (var route in routes)
        {
            if (!driverIndexById.TryGetValue(route.Driver.Id, out var driverIndex))
            {
                continue;
            }

            foreach (var stop in route.Stops)
            {
                if (!penalties.TryGetValue(stop.ServiceLocationId, out var penalty))
                {
                    continue;
                }

                var assignedKm = penalty.StartToJobKm[driverIndex];
                var detourKm = Math.Max(0, assignedKm - penalty.NearestKm);
                _logger.LogDebug(
                    "VRP penalty job={JobId} driver={DriverId} nearestKm={NearestKm:F1} assignedKm={AssignedKm:F1} detourKm={DetourKm:F1} detourPenalty={DetourPenalty:F3} duePenalty={DuePenalty:F3}",
                    stop.ServiceLocationId,
                    route.Driver.Id,
                    penalty.NearestKm,
                    assignedKm,
                    detourKm,
                    penalty.DetourPenaltyByDriver[driverIndex],
                    penalty.DuePenaltyByDriver[driverIndex]);
            }
        }
    }

    private static double Clamp01(double value) => Math.Max(0, Math.Min(1, value));

    private SolverPenaltyOptions ResolvePenaltyOptions(VrpSolveRequest request)
    {
        return new SolverPenaltyOptions(
            ApplyPercentToBase(_options.DueCostCapKm, request.DueCostCapPercent),
            ApplyPercentToBase(_options.DetourCostCapKm, request.DetourCostCapPercent),
            ApplyPercentToBase(_options.DetourRefKm, request.DetourRefKmPercent),
            (int)Math.Round(ApplyPercentToBase(_options.LateRefMinutes, request.LateRefMinutesPercent)));
    }

    private static double ApplyPercentToBase(double baseValue, double? percent)
    {
        var pct = ClampPercent(percent ?? 50);
        var minValue = 1.0;
        if (baseValue <= minValue)
        {
            return minValue;
        }

        if (pct <= 50)
        {
            return minValue + (baseValue - minValue) * (pct / 50.0);
        }

        return baseValue + baseValue * ((pct - 50.0) / 50.0);
    }

    private static double ClampPercent(double value)
    {
        if (value < 0) return 0;
        if (value > 100) return 100;
        return value;
    }

    private sealed record NormalizedWeightSet(double Time, double Distance, double Date, double Cost, double Overtime);
    private sealed record NormalizationReferences(double DistanceKm, double TimeMinutes, double CostEuro, double OvertimeMinutes);
    private sealed record SolverPenaltyOptions(double DueCostCapKm, double DetourCostCapKm, double DetourRefKm, int LateRefMinutes);
    private sealed record JobPenaltyInfo(
        double[] StartToJobKm,
        double[] DuePenaltyByDriver,
        double[] DetourPenaltyByDriver,
        double NearestKm);
}
